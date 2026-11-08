// @module: Foundation.Core.Resilience
// @purpose: Captures unhandled exceptions and writes structured, sanitized JSON crash bundles to logs\crashes\.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.Text.Json
// @exports: CrashBundlePayload, CrashBundleService

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Foundation.Core;

/// <summary>
/// Canonical JSON crash bundle schema written to logs\crashes\crash_&lt;appId&gt;_&lt;timestamp&gt;.json (Standard 13).
/// </summary>
public sealed record CrashBundlePayload
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("appId")]
    public required string AppId { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("crashedAtUtc")]
    public required string CrashedAtUtc { get; init; }

    [JsonPropertyName("pid")]
    public int Pid { get; init; }

    [JsonPropertyName("osDescription")]
    public string OsDescription { get; init; } = RuntimeInformation.OSDescription;

    [JsonPropertyName("frameworkDescription")]
    public string FrameworkDescription { get; init; } = RuntimeInformation.FrameworkDescription;

    [JsonPropertyName("workingSetBytes")]
    public long WorkingSetBytes { get; init; }

    [JsonPropertyName("exceptionType")]
    public required string ExceptionType { get; init; }

    [JsonPropertyName("exceptionMessage")]
    public required string ExceptionMessage { get; init; }

    [JsonPropertyName("stackTrace")]
    public string? StackTrace { get; init; }

    [JsonPropertyName("innerException")]
    public string? InnerException { get; init; }

    [JsonPropertyName("recentLogs")]
    public IReadOnlyList<string> RecentLogs { get; init; } = Array.Empty<string>();

    [JsonPropertyName("activeJobs")]
    public object? ActiveJobs { get; init; }
}

/// <summary>
/// Registers global unhandled exception hooks and writes self-contained JSON crash dumps.
/// </summary>
public static class CrashBundleService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void RegisterGlobalHandlers(
        string logDir,
        string appId,
        string version,
        Func<IEnumerable<string>>? recentLogsProvider = null,
        Func<object?>? activeJobsProvider = null)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                TryWriteCrashBundle(
                    logDir,
                    appId,
                    version,
                    ex,
                    recentLogsProvider?.Invoke(),
                    activeJobsProvider?.Invoke());
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            TryWriteCrashBundle(
                logDir,
                appId,
                version,
                e.Exception,
                recentLogsProvider?.Invoke(),
                activeJobsProvider?.Invoke());
            e.SetObserved();
        };
    }

    public static string WriteCrashBundle(
        string logDir,
        string appId,
        string version,
        Exception ex,
        IEnumerable<string>? recentLogs = null,
        object? activeJobs = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentNullException.ThrowIfNull(ex);

        string crashesDir = Path.Combine(Path.GetFullPath(logDir), "crashes");
        Directory.CreateDirectory(crashesDir);

        DateTime utcNow = DateTime.UtcNow;
        string safeAppId = SanitizeFileNameToken(appId);
        string fileName = $"crash_{safeAppId}_{utcNow:yyyyMMdd_HHmmss_fff}.json";
        string fullPath = Path.Combine(crashesDir, fileName);

        long workingSet = 0;
        int pid = Environment.ProcessId;
        try
        {
            using var proc = Process.GetCurrentProcess();
            workingSet = proc.WorkingSet64;
        }
        catch
        {
            // Ignore process inspection failure during crash teardown
        }

        IReadOnlyList<string> tailLogs = recentLogs is null
            ? Array.Empty<string>()
            : recentLogs.TakeLast(100).ToList();

        var payload = new CrashBundlePayload
        {
            SchemaVersion = 1,
            AppId = appId,
            Version = string.IsNullOrWhiteSpace(version) ? "0.1.0" : version,
            CrashedAtUtc = utcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            Pid = pid,
            WorkingSetBytes = workingSet,
            ExceptionType = ex.GetType().FullName ?? ex.GetType().Name,
            ExceptionMessage = ex.Message,
            StackTrace = ex.StackTrace,
            InnerException = ex.InnerException?.ToString(),
            RecentLogs = tailLogs,
            ActiveJobs = activeJobs
        };

        string json = JsonSerializer.Serialize(payload, JsonOptions);
        File.WriteAllText(fullPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return fullPath;
    }

    public static string? TryWriteCrashBundle(
        string logDir,
        string appId,
        string version,
        Exception ex,
        IEnumerable<string>? recentLogs = null,
        object? activeJobs = null)
    {
        try
        {
            return WriteCrashBundle(logDir, appId, version, ex, recentLogs, activeJobs);
        }
        catch
        {
            return null;
        }
    }

    private static string SanitizeFileNameToken(string input)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : char.ToLowerInvariant(c));
        }
        return sb.Length > 0 ? sb.ToString() : "app";
    }
}
