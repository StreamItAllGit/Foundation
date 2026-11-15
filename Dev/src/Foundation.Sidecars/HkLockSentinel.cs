// @module: Foundation.Sidecars.HkLockSentinel
// @purpose: PID-reuse-safe .hklock sentinel manager, temporary/partial file filter, .coexistence.json v2 helper, and sister-app peer prober.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.Net.Http, System.Text, System.Text.Json, System.Threading, System.Threading.Tasks, Foundation.Core
// @exports: HkLockEnvelope, HkLockSentinel

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;

namespace Foundation.Sidecars;

public sealed class HkLockEnvelope
{
    public int SchemaVersion { get; set; } = 1;
    public string MachineName { get; set; } = Environment.MachineName;
    public int ProcessId { get; set; } = Environment.ProcessId;
    public string ProcessStartTimeUtc { get; set; } = "";
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime HeartbeatAtUtc { get; set; } = DateTime.UtcNow;
    public string TargetFilePath { get; set; } = "";
}

public static class HkLockSentinel
{
    private static readonly TimeSpan StaleLockMaxAge = TimeSpan.FromMinutes(90);
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static bool IsTemporaryOrPartialFileName(string? fileNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrPath)) return false;
        string name = Path.GetFileName(fileNameOrPath);
        return name.EndsWith(".hklock", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ||
               name.Contains(".partial_", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("~$", StringComparison.Ordinal);
    }

    public static bool HasActiveForeignLockInDirectory(string? directoryPath, bool honorHkLocks, out string reason, out HkLockEnvelope? activeLock)
    {
        reason = "";
        activeLock = null;
        if (!honorHkLocks || string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return false;

        foreach (string searchDir in EnumerateLockCheckDirectories(directoryPath))
        {
            try
            {
                foreach (string existingLock in Directory.EnumerateFiles(searchDir, "*.hklock", SearchOption.TopDirectoryOnly))
                {
                    if (IsActiveForeignHkLock(existingLock, out reason, out activeLock))
                        return true;
                }
            }
            catch { }
        }
        return false;
    }

    public static (bool Acquired, string? LockFilePath, string? ConflictReason) TryAcquireDirectoryHkLock(
        string directoryPath,
        string appSlug,
        bool honorHkLocks)
    {
        if (!honorHkLocks || string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return (true, null, null);

        try
        {
            foreach (string searchDir in EnumerateLockCheckDirectories(directoryPath))
            {
                foreach (string existingLock in Directory.EnumerateFiles(searchDir, "*.hklock", SearchOption.TopDirectoryOnly))
                {
                    if (IsActiveForeignHkLock(existingLock, out string reason, out _))
                        return (false, null, reason);
                    try { File.Delete(existingLock); } catch { }
                }
            }

            string cleanSlug = string.IsNullOrWhiteSpace(appSlug) ? "app" : appSlug.Trim().ToLowerInvariant();
            string ourLock = AppEnvironment.NormalizeLongPath(
                Path.Combine(directoryPath, $".{cleanSlug}_{Environment.ProcessId}.hklock"));
            long startUtcTicks = 0;
            try { using var p = Process.GetCurrentProcess(); startUtcTicks = p.StartTime.ToUniversalTime().Ticks; } catch { }

            var payload = new HkLockEnvelope
            {
                ProcessId = Environment.ProcessId,
                ProcessStartTimeUtc = startUtcTicks > 0 ? new DateTime(startUtcTicks, DateTimeKind.Utc).ToString("O") : DateTime.UtcNow.ToString("O"),
                TargetFilePath = directoryPath
            };
            using var fs = new FileStream(ourLock, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOpts));
            fs.Write(bytes, 0, bytes.Length);
            return (true, ourLock, null);
        }
        catch (IOException ex)
        {
            return (false, null, $"Lock contention in '{directoryPath}': {ex.Message}");
        }
        catch
        {
            return (true, null, null);
        }
    }

    public static bool IsActiveForeignHkLock(string lockPath, out string reason, out HkLockEnvelope? parsed)
    {
        reason = "";
        parsed = null;
        try
        {
            var fi = new FileInfo(lockPath);
            if (!fi.Exists || (DateTime.UtcNow - fi.LastWriteTimeUtc) > StaleLockMaxAge) return false;

            string rawText;
            try { rawText = File.ReadAllText(lockPath, Encoding.UTF8); }
            catch (IOException)
            {
                reason = $"Active exclusive .hklock file handle on '{Path.GetFileName(lockPath)}'.";
                return true;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(rawText) && rawText.TrimStart().StartsWith('{'))
                    parsed = JsonSerializer.Deserialize<HkLockEnvelope>(rawText, JsonOpts);
            }
            catch (JsonException) { parsed = null; }

            if (parsed == null || string.IsNullOrWhiteSpace(parsed.MachineName) || parsed.ProcessId <= 0)
            {
                reason = $"Active .hklock sentinel '{Path.GetFileName(lockPath)}' present (modified {fi.LastWriteTimeUtc:O}).";
                return true;
            }
            if ((DateTime.UtcNow - parsed.HeartbeatAtUtc.ToUniversalTime()) > StaleLockMaxAge) return false;

            if (string.Equals(parsed.MachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            {
                if (parsed.ProcessId == Environment.ProcessId) return false;
                try { using var proc = Process.GetProcessById(parsed.ProcessId); if (proc.HasExited) return false; }
                catch (ArgumentException) { return false; }
            }

            reason = $"Active Housekeeping/ShadowLibrary lock held by {parsed.MachineName} (PID {parsed.ProcessId}) since {parsed.StartedAtUtc:O}.";
            return true;
        }
        catch { return false; }
    }

    public static async Task<bool> ProbeSisterPeerOnlineAsync(int port, string healthPath = "/api/v1/health", CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(300) };
            using var resp = await http.GetAsync($"http://127.0.0.1:{port}{healthPath}", ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateLockCheckDirectories(string directoryPath)
    {
        yield return directoryPath;
        string? parent = null;
        try { parent = Directory.GetParent(directoryPath.TrimEnd('\\', '/'))?.FullName; } catch { }
        if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent)) yield return parent;
    }
}
