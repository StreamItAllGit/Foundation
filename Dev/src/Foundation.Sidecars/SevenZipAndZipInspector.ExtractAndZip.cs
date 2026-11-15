// @module: Foundation.Sidecars.SevenZipAndZipInspector.ExtractAndZip
// @purpose: Bounded entry reads, single-entry extraction, deterministic TorrentZip builder, and 7z process helpers.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.IO.Compression, System.Linq, System.Text, System.Threading, System.Threading.Tasks, Foundation.Core
// @exports: SevenZipAndZipInspector (partial)

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

namespace Foundation.Sidecars;

public static partial class SevenZipAndZipInspector
{
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
