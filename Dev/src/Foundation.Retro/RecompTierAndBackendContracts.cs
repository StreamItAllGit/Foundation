// @module: Foundation.Retro.RecompTierAndBackendContracts
// @purpose: AOT-primary execution tier modes, render backend IDs, .decomp package manifest, runtime telemetry snapshot, and deterministic AOT cache path layout.
// @depends-on: System, System.Collections.Generic, System.IO
// @exports: RecompExecutionTierMode, RecompRenderBackendIds, IDecompPackageManifest, RecompRuntimeTelemetrySnapshot, AotCacheLayoutHelper

using System;
using System.Collections.Generic;
using System.IO;

namespace Foundation.Retro;

/// <summary>
/// Execution tier mode for static recompilation runtimes.
/// Ahead-of-Time (AOT) local compilation is primary; JIT bring-up and threaded IR are optional fallbacks.
/// </summary>
public enum RecompExecutionTierMode
{
    AotPrimary = 0,
    JitBringup = 1,
    ThreadedIr = 2
}

/// <summary>
/// Canonical render backend identifiers for Foundation.Retro hosts.
/// </summary>
public static class RecompRenderBackendIds
{
    public const string Software = "software";
    public const string OpenGl33 = "opengl";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Software,
        OpenGl33
    };

    public static bool IsSupported(string? backendId) =>
        !string.IsNullOrWhiteSpace(backendId) &&
        (string.Equals(backendId.Trim(), Software, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(backendId.Trim(), OpenGl33, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Clean-room .decomp package manifest contract (Standard 18: decompSchemaVersion, targetDecompTier, goalDecompTier).
/// </summary>
public interface IDecompPackageManifest
{
    string ModuleId { get; }
    string PlatformId { get; }
    string DecompSchemaVersion { get; }
    string TargetDecompTier { get; }
    string GoalDecompTier { get; }
    string? PackageVersion { get; }
    string? UpstreamResearchPath { get; }
}

/// <summary>
/// Point-in-time runtime telemetry snapshot emitted by recompilation hosts for diagnostics and HUD overlays.
/// </summary>
public sealed record RecompRuntimeTelemetrySnapshot(
    RecompExecutionTierMode ExecutionTier,
    string RenderBackendId,
    long FrameNumber,
    double FrameTimeMs,
    double CurrentFps,
    double P99FrameMs,
    long AotCacheHits,
    long JitFallbackCount,
    bool IsAotCacheWarm,
    DateTimeOffset CapturedAtUtc
);

/// <summary>
/// Deterministic local AOT cache layout resolver under &lt;dataDir&gt;\cache\&lt;ModuleId&gt;\&lt;Version&gt;\.
/// Ensures generated binaries never pollute tracked Git trees or ship in release bundles.
/// </summary>
public static class AotCacheLayoutHelper
{
    public const string CacheRootFolderName = "cache";
    public const string BringupProfileFileName = "bringup.profile.json";

    public static string ResolveModuleCacheRoot(string dataDir, string moduleId, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDir);
        string safeModule = SanitizeSegment(moduleId, nameof(moduleId));
        string safeVersion = SanitizeSegment(version, nameof(version));

        return Path.Combine(Path.GetFullPath(dataDir), CacheRootFolderName, safeModule, safeVersion);
    }

    public static string ResolveGeneratedAssemblyPath(string dataDir, string moduleId, string version)
    {
        string safeModule = SanitizeSegment(moduleId, nameof(moduleId));
        string cacheRoot = ResolveModuleCacheRoot(dataDir, safeModule, version);
        return Path.Combine(cacheRoot, $"{safeModule}.Generated.dll");
    }

    public static string ResolveBringupProfilePath(string dataDir, string moduleId, string version)
    {
        string cacheRoot = ResolveModuleCacheRoot(dataDir, moduleId, version);
        return Path.Combine(cacheRoot, BringupProfileFileName);
    }

    private static string SanitizeSegment(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        string trimmed = value.Trim();

        if (trimmed.Contains("..", StringComparison.Ordinal) ||
            trimmed.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ':' }) >= 0)
        {
            throw new ArgumentException($"Invalid path segment '{value}' for AOT cache layout.", paramName);
        }

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            if (trimmed.IndexOf(invalid) >= 0)
            {
                throw new ArgumentException($"Invalid character '{invalid}' in AOT cache segment '{value}'.", paramName);
            }
        }

        return trimmed;
    }
}
