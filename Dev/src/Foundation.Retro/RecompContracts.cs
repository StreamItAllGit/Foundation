// @module: Foundation.Retro.RecompContracts
// @purpose: Pure-.NET static recompilation host, game module, timing/FPS patch, checksum, and render backend contracts.
// @depends-on: System, System.Collections.Generic, System.Threading, System.Threading.Tasks
// @exports: DiscChecksumEntry, IRecompModuleManifest, RecompTimingMode, RecompPatchDescriptor, RecompHostOptions, RecompFrameStats, IRecompRenderBackend, IRecompHost, IRecompGameModule

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Foundation.Retro;

public sealed record DiscChecksumEntry(
    string RelativePath,
    string ExpectedSha256,
    long? ExpectedSizeBytes = null,
    bool IsRequired = true
);

public interface IRecompModuleManifest
{
    string ModuleId { get; }
    string DisplayName { get; }
    string PlatformId { get; }
    string Version { get; }
    IReadOnlyList<DiscChecksumEntry> RequiredDiscChecksums { get; }
}

public enum RecompTimingMode
{
    LockedCap,
    Unlocked
}

public sealed record RecompPatchDescriptor(
    string PatchId,
    string DisplayName,
    double TargetFps,
    bool IsUnlocked = false,
    bool IsEnabledByDefault = false,
    string? Description = null
);

public sealed record RecompHostOptions
{
    public double TargetFps { get; init; } = 60.0;
    public bool UnlockFps { get; init; }
    public int ScaleFactor { get; init; } = 1;
    public int AudioSampleRate { get; init; } = 44100;
    public bool EnableHudOverlay { get; init; }
    public RecompExecutionTierMode ExecutionTier { get; init; } = RecompExecutionTierMode.AotPrimary;
    public string RenderBackendId { get; init; } = RecompRenderBackendIds.Software;
    public string? AotCacheDirectory { get; init; }
}

public sealed record RecompFrameStats(
    long FrameNumber,
    double FrameTimeMs,
    double CurrentFps,
    double P99FrameMs,
    double AudioFillRatio,
    long AudioUnderruns
);

public interface IRecompRenderBackend
{
    string BackendId { get; }
    int ScaleFactor { get; set; }
    void BeginFrame(int width, int height);
    void PresentRgba(int width, int height, ReadOnlySpan<byte> rgba);
}

public interface IRecompHost
{
    RecompHostOptions Options { get; }
    IRecompRenderBackend RenderBackend { get; }
    RecompFrameStats GetFrameStats();
    void SetTargetFps(double targetFps, bool unlock = false);
}

public interface IRecompGameModule
{
    IRecompModuleManifest Manifest { get; }
    IReadOnlyList<RecompPatchDescriptor> TimingPatches { get; }
    Task InitializeAsync(IRecompHost host, CancellationToken ct = default);
    Task StepFrameAsync(CancellationToken ct = default);
    Task LoadSaveStateAsync(string saveStatePath, CancellationToken ct = default);
}
