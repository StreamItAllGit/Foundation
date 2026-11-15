// @module: Foundation.Sidecars.SevenZipAndZipInspector
// @purpose: Fast ZIP central-directory and 7z -slt listing, in-memory inner stream hashing, single-entry extraction, and deterministic TorrentZip builder.
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

public static class SevenZipAndZipInspector
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
        new(new FileStream(SandboxEnvironment.NormalizeLongPath(archivePath), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), ZipArchiveMode.Read, leaveOpen: false);

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

    public static async Task<string?> ReadArchiveTextEntryAsync(
        string archivePath,
        string innerEntryPath,
        string? sevenZipExePath = null,
        int maxBytes = 262144,
        CancellationToken ct = default)
    {
        byte[]? bytes = await ReadArchiveBinaryEntryAsync(archivePath, innerEntryPath, sevenZipExePath, maxBytes, ct);
        return bytes != null ? Encoding.UTF8.GetString(bytes) : null;
    }

    public static async Task<byte[]?> ReadArchiveBinaryEntryAsync(
        string archivePath,
        string innerEntryPath,
        string? sevenZipExePath = null,
        int maxBytes = 16 * 1024 * 1024,
        CancellationToken ct = default)
    {
        if (innerEntryPath.Contains("::", StringComparison.Ordinal))
        {
            return await WithNestedArchiveResolvedAsync(
                archivePath,
                innerEntryPath,
                sevenZipExePath,
                (resolvedArchive, leafEntry) => ReadArchiveBinaryEntryAsync(resolvedArchive, leafEntry, sevenZipExePath, maxBytes, ct),
                ct).ConfigureAwait(false);
        }

        if (string.Equals(Path.GetExtension(archivePath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = OpenZipReadShared(archivePath);
            var entry = FindZipEntry(zip, innerEntryPath);
            if (entry == null) return null;
            try
            {
                await using var s = entry.Open();
                return await ReadStreamBoundedAsync(s, maxBytes, ct);
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidDataException) { }
        }

        using var proc = StartSevenZipExtract(sevenZipExePath, archivePath, innerEntryPath);
        try
        {
            _ = proc.StandardError.ReadToEndAsync(CancellationToken.None);
            return await ReadStreamBoundedAsync(proc.StandardOutput.BaseStream, maxBytes, ct);
        }
        finally { TryKillProcessTree(proc); }
    }

    public static async Task<string> ExtractSingleArchiveEntryToFileAsync(
        string archivePath,
        string innerEntryPath,
        string destinationFilePath,
        string? sevenZipExePath = null,
        CancellationToken ct = default)
    {
        if (innerEntryPath.Contains("::", StringComparison.Ordinal))
        {
            return await WithNestedArchiveResolvedAsync(
                archivePath,
                innerEntryPath,
                sevenZipExePath,
                (resolvedArchive, leafEntry) => ExtractSingleArchiveEntryToFileAsync(resolvedArchive, leafEntry, destinationFilePath, sevenZipExePath, ct),
                ct).ConfigureAwait(false);
        }

        string? destDir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrWhiteSpace(destDir))
            Directory.CreateDirectory(destDir);

        string ext = Path.GetExtension(archivePath);
        string partial = destinationFilePath + ".partial_" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            if (string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase))
            {
                bool extractedByZip = false;
                using (var zip = OpenZipReadShared(archivePath))
                {
                    var entry = FindZipEntry(zip, innerEntryPath) ?? throw new FileNotFoundException($"Inner ZIP entry '{innerEntryPath}' not found in '{archivePath}'.");
                    try
                    {
                        await using (var s = entry.Open())
                        await using (var outFs = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 131072, useAsync: true))
                            await s.CopyToAsync(outFs, 131072, ct);
                        extractedByZip = true;
                    }
                    catch (Exception ex) when (ex is NotSupportedException or InvalidDataException)
                    {
                        // Method 14 (LZMA), Deflate64, or BZip2 inside .zip: fall back to 7z.exe.
                    }
                }
                if (extractedByZip)
                {
                    File.Move(partial, destinationFilePath, overwrite: true);
                    return destinationFilePath;
                }
            }

            using var proc = StartSevenZipExtract(sevenZipExePath, archivePath, innerEntryPath);
            string stderr;
            try
            {
                Task<string> stderrTask = proc.StandardError.ReadToEndAsync(ct);
                await using (var outFs = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 131072, useAsync: true))
                    await proc.StandardOutput.BaseStream.CopyToAsync(outFs, 131072, ct);
                stderr = await stderrTask;
                await proc.WaitForExitAsync(ct);
            }
            catch { TryKillProcessTree(proc); throw; }
            if (proc.ExitCode != 0)
                throw new IOException($"7-Zip extraction failed (exit {proc.ExitCode}): {stderr}");
            File.Move(partial, destinationFilePath, overwrite: true);
            return destinationFilePath;
        }
        finally
        {
            if (File.Exists(partial)) { try { File.Delete(partial); } catch { } }
        }
    }

    public static async Task CreateDeterministicZipAsync(
        IReadOnlyList<(string SourceFile, string EntryName)> files,
        string destinationZipPath,
        CompressionLevel level = CompressionLevel.Optimal,
        CancellationToken ct = default)
    {
        string? dir = Path.GetDirectoryName(destinationZipPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var fixedTimestamp = new DateTimeOffset(1996, 12, 24, 23, 32, 0, TimeSpan.Zero);
        string partial = destinationZipPath + ".partial_" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            await using (var outFs = new FileStream(partial, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 131072, useAsync: true))
            using (var archive = new ZipArchive(outFs, ZipArchiveMode.Create, leaveOpen: false, entryNameEncoding: Encoding.UTF8))
            {
                foreach (var item in files.OrderBy(f => f.EntryName.Replace('\\', '/'), StringComparer.Ordinal))
                {
                    string cleanEntryName = item.EntryName.Replace('\\', '/').TrimStart('/');
                    var entry = archive.CreateEntry(cleanEntryName, level);
                    entry.LastWriteTime = fixedTimestamp;
                    await using var src = new FileStream(item.SourceFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 131072, useAsync: true);
                    await using var dst = entry.Open();
                    await src.CopyToAsync(dst, 131072, ct);
                }
            }

            using (var verifyZip = OpenZipReadShared(partial))
            {
                if (verifyZip.Entries.Count != files.Count)
                    throw new InvalidDataException("Deterministic ZIP entry count verification failed.");
            }

            File.Move(partial, destinationZipPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(partial)) { try { File.Delete(partial); } catch { } }
        }
    }

    public static string ResolveSevenZipExe(string? preferred = null)
    {
        var resolved = ToolResolver.ResolveTool(
            "7z",
            new[] { "7z.exe", "7za.exe" },
            preferred,
            AppDomain.CurrentDomain.BaseDirectory,
            new[] { @"C:\Program Files\7-Zip", @"C:\Program Files (x86)\7-Zip" });

        return resolved.IsAvailable && !string.IsNullOrWhiteSpace(resolved.ResolvedPath) ? resolved.ResolvedPath : "7z.exe";
    }

    private static async Task<T> WithNestedArchiveResolvedAsync<T>(
        string archivePath,
        string innerEntryPath,
        string? sevenZipExePath,
        Func<string, string, Task<T>> leafAction,
        CancellationToken ct)
    {
        string[] parts = innerEntryPath.Split("::", StringSplitOptions.TrimEntries);
        string tempDir = Path.Combine(Path.GetTempPath(), "fnd_nested_" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(tempDir);
        try
        {
            string currentArchive = archivePath;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (string.IsNullOrWhiteSpace(parts[i])) continue;
                string leafName = Path.GetFileName(parts[i].Replace('\\', '/'));
                string nextArchive = Path.Combine(tempDir, $"{i}_{leafName}");
                await ExtractSingleArchiveEntryToFileAsync(currentArchive, parts[i], nextArchive, sevenZipExePath, ct).ConfigureAwait(false);
                currentArchive = nextArchive;
            }
            return await leafAction(currentArchive, parts[^1]).ConfigureAwait(false);
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static Process StartSevenZipExtract(string? sevenZipExePath, string archivePath, string innerEntryPath)
    {
        string exe = ResolveSevenZipExe(sevenZipExePath);
        string argEntry = innerEntryPath.Replace('/', '\\');
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"x -so \"{archivePath}\" \"{argEntry}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        return Process.Start(psi) ?? throw new InvalidOperationException($"Failed to launch '{exe}'.");
    }

    private static async Task<byte[]> ReadStreamBoundedAsync(Stream s, int maxBytes, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        byte[] buf = new byte[16384];
        int read;
        while (ms.Length < maxBytes && (read = await s.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, maxBytes - ms.Length)), ct)) > 0)
            ms.Write(buf, 0, read);
        return ms.ToArray();
    }

    public static void TryKillProcessTree(Process proc)
    {
        try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
    }
}
