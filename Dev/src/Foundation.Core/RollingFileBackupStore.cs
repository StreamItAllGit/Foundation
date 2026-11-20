// @module: Foundation.Core.RollingFileBackupStore
// @purpose: Reusable Standard 39 & RecompBible R09 atomic .tmp -> Flush -> .bak rotating multi-generation file backup and restore store.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Security.Cryptography, System.Text, System.Threading, System.Threading.Tasks
// @exports: RollingBackupEntry, RollingBackupCommitResult, RollingFileBackupStore

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Foundation.Core;

public sealed record RollingBackupEntry(
    string FilePath,
    string FileName,
    long SizeBytes,
    DateTimeOffset CreatedAtUtc,
    string Sha256Hex
);

public sealed record RollingBackupCommitResult(
    string TargetFilePath,
    string? BackupFilePath,
    string Sha256Hex,
    long SizeBytes,
    int RetainedBackupCount
);

/// <summary>
/// Manages atomic <c>.tmp</c> -&gt; <c>.bak</c> writes and bounded rotating backup history
/// inside an isolated backup directory for save vaults, VDF stores, and configs.
/// </summary>
public sealed class RollingFileBackupStore
{
    public const int DefaultMaxBackups = 10;

    private static long _sequenceCounter;

    public string BackupDirectory { get; }
    public int MaxBackups { get; }

    public RollingFileBackupStore(string backupDirectory, int maxBackups = DefaultMaxBackups)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        BackupDirectory = Path.GetFullPath(backupDirectory);
        MaxBackups = Math.Max(1, maxBackups);
        Directory.CreateDirectory(BackupDirectory);
    }

    public RollingBackupEntry? CreateBackup(string sourceFilePath, string? label = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        string fullSource = Path.GetFullPath(sourceFilePath);
        if (!File.Exists(fullSource))
        {
            return null;
        }

        byte[] existingBytes = File.ReadAllBytes(fullSource);
        return CreateBackupFromBytes(fullSource, existingBytes, label);
    }

    public RollingBackupCommitResult WriteAtomicallyWithBackup(
        string targetFilePath,
        string utf8Content,
        string? label = null)
    {
        ArgumentNullException.ThrowIfNull(utf8Content);
        byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(utf8Content);
        return WriteAtomicallyWithBackup(targetFilePath, bytes.AsSpan(), label);
    }

    public RollingBackupCommitResult WriteAtomicallyWithBackup(
        string targetFilePath,
        ReadOnlySpan<byte> content,
        string? label = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFilePath);
        string fullTarget = Path.GetFullPath(targetFilePath);
        string? parentDir = Path.GetDirectoryName(fullTarget);
        if (!string.IsNullOrEmpty(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        string? backupPath = null;
        if (File.Exists(fullTarget))
        {
            byte[] priorBytes = File.ReadAllBytes(fullTarget);
            RollingBackupEntry? entry = CreateBackupFromBytes(fullTarget, priorBytes, label);
            backupPath = entry?.FilePath;
            WriteBytesAtomically(fullTarget + ".bak", priorBytes);
        }

        WriteBytesAtomically(fullTarget, content);
        string shaHex = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        int retained = ListBackups(fullTarget).Count;
        return new RollingBackupCommitResult(fullTarget, backupPath, shaHex, content.Length, retained);
    }

    public async Task<RollingBackupCommitResult> WriteAtomicallyWithBackupAsync(
        string targetFilePath,
        byte[] content,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFilePath);
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        string fullTarget = Path.GetFullPath(targetFilePath);
        string? parentDir = Path.GetDirectoryName(fullTarget);
        if (!string.IsNullOrEmpty(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        string? backupPath = null;
        if (File.Exists(fullTarget))
        {
            byte[] priorBytes = await File.ReadAllBytesAsync(fullTarget, cancellationToken).ConfigureAwait(false);
            RollingBackupEntry? entry = CreateBackupFromBytes(fullTarget, priorBytes, label);
            backupPath = entry?.FilePath;
            await WriteBytesAtomicallyAsync(fullTarget + ".bak", priorBytes, cancellationToken).ConfigureAwait(false);
        }

        await WriteBytesAtomicallyAsync(fullTarget, content, cancellationToken).ConfigureAwait(false);
        string shaHex = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        int retained = ListBackups(fullTarget).Count;
        return new RollingBackupCommitResult(fullTarget, backupPath, shaHex, content.LongLength, retained);
    }

    public IReadOnlyList<RollingBackupEntry> ListBackups(string targetFileNameOrPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFileNameOrPath);
        if (!Directory.Exists(BackupDirectory))
        {
            return Array.Empty<RollingBackupEntry>();
        }

        (string prefix, string suffix) = BuildNameAffixes(targetFileNameOrPath);
        string pattern = $"{prefix}*{suffix}";

        return Directory.EnumerateFiles(BackupDirectory, pattern, SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(fi => fi.Name, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(fi => fi.LastWriteTimeUtc)
            .Select(fi => new RollingBackupEntry(
                fi.FullName,
                fi.Name,
                fi.Length,
                new DateTimeOffset(fi.LastWriteTimeUtc, TimeSpan.Zero),
                ComputeSha256Hex(fi.FullName)))
            .ToList();
    }

    public RollingBackupEntry RestoreBackup(string backupFilePath, string targetFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFilePath);

        string safeBackup = AppEnvironment.AssertPathContained(backupFilePath, BackupDirectory);
        if (!File.Exists(safeBackup))
        {
            throw new FileNotFoundException($"Backup snapshot not found: '{safeBackup}'.", safeBackup);
        }

        string fullTarget = Path.GetFullPath(targetFilePath);
        string? parent = Path.GetDirectoryName(fullTarget);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        byte[] payload = File.ReadAllBytes(safeBackup);
        WriteBytesAtomically(fullTarget, payload);

        var fi = new FileInfo(safeBackup);
        return new RollingBackupEntry(
            safeBackup,
            fi.Name,
            payload.LongLength,
            new DateTimeOffset(fi.LastWriteTimeUtc, TimeSpan.Zero),
            Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant());
    }

    public RollingBackupEntry? RestoreLatestBackup(string targetFilePath)
    {
        IReadOnlyList<RollingBackupEntry> backups = ListBackups(targetFilePath);
        return backups.Count == 0 ? null : RestoreBackup(backups[0].FilePath, targetFilePath);
    }

    private RollingBackupEntry CreateBackupFromBytes(string fullSource, byte[] bytes, string? label)
    {
        Directory.CreateDirectory(BackupDirectory);
        (string prefix, string suffix) = BuildNameAffixes(fullSource);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        long seq = Interlocked.Increment(ref _sequenceCounter) % 10000;
        string cleanLabel = SanitizeLabel(label);
        string labelPart = string.IsNullOrEmpty(cleanLabel) ? string.Empty : $"_{cleanLabel}";
        string fileName = $"{prefix}{now:yyyyMMdd_HHmmss_fff}_{seq:D4}{labelPart}{suffix}";
        string destPath = AppEnvironment.AssertPathContained(Path.Combine(BackupDirectory, fileName), BackupDirectory);

        WriteBytesAtomically(destPath, bytes);
        PruneExcessBackups(prefix, suffix);

        string shaHex = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new RollingBackupEntry(destPath, fileName, bytes.LongLength, now, shaHex);
    }

    private void PruneExcessBackups(string prefix, string suffix)
    {
        List<FileInfo> files = Directory.EnumerateFiles(BackupDirectory, $"{prefix}*{suffix}", SearchOption.TopDirectoryOnly)
            .Select(p => new FileInfo(p))
            .OrderByDescending(fi => fi.Name, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(fi => fi.LastWriteTimeUtc)
            .ToList();

        for (int i = MaxBackups; i < files.Count; i++)
        {
            try { files[i].Delete(); } catch { /* best-effort rotation pruning */ }
        }
    }

    private static (string Prefix, string Suffix) BuildNameAffixes(string fileNameOrPath)
    {
        string baseName = Path.GetFileName(fileNameOrPath.Trim());
        string stem = Path.GetFileNameWithoutExtension(baseName);
        if (string.IsNullOrWhiteSpace(stem)) stem = "backup";
        string ext = Path.GetExtension(baseName);
        return ($"{stem}.", $".bak{ext}");
    }

    private static string SanitizeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return string.Empty;
        var sb = new StringBuilder(label.Length);
        foreach (char ch in label.Trim())
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_') sb.Append(ch);
            else if (ch == ' ') sb.Append('-');
        }
        return sb.ToString();
    }

    private static string ComputeSha256Hex(string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    private static void WriteBytesAtomically(string destPath, ReadOnlySpan<byte> bytes)
    {
        string tmpPath = $"{destPath}.tmp_{Guid.NewGuid().ToString("N")[..8]}";
        try
        {
            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
            {
                fs.Write(bytes);
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmpPath, destPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmpPath)) { try { File.Delete(tmpPath); } catch { } }
        }
    }

    private static async Task WriteBytesAtomicallyAsync(string destPath, byte[] bytes, CancellationToken ct)
    {
        string tmpPath = $"{destPath}.tmp_{Guid.NewGuid().ToString("N")[..8]}";
        try
        {
            await using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
            {
                await fs.WriteAsync(bytes, ct).ConfigureAwait(false);
                await fs.FlushAsync(ct).ConfigureAwait(false);
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmpPath, destPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmpPath)) { try { File.Delete(tmpPath); } catch { } }
        }
    }
}
