// @module: Foundation.Installer.OnlinePayload.Extraction
// @purpose: Allocation-trimmed archive wrapper detection and path-contained ZIP batch extraction for InstallerEngine.
// @depends-on: System, System.Collections.Generic, System.IO, System.IO.Compression, System.Threading, System.Threading.Tasks, Foundation.Core
// @exports: InstallerEngine (partial: DetectSingleWrapperPrefix, ExtractPayloadBatchAsync)

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace Foundation.Installer;

public sealed partial class InstallerEngine
{
    private static readonly HashSet<string> ReservedCleanRootDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "app", "data", "logs", "backup", "backups", "scripts", "config", "cache", "wwwroot", "bin", "lib"
    };

    internal static string? DetectSingleWrapperPrefix(ZipArchive zip)
    {
        ArgumentNullException.ThrowIfNull(zip);
        if (zip.Entries.Count == 0)
            return null;

        string? soleSegment = null;
        string? firstSecondSeg = null;
        bool hasMultipleSecondLevel = false;
        bool sawAppSecondSeg = false;

        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            ReadOnlySpan<char> norm = entry.FullName.AsSpan().TrimStart(['/', '\\']);
            if (norm.IsWhiteSpace())
                continue;

            int slashIdx = norm.IndexOfAny('/', '\\');
            if (slashIdx <= 0)
                return null;

            ReadOnlySpan<char> topSeg = norm[..slashIdx];
            if (soleSegment is null)
                soleSegment = topSeg.ToString();
            else if (!topSeg.Equals(soleSegment.AsSpan(), StringComparison.OrdinalIgnoreCase))
                return null;

            ReadOnlySpan<char> remainder = norm[(slashIdx + 1)..];
            if (!remainder.IsWhiteSpace())
            {
                int secondSlash = remainder.IndexOfAny('/', '\\');
                ReadOnlySpan<char> secondSeg = secondSlash >= 0 ? remainder[..secondSlash] : remainder;
                if (!secondSeg.IsWhiteSpace())
                {
                    if (secondSeg.Equals("app", StringComparison.OrdinalIgnoreCase))
                        sawAppSecondSeg = true;

                    if (firstSecondSeg is null)
                        firstSecondSeg = secondSeg.ToString();
                    else if (!secondSeg.Equals(firstSecondSeg.AsSpan(), StringComparison.OrdinalIgnoreCase))
                        hasMultipleSecondLevel = true;
                }
            }
        }

        if (soleSegment is null || firstSecondSeg is null || ReservedCleanRootDirs.Contains(soleSegment))
            return null;

        if (!hasMultipleSecondLevel && !sawAppSecondSeg)
            return null;

        return soleSegment + "/";
    }

    internal async Task ExtractPayloadBatchAsync(
        ResolvedPayloadBatch batch,
        string? externalPayloadPath,
        string installDir,
        List<string> installedRelFiles,
        HashSet<string> installedTopDirs,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        long expandedBytes = 0;
        foreach (ResolvedPayloadItem item in batch.Items)
        {
            string? cleanSubdir = InstallerManifestStore.NormalizeRelativeSubdir(item.RelativeSubdir);
            string extractRoot = string.IsNullOrEmpty(cleanSubdir)
                ? installDir
                : EnsureWithinDirectory(installDir, Path.Combine(installDir, cleanSubdir.Replace('/', Path.DirectorySeparatorChar)));
            if (!string.IsNullOrEmpty(cleanSubdir))
            {
                if (HasReparsePointInExistingHierarchy(extractRoot, _ctx.EffectiveSandbox))
                    throw new InvalidOperationException($"Refusing to extract into reparse point subdirectory '{extractRoot}'.");
                Directory.CreateDirectory(extractRoot);
                int firstSlash = cleanSubdir.IndexOf('/');
                installedTopDirs.Add(firstSlash >= 0 ? cleanSubdir[..firstSlash] : cleanSubdir);
            }

            await using Stream payloadStream = !string.IsNullOrWhiteSpace(item.LocalZipPath)
                ? File.OpenRead(item.LocalZipPath)
                : (OpenPayloadStream(externalPayloadPath) ?? throw new FileNotFoundException("Unable to open installer payload stream."));

            using var zip = new ZipArchive(payloadStream, ZipArchiveMode.Read, leaveOpen: false);
            string? wrapperPrefix = string.IsNullOrEmpty(cleanSubdir) ? DetectSingleWrapperPrefix(zip) : null;
            int totalEntries = zip.Entries.Count;
            int currentEntry = 0;
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                currentEntry++;
                string normalizedRel = entry.FullName.Replace('\\', '/').TrimStart('/');
                if (string.IsNullOrWhiteSpace(normalizedRel)) continue;
                if (wrapperPrefix is not null && normalizedRel.StartsWith(wrapperPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    normalizedRel = normalizedRel[wrapperPrefix.Length..];
                    if (string.IsNullOrWhiteSpace(normalizedRel)) continue;
                }

                string manifestRel = string.IsNullOrEmpty(cleanSubdir) ? normalizedRel : $"{cleanSubdir}/{normalizedRel}";
                int slashPos = manifestRel.IndexOf('/');
                string topSegment = slashPos >= 0 ? manifestRel[..slashPos] : manifestRel;
                if (ProtectedRuntimeNames.Contains(topSegment)) continue;

                string destPath = EnsureWithinDirectory(extractRoot, Path.Combine(extractRoot, normalizedRel.Replace('/', Path.DirectorySeparatorChar)));
                if (normalizedRel.EndsWith("/", StringComparison.Ordinal) || string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destPath);
                    if (!string.Equals(topSegment, manifestRel.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) || Directory.Exists(destPath))
                        installedTopDirs.Add(topSegment);
                    continue;
                }

                expandedBytes += entry.Length;
                if (expandedBytes > MaxArchiveExpandedBytes)
                    throw new InvalidDataException("Payload archive exceeded 2 GB uncompressed safety cap during extraction.");
                if (slashPos >= 0) installedTopDirs.Add(topSegment);

                string? parent = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                if (currentEntry == 1 || currentEntry % 15 == 0 || currentEntry == totalEntries)
                    progress?.Report($"Extracting ({currentEntry}/{totalEntries}): {manifestRel}");

                string tempDest = destPath + ".tmp_" + Guid.NewGuid().ToString("N")[..8];
                try
                {
                    await using (Stream entryStream = entry.Open())
                    await using (var outFs = new FileStream(tempDest, FileMode.Create, FileAccess.Write, FileShare.None))
                        await entryStream.CopyToAsync(outFs, ct).ConfigureAwait(false);
                    FileLockRetryHelper.MoveWithOverwrite(tempDest, destPath, onRetryLog: msg => _ctx.WriteLog("WARN", msg), ct: ct);
                    installedRelFiles.Add(manifestRel);
                }
                finally
                {
                    if (File.Exists(tempDest)) { try { File.Delete(tempDest); } catch { } }
                }
            }
        }
    }
}
