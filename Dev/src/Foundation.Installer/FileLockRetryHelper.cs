// @module: Foundation.Installer.FileLockRetryHelper
// @purpose: Bounded backoff retry helper for transient file locks and safe Sandbox/Temp test-mode install path detection.
// @depends-on: System, System.IO, System.Threading, System.Threading.Tasks
// @exports: FileLockRetryHelper

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Foundation.Installer;

/// <summary>
/// Retries transient file-lock failures using a bounded 10-attempt backoff schedule (150ms -> 250ms -> 500ms cap).
/// </summary>
public static class FileLockRetryHelper
{
    public const int DefaultMaxAttempts = 10;

    public static void ExecuteWithRetry(
        Action action, int maxAttempts = DefaultMaxAttempts, Action<string>? onRetryLog = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        int attempts = Math.Max(1, maxAttempts);
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (attempt < attempts && IsTransientLockException(ex))
            {
                int delayMs = ComputeBackoffMs(attempt - 1);
                onRetryLog?.Invoke($"Transient file lock on attempt {attempt}/{attempts}: {ex.Message}; retrying in {delayMs}ms.");
                if (ct.WaitHandle.WaitOne(delayMs)) ct.ThrowIfCancellationRequested();
            }
        }
    }

    public static async Task ExecuteWithRetryAsync(
        Action action, int maxAttempts = DefaultMaxAttempts, Action<string>? onRetryLog = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        int attempts = Math.Max(1, maxAttempts);
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (attempt < attempts && IsTransientLockException(ex))
            {
                int delayMs = ComputeBackoffMs(attempt - 1);
                onRetryLog?.Invoke($"Transient file lock on attempt {attempt}/{attempts}: {ex.Message}; retrying in {delayMs}ms.");
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        }
    }

    public static void MoveWithOverwrite(string sourcePath, string destinationPath, int maxAttempts = DefaultMaxAttempts, Action<string>? onRetryLog = null, CancellationToken ct = default) =>
        ExecuteWithRetry(() => File.Move(sourcePath, destinationPath, overwrite: true), maxAttempts, onRetryLog, ct);

    public static void CopyWithOverwrite(string sourcePath, string destinationPath, int maxAttempts = DefaultMaxAttempts, Action<string>? onRetryLog = null, CancellationToken ct = default) =>
        ExecuteWithRetry(() => File.Copy(sourcePath, destinationPath, overwrite: true), maxAttempts, onRetryLog, ct);

    public static void DeleteIfExists(string path, int maxAttempts = DefaultMaxAttempts, Action<string>? onRetryLog = null, CancellationToken ct = default) =>
        ExecuteWithRetry(() => { if (File.Exists(path)) File.Delete(path); }, maxAttempts, onRetryLog, ct);

    public static bool IsTransientLockException(Exception ex) =>
        ex is UnauthorizedAccessException ||
        (ex is IOException && ex is not FileNotFoundException && ex is not DirectoryNotFoundException && ex is not PathTooLongException);

    public static bool IsSafeTestInstallDirectory(string? candidateDir)
    {
        if (string.IsNullOrWhiteSpace(candidateDir)) return false;
        try
        {
            string full = Path.GetFullPath(candidateDir.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string? root = Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(root) || string.Equals(full, root, StringComparison.OrdinalIgnoreCase)) return false;

            string[] segments = full.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2 && string.Equals(segments[1], "Programs", StringComparison.OrdinalIgnoreCase)) return false;

            string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.StartsWith(tempRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;

            for (int i = 1; i < segments.Length; i++)
            {
                if (string.Equals(segments[i], "Sandbox", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
        catch { return false; }
    }

    private static int ComputeBackoffMs(int retryIndex) => retryIndex switch { 0 => 150, 1 => 250, _ => 500 };
}
