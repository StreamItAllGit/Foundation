// @module: Foundation.Sidecars.SevenZipAndZipInspector
// @purpose: Fast ZIP central-directory and 7z -slt listing plus streaming inner entry hashing.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.IO.Compression, System.Linq, System.Text, System.Threading, System.Threading.Tasks, Foundation.Core, Foundation.Retro
// @exports: InspectedArchiveEntry, SevenZipAndZipInspector

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;
using Foundation.Retro;

namespace Foundation.Sidecars;

public sealed class InspectedArchiveEntry
{
    public string Path { get; set; } = "";
    public long SizeBytes { get; set; }
    public long PackedBytes { get; set; }
    public string Crc32 { get; set; } = "";
    public bool IsFolder { get; set; }
}

public static partial class SevenZipAndZipInspector
{
    private static readonly string[] ArchiveExtensions = { ".zip", ".7z", ".rar" };

    public static bool IsStandardArchiveExtension(string? ext) =>
        !string.IsNullOrWhiteSpace(ext) &&
        ArchiveExtensions.Any(a => string.Equals(a, ext, StringComparison.OrdinalIgnoreCase));

    public static bool IsMultipartContinuationRar(string filePath)
    {
        string name = Path.GetFileName(filePath);
        int partIdx = name.LastIndexOf(".part", StringComparison.OrdinalIgnoreCase);
        if (partIdx < 0 || !name.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
            return false;

        string numSpan = name.Substring(partIdx + 5, name.Length - (partIdx + 5) - 4);
        return int.TryParse(numSpan, out int partNum) && partNum > 1;
    }

    public static ZipArchive OpenZipReadShared(string archivePath) =>
        new(new FileStream(AppEnvironment.NormalizeLongPath(archivePath), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), ZipArchiveMode.Read, leaveOpen: false);

    public static ZipArchiveEntry? FindZipEntry(ZipArchive zip, string innerPath)
    {
        string norm = innerPath.Replace('\\', '/').TrimStart('/');
        return zip.Entries.FirstOrDefault(e =>
            string.Equals(e.FullName.Replace('\\', '/').TrimStart('/'), norm, StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<List<InspectedArchiveEntry>> ListArchiveEntriesFastAsync(
        string archivePath,
        string? sevenZipExePath = null,
        CancellationToken ct = default)
    {
        if (archivePath.Contains("::", StringComparison.Ordinal))
        {
            string[] parts = archivePath.Split("::", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 1)
            {
                return await WithNestedArchiveResolvedAsync(
                    parts[0],
                    string.Join("::", parts, 1, parts.Length - 1) + "::",
                    sevenZipExePath,
                    (resolvedArchive, _) => ListArchiveEntriesFastAsync(resolvedArchive, sevenZipExePath, ct),
                    ct).ConfigureAwait(false);
            }
        }

        if (string.Equals(Path.GetExtension(archivePath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var zip = OpenZipReadShared(archivePath);
                return zip.Entries.Select(entry =>
                {
                    bool isDir = string.IsNullOrEmpty(entry.Name) && (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'));
                    return new InspectedArchiveEntry
                    {
                        Path = entry.FullName.Replace('\\', '/').TrimEnd('/'),
                        SizeBytes = entry.Length,
                        PackedBytes = entry.CompressedLength,
                        Crc32 = isDir ? "" : entry.Crc32.ToString("X8"),
                        IsFolder = isDir
                    };
                }).ToList();
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidDataException) { }
        }

        string exe = ResolveSevenZipExe(sevenZipExePath);
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"l -slt -sccUTF-8 \"{archivePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to launch '{exe}'.");
        try
        {
            Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            Task<string> stderrTask = proc.StandardError.ReadToEndAsync(ct);
            await Task.WhenAll(stdoutTask, stderrTask);
            await proc.WaitForExitAsync(ct);
            return ParseSevenZipSltOutput(await stdoutTask);
        }
        catch { TryKillProcessTree(proc); throw; }
    }

    public static List<InspectedArchiveEntry> ParseSevenZipSltOutput(string stdout)
    {
        var results = new List<InspectedArchiveEntry>();
        if (string.IsNullOrWhiteSpace(stdout)) return results;

        bool inEntriesSection = false;
        InspectedArchiveEntry? current = null;
        foreach (string rawLine in stdout.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (!inEntriesSection)
            {
                if (line.StartsWith("----------", StringComparison.Ordinal)) inEntriesSection = true;
                continue;
            }
            if (string.IsNullOrWhiteSpace(line))
            {
                if (current != null && !string.IsNullOrWhiteSpace(current.Path)) results.Add(current);
                current = null;
                continue;
            }

            int eq = line.IndexOf(" = ", StringComparison.Ordinal);
            if (eq <= 0) continue;
            string key = line[..eq].Trim();
            string val = line[(eq + 3)..].Trim();
            current ??= new InspectedArchiveEntry();

            if (key.Equals("Path", StringComparison.OrdinalIgnoreCase)) current.Path = val.Replace('\\', '/');
            else if (key.Equals("Size", StringComparison.OrdinalIgnoreCase) && long.TryParse(val, out long sz)) current.SizeBytes = sz;
            else if (key.Equals("Packed Size", StringComparison.OrdinalIgnoreCase) && long.TryParse(val, out long psz)) current.PackedBytes = psz;
            else if (key.Equals("CRC", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(val)) current.Crc32 = val.PadLeft(8, '0').ToUpperInvariant();
            else if (key.Equals("Folder", StringComparison.OrdinalIgnoreCase)) current.IsFolder = val == "+";
            else if (key.Equals("Attributes", StringComparison.OrdinalIgnoreCase) && val.StartsWith('D')) current.IsFolder = true;
        }

        if (current != null && !string.IsNullOrWhiteSpace(current.Path)) results.Add(current);
        return results;
    }

    public static async Task<ComputedStreamHashes> HashArchiveInnerEntryAsync(
        string archivePath,
        string innerEntryPath,
        long knownSize,
        string? sevenZipExePath = null,
        IReadOnlyList<RetroHeaderSkipRule>? headerRules = null,
        CancellationToken ct = default,
        Action<long, long>? onBytesRead = null)
    {
        if (innerEntryPath.Contains("::", StringComparison.Ordinal))
        {
            return await WithNestedArchiveResolvedAsync(
                archivePath,
                innerEntryPath,
                sevenZipExePath,
                (resolvedArchive, leafEntry) => HashArchiveInnerEntryAsync(resolvedArchive, leafEntry, knownSize, sevenZipExePath, headerRules, ct, onBytesRead),
                ct).ConfigureAwait(false);
        }

        string ext = Path.GetExtension(archivePath);
        string innerExt = Path.GetExtension(innerEntryPath);

        if (string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = OpenZipReadShared(archivePath);
            var entry = FindZipEntry(zip, innerEntryPath)
                ?? throw new FileNotFoundException($"Inner ZIP entry '{innerEntryPath}' not found in '{archivePath}'.");
            try
            {
                await using var entryStream = entry.Open();
                return await SinglePassStreamHasher.HashStreamSinglePassAsync(entryStream, entry.Length, innerExt, headerRules, includeSha256: false, ct, onBytesRead);
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidDataException) { }
        }

        using var proc = StartSevenZipExtract(sevenZipExePath, archivePath, innerEntryPath);
        try
        {
            Task<string> stderrTask = proc.StandardError.ReadToEndAsync(ct);
            var hashes = await SinglePassStreamHasher.HashStreamSinglePassAsync(proc.StandardOutput.BaseStream, knownSize, innerExt, headerRules, includeSha256: false, ct, onBytesRead);
            await stderrTask;
            await proc.WaitForExitAsync(ct);
            return hashes;
        }
        catch { TryKillProcessTree(proc); throw; }
    }
}
