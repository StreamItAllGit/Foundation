// @module: Foundation.Installer.FileLockRetryHelper
// @purpose: Bounded backoff retry helper for transient file locks and isolated/sandbox install-path safety checks.
// @depends-on: System, System.IO, System.Threading, System.Threading.Tasks, Foundation.Core
// @exports: FileLockRetryHelper

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;

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

    public static bool IsSafeIsolatedInstallDirectory(string? candidateDir) =>
        IsSafeIsolatedInstallDirectory(candidateDir, sandbox: null);

    public static bool IsSafeIsolatedInstallDirectory(string? candidateDir, SandboxResolution? sandbox = null)
    {
        if (string.IsNullOrWhiteSpace(candidateDir)) return false;
        try
        {
            string full = NormalizeCandidatePath(candidateDir);
            string? root = Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(root) || string.Equals(full, root, StringComparison.OrdinalIgnoreCase)) return false;
            if (IsCanonicalProductionOrSystemPath(full, sandbox)) return false;

            if (sandbox is { IsSandboxActive: true })
                return SandboxEnvironmentSeam.IsPathWithinSandbox(full, sandbox);

            string tempRoot = NormalizeCandidatePath(Path.GetTempPath());
            return full.StartsWith(tempRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static bool IsCanonicalProductionOrSystemPath(string? candidateDir) =>
        IsCanonicalProductionOrSystemPath(candidateDir, sandbox: null);

    public static bool IsCanonicalProductionOrSystemPath(string? candidateDir, SandboxResolution? sandbox = null)
    {
        if (string.IsNullOrWhiteSpace(candidateDir)) return false;
        try
        {
            string full = NormalizeCandidatePath(candidateDir);
            bool insideVerifiedSandbox = sandbox is { IsSandboxActive: true } &&
                SandboxEnvironmentSeam.IsPathWithinSandbox(full, sandbox);

            string[] segments = full.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            if (!insideVerifiedSandbox && segments.Length >= 2 && string.Equals(segments[1], "Programs", StringComparison.OrdinalIgnoreCase))
                return true;

            string[] protectedRoots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
            };

            foreach (string root in protectedRoots)
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                string fullProtected = NormalizeCandidatePath(root);
                if (string.Equals(full, fullProtected, StringComparison.OrdinalIgnoreCase) ||
                    full.StartsWith(fullProtected + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        catch { return false; }
    }

    private static string NormalizeCandidatePath(string rawPath)
    {
        string trimmed = rawPath.Trim().Trim('"');
        if (trimmed.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            trimmed = @"\\" + trimmed[8..];
        else if (trimmed.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[4..];
        return Path.GetFullPath(trimmed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static int ComputeBackoffMs(int retryIndex) => retryIndex switch { 0 => 150, 1 => 250, _ => 500 };
}
