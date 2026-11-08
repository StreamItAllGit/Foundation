// @module: Foundation.Core.Concurrency
// @purpose: Implements Standard 19 §4 PID-safe JSON lockfiles with (pid, processStartTimeUtc) verification and orphan lock breaking.
// @depends-on: System, System.Diagnostics, System.IO, System.Text.Json
// @exports: PidSafeLockEnvelope, PidSafeLockfile

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foundation.Core;

/// <summary>
/// Canonical JSON lockfile envelope per Standard 19 §4.
/// </summary>
public sealed record PidSafeLockEnvelope
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("appId")]
    public required string AppId { get; init; }

    [JsonPropertyName("pid")]
    public required int Pid { get; init; }

    [JsonPropertyName("processStartTimeUtc")]
    public required string ProcessStartTimeUtc { get; init; }

    [JsonPropertyName("hostName")]
    public required string HostName { get; init; }

    [JsonPropertyName("operation")]
    public required string Operation { get; init; }

    [JsonPropertyName("acquiredAtUtc")]
    public required string AcquiredAtUtc { get; init; }

    [JsonPropertyName("heartbeatAtUtc")]
    public required string HeartbeatAtUtc { get; init; }

    [JsonPropertyName("expiresAtUtc")]
    public required string ExpiresAtUtc { get; init; }
}

/// <summary>
/// Manages PID-safe lockfiles (e.g. .hklock / .sllock) that verify both PID and process start time
/// and automatically break stale orphan locks left behind by crashed processes.
/// </summary>
public sealed class PidSafeLockfile : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private bool _disposed;

    public string LockFilePath { get; }
    public PidSafeLockEnvelope Envelope { get; private set; }

    private PidSafeLockfile(string lockFilePath, PidSafeLockEnvelope envelope)
    {
        LockFilePath = lockFilePath;
        Envelope = envelope;
    }

    public static PidSafeLockfile Acquire(
        string lockFilePath,
        string appId,
        string operation,
        TimeSpan? leaseDuration = null)
    {
        if (!TryAcquire(lockFilePath, appId, operation, out PidSafeLockfile? handle, out PidSafeLockEnvelope? blocking, leaseDuration) || handle is null)
        {
            throw new IOException(
                $"Lockfile '{lockFilePath}' is actively held by live process '{blocking?.AppId}' (PID {blocking?.Pid}, operation '{blocking?.Operation}').");
        }
        return handle;
    }

    public static bool TryAcquire(
        string lockFilePath,
        string appId,
        string operation,
        out PidSafeLockfile? lockHandle,
        out PidSafeLockEnvelope? activeHolder,
        TimeSpan? leaseDuration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);

        string fullPath = Path.GetFullPath(lockFilePath);
        string? dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (IsLockedByLiveProcess(fullPath, out activeHolder))
        {
            lockHandle = null;
            return false;
        }

        // If an orphan lock exists on disk from a dead or recycled PID, break it cleanly.
        if (File.Exists(fullPath))
        {
            try { File.Delete(fullPath); } catch { /* ignore */ }
        }

        DateTime nowUtc = DateTime.UtcNow;
        TimeSpan ttl = leaseDuration ?? TimeSpan.FromMinutes(15);
        var envelope = new PidSafeLockEnvelope
        {
            SchemaVersion = 1,
            AppId = appId,
            Pid = Environment.ProcessId,
            ProcessStartTimeUtc = GetCurrentProcessStartTimeUtcString(),
            HostName = Environment.MachineName,
            Operation = string.IsNullOrWhiteSpace(operation) ? "exclusive-lock" : operation,
            AcquiredAtUtc = nowUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            HeartbeatAtUtc = nowUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            ExpiresAtUtc = nowUtc.Add(ttl).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };

        WriteEnvelopeAtomically(fullPath, envelope);
        activeHolder = envelope;
        lockHandle = new PidSafeLockfile(fullPath, envelope);
        return true;
    }

    public void RefreshHeartbeat(TimeSpan? leaseDuration = null)
    {
        if (_disposed)
        {
            return;
        }

        DateTime nowUtc = DateTime.UtcNow;
        TimeSpan ttl = leaseDuration ?? TimeSpan.FromMinutes(15);
        Envelope = Envelope with
        {
            HeartbeatAtUtc = nowUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            ExpiresAtUtc = nowUtc.Add(ttl).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };
        WriteEnvelopeAtomically(LockFilePath, Envelope);
    }

    /// <summary>
    /// Inspects a lockfile on disk and returns true ONLY if the holding process is still alive with a matching start time
    /// and an unexpired lease.
    /// </summary>
    public static bool IsLockedByLiveProcess(string lockFilePath, out PidSafeLockEnvelope? envelope)
    {
        envelope = null;
        if (string.IsNullOrWhiteSpace(lockFilePath) || !File.Exists(lockFilePath))
        {
            return false;
        }

        try
        {
            string raw = File.ReadAllText(lockFilePath, Encoding.UTF8);
            envelope = JsonSerializer.Deserialize<PidSafeLockEnvelope>(raw, JsonOptions);
            if (envelope is null || envelope.Pid <= 0)
            {
                return false;
            }

            if (DateTimeOffset.TryParse(envelope.ExpiresAtUtc, out DateTimeOffset expiresAt) &&
                expiresAt < DateTimeOffset.UtcNow)
            {
                return false;
            }

            if (!string.Equals(envelope.HostName, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            {
                // Cross-host network lock is respected until its lease expires.
                return true;
            }

            using Process proc = Process.GetProcessById(envelope.Pid);
            if (proc.HasExited)
            {
                return false;
            }

            if (DateTimeOffset.TryParse(envelope.ProcessStartTimeUtc, out DateTimeOffset expectedStart))
            {
                DateTime actualStartUtc = proc.StartTime.ToUniversalTime();
                double driftSeconds = Math.Abs((actualStartUtc - expectedStart.UtcDateTime).TotalSeconds);
                if (driftSeconds > 2.0)
                {
                    return false;
                }
            }

            return true;
        }
        catch (ArgumentException)
        {
            // Process with envelope.Pid is not running.
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static string GetCurrentProcessStartTimeUtcString()
    {
        try
        {
            using Process current = Process.GetCurrentProcess();
            return current.StartTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        }
        catch
        {
            return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        }
    }

    private static void WriteEnvelopeAtomically(string path, PidSafeLockEnvelope envelope)
    {
        string tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            string json = JsonSerializer.Serialize(envelope, JsonOptions);
            File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* ignore */ }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        try
        {
            if (File.Exists(LockFilePath))
            {
                File.Delete(LockFilePath);
            }
        }
        catch
        {
            // Ignore cleanup failure on exit
        }
    }
}
