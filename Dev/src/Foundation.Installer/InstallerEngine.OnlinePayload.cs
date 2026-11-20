// @module: Foundation.Installer.OnlinePayload
// @purpose: Resolves, downloads (HTTPS + SHA-256 + byte-size verified via SafeNetworkTransfer), validates --offline-bundle payloads, and extracts profile assets safely.
// @depends-on: System, System.Collections.Generic, System.IO, System.IO.Compression, System.Linq, System.Net.Http, System.Security.Cryptography, System.Threading, System.Threading.Tasks, Foundation.Core
// @exports: InstallerEngine (partial: online & offline payload resolution, ZIP verification, and bounded extraction)

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;

namespace Foundation.Installer;

public sealed partial class InstallerEngine
{
    public HttpMessageHandler? CustomHttpHandler { get; set; }
    public bool AllowLoopbackHttpForTesting { get; set; }

    internal sealed record ResolvedPayloadItem(
        string DisplayLabel,
        string? LocalZipPath,
        string? RelativeSubdir,
        string Sha256Hex,
        long SizeBytes,
        int EntryCount);

    internal sealed class ResolvedPayloadBatch : IAsyncDisposable
    {
        public bool Valid { get; init; }
        public string Message { get; init; } = string.Empty;
        public string CombinedSha256 { get; init; } = string.Empty;
        public long TotalBytes { get; init; }
        public int TotalEntries { get; init; }
        public List<ResolvedPayloadItem> Items { get; init; } = new();
        public string? StagingTempDir { get; init; }

        public ValueTask DisposeAsync()
        {
            if (!string.IsNullOrWhiteSpace(StagingTempDir) && Directory.Exists(StagingTempDir))
            {
                try { Directory.Delete(StagingTempDir, recursive: true); } catch { /* best-effort temp cleanup */ }
            }
            return ValueTask.CompletedTask;
        }
    }

    public IReadOnlyList<InstallerPayloadAsset> SelectPayloadAssetsForProfile(string? installProfile)
    {
        if (Descriptor.PayloadAssets is null || Descriptor.PayloadAssets.Count == 0)
            return Array.Empty<InstallerPayloadAsset>();

        string profile = string.IsNullOrWhiteSpace(installProfile)
            ? Descriptor.DefaultProfile.Trim().ToLowerInvariant()
            : installProfile.Trim().ToLowerInvariant();

        return Descriptor.PayloadAssets
            .Where(a =>
            {
                string ap = (a.InstallProfile ?? "lightweight").Trim().ToLowerInvariant();
                if (ap is "*" or "all" or "common") return true;
                if (profile == "full") return ap is "full" or "lightweight";
                return string.Equals(ap, profile, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
    }

    internal async Task<ResolvedPayloadBatch> ResolvePayloadBatchAsync(
        string? externalPayloadPath,
        string? offlineBundlePath,
        string? installProfile,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(externalPayloadPath) || !Descriptor.IsOnlineSetup)
        {
            var local = await VerifyLocalPayloadStreamAsync(externalPayloadPath, ct).ConfigureAwait(false);
            if (!local.Valid)
                return new ResolvedPayloadBatch { Valid = false, Message = local.Message };

            return new ResolvedPayloadBatch
            {
                Valid = true,
                Message = local.Message,
                CombinedSha256 = local.Sha256,
                TotalBytes = local.SizeBytes,
                TotalEntries = local.EntryCount,
                Items = new List<ResolvedPayloadItem>
                {
                    new(externalPayloadPath ?? Descriptor.EffectiveEmbeddedPayloadResource,
                        !string.IsNullOrWhiteSpace(externalPayloadPath) ? Path.GetFullPath(externalPayloadPath) : null,
                        null, local.Sha256, local.SizeBytes, local.EntryCount)
                }
            };
        }

        if (!string.IsNullOrWhiteSpace(offlineBundlePath))
        {
            return await ResolveFromOfflineBundleAsync(offlineBundlePath, installProfile, ct).ConfigureAwait(false);
        }

        return await ResolveFromRemoteOnlineSetupAsync(installProfile, progress, ct).ConfigureAwait(false);
    }

    private async Task<ResolvedPayloadBatch> ResolveFromOfflineBundleAsync(
        string offlineBundlePath,
        string? installProfile,
        CancellationToken ct)
    {
        string fullBundle = Path.GetFullPath(offlineBundlePath.Trim());
        IReadOnlyList<InstallerPayloadAsset> selected = SelectPayloadAssetsForProfile(installProfile);
        var items = new List<ResolvedPayloadItem>();

        if (File.Exists(fullBundle))
        {
            string bundleName = Path.GetFileName(fullBundle);
            InstallerPayloadAsset? matched = selected.FirstOrDefault(a =>
                string.Equals(Path.GetFileName(a.FileName), bundleName, StringComparison.OrdinalIgnoreCase))
                ?? selected.FirstOrDefault();

            var check = await VerifySingleZipFileAsync(
                fullBundle, matched?.ExpectedSizeBytes, matched?.ExpectedSha256, ct).ConfigureAwait(false);
            if (!check.Valid)
            {
                return new ResolvedPayloadBatch { Valid = false, Message = BuildOfflineSetupFailureMessage(check.Message) };
            }

            items.Add(new ResolvedPayloadItem(bundleName, fullBundle, matched?.RelativeSubdir, check.Sha256, check.SizeBytes, check.EntryCount));
            return BuildBatchSummary(items);
        }

        if (Directory.Exists(fullBundle))
        {
            if (selected.Count == 0)
            {
                return new ResolvedPayloadBatch
                {
                    Valid = false,
                    Message = BuildOfflineSetupFailureMessage($"No payloadAssets[] entries match profile '{installProfile ?? Descriptor.DefaultProfile}'.")
                };
            }

            foreach (InstallerPayloadAsset asset in selected)
            {
                string safeName = Path.GetFileName(asset.FileName);
                string assetPath = EnsureWithinDirectory(fullBundle, Path.Combine(fullBundle, safeName));
                if (!File.Exists(assetPath))
                {
                    return new ResolvedPayloadBatch
                    {
                        Valid = false,
                        Message = BuildOfflineSetupFailureMessage($"Missing '{safeName}' inside offline bundle directory '{fullBundle}'.")
                    };
                }

                var check = await VerifySingleZipFileAsync(
                    assetPath, asset.ExpectedSizeBytes > 0 ? asset.ExpectedSizeBytes : null, asset.ExpectedSha256, ct).ConfigureAwait(false);
                if (!check.Valid)
                {
                    return new ResolvedPayloadBatch { Valid = false, Message = BuildOfflineSetupFailureMessage(check.Message) };
                }

                items.Add(new ResolvedPayloadItem(safeName, assetPath, asset.RelativeSubdir, check.Sha256, check.SizeBytes, check.EntryCount));
            }
            return BuildBatchSummary(items);
        }

        return new ResolvedPayloadBatch
        {
            Valid = false,
            Message = BuildOfflineSetupFailureMessage($"Offline bundle path not found: '{fullBundle}'.")
        };
    }

    private async Task<ResolvedPayloadBatch> ResolveFromRemoteOnlineSetupAsync(
        string? installProfile,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Descriptor.PayloadDownloadUrl) ||
            !Uri.TryCreate(Descriptor.PayloadDownloadUrl.Trim(), UriKind.Absolute, out Uri? baseUri))
        {
            return new ResolvedPayloadBatch
            {
                Valid = false,
                Message = BuildOfflineSetupFailureMessage("Online Setup manifest is missing a valid absolute payloadDownloadUrl.")
            };
        }

        IReadOnlyList<InstallerPayloadAsset> selected = SelectPayloadAssetsForProfile(installProfile);
        if (selected.Count == 0)
        {
            return new ResolvedPayloadBatch
            {
                Valid = false,
                Message = BuildOfflineSetupFailureMessage($"Online Setup manifest defines no payloadAssets[] for profile '{installProfile ?? Descriptor.DefaultProfile}'.")
            };
        }

        string tempRoot = _ctx.IsSandboxActive
            ? Path.Combine(KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, _ctx.EffectiveSandbox), "Temp")
            : Path.GetTempPath();
        string stagingDir = Path.Combine(tempRoot, $"FoundationOnline_{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(stagingDir);

        var items = new List<ResolvedPayloadItem>();
        try
        {
            foreach (InstallerPayloadAsset asset in selected)
            {
                if (string.IsNullOrWhiteSpace(asset.ExpectedSha256))
                {
                    throw new InvalidDataException($"Online Setup asset '{asset.FileName}' is missing required pinned expectedSha256.");
                }

                string safeFileName = Path.GetFileName(asset.FileName);
                Uri assetUri = BuildAssetDownloadUri(baseUri, safeFileName);
                string stagedPath = EnsureWithinDirectory(stagingDir, Path.Combine(stagingDir, safeFileName));

                progress?.Report($"Downloading verified payload '{safeFileName}'...");
                SafeTransferResult transfer = await SafeNetworkTransfer.DownloadVerifiedAsync(
                    assetUri,
                    stagedPath,
                    expectedSha256Hex: asset.ExpectedSha256,
                    expectedSizeBytes: asset.ExpectedSizeBytes > 0 ? asset.ExpectedSizeBytes : null,
                    requireHttps: true,
                    allowLoopbackHttp: AllowLoopbackHttpForTesting,
                    onProgress: (done, total) =>
                    {
                        if (total is > 0)
                        {
                            double pct = Math.Clamp(done * 100.0 / total.Value, 0.0, 100.0);
                            progress?.Report($"Downloading {safeFileName}: {pct:F0}% ({done / (1024.0 * 1024.0):F1} MB)");
                        }
                    },
                    customHandler: CustomHttpHandler,
                    cancellationToken: ct).ConfigureAwait(false);

                var check = await VerifySingleZipFileAsync(stagedPath, null, asset.ExpectedSha256, ct).ConfigureAwait(false);
                if (!check.Valid)
                {
                    throw new InvalidDataException(check.Message);
                }

                items.Add(new ResolvedPayloadItem(safeFileName, transfer.FinalFilePath, asset.RelativeSubdir, check.Sha256, check.SizeBytes, check.EntryCount));
            }

            return BuildBatchSummary(items, stagingDir);
        }
        catch (Exception ex)
        {
            try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true); } catch { }
            string msg = BuildOfflineSetupFailureMessage(ex.Message);
            _ctx.WriteLog("ERROR", msg);
            return new ResolvedPayloadBatch { Valid = false, Message = msg };
        }
    }

    private static Uri BuildAssetDownloadUri(Uri baseUri, string fileName)
    {
        if (string.Equals(Path.GetFileName(baseUri.AbsolutePath.TrimEnd('/')), fileName, StringComparison.OrdinalIgnoreCase))
            return baseUri;

        string rootText = baseUri.AbsoluteUri.EndsWith('/') ? baseUri.AbsoluteUri : baseUri.AbsoluteUri + "/";
        return new Uri(new Uri(rootText, UriKind.Absolute), Uri.EscapeDataString(fileName));
    }

    private string BuildOfflineSetupFailureMessage(string reason) =>
        $"Online Setup failed to retrieve a verified payload for {Descriptor.DisplayName} v{InstallerVersion}. " +
        $"Reason: {reason} " +
        $"Remedy: Check connectivity or download '{Descriptor.ProductName}-Offline-Setup-v{InstallerVersion}.exe' or '{Descriptor.ProductName}-v{InstallerVersion}-Portable.zip', or re-run with '--offline-bundle <zipOrDir>'.";

    private ResolvedPayloadBatch BuildBatchSummary(List<ResolvedPayloadItem> items, string? stagingDir = null)
    {
        long totalBytes = items.Sum(i => i.SizeBytes);
        int totalEntries = items.Sum(i => i.EntryCount);
        string combinedSha = items.Count == 1
            ? items[0].Sha256Hex
            : Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join('|', items.Select(i => i.Sha256Hex))))).ToLowerInvariant();
        string msg = $"Verified {Descriptor.DisplayName} v{InstallerVersion} payload ({totalEntries:N0} files, {totalBytes:N0} compressed bytes, SHA-256: {combinedSha}).";
        _ctx.WriteLog("INFO", msg);
        return new ResolvedPayloadBatch
        {
            Valid = true,
            Message = msg,
            CombinedSha256 = combinedSha,
            TotalBytes = totalBytes,
            TotalEntries = totalEntries,
            Items = items,
            StagingTempDir = stagingDir
        };
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
                installedTopDirs.Add(cleanSubdir.Split('/')[0]);
            }

            await using Stream payloadStream = !string.IsNullOrWhiteSpace(item.LocalZipPath)
                ? File.OpenRead(item.LocalZipPath)
                : (OpenPayloadStream(externalPayloadPath) ?? throw new FileNotFoundException("Unable to open installer payload stream."));

            using var zip = new ZipArchive(payloadStream, ZipArchiveMode.Read, leaveOpen: false);
            int totalEntries = zip.Entries.Count;
            int currentEntry = 0;
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                currentEntry++;
                string normalizedRel = entry.FullName.Replace('\\', '/').TrimStart('/');
                if (string.IsNullOrWhiteSpace(normalizedRel)) continue;

                string manifestRel = string.IsNullOrEmpty(cleanSubdir) ? normalizedRel : $"{cleanSubdir}/{normalizedRel}";
                string topSegment = manifestRel.Split('/', 2)[0];
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
                if (manifestRel.Contains('/')) installedTopDirs.Add(topSegment);

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
