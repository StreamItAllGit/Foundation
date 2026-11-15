// @module: Foundation.Tests.V031Primitives
// @purpose: Unit tests for Foundation Core timing/audio/PNG/environment primitives, Retro MediaContainerExtractor/RecompContracts, and Installer security/layout features.
// @depends-on: System, System.Buffers.Binary, System.Collections.Generic, System.IO, System.IO.Compression, System.Security.Cryptography, System.Text, System.Threading, System.Threading.Tasks, System.Windows.Forms, Foundation.Core, Foundation.Retro, Foundation.Installer, Xunit
// @exports: FoundationV031PrimitivesTests

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Foundation.Core;
using Foundation.Installer;
using Foundation.Retro;
using Xunit;

namespace Foundation.Tests;

public sealed class FoundationV031PrimitivesTests : IDisposable
{
    private readonly string _tempDir;

    public FoundationV031PrimitivesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FoundationV031Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void HighResFramePacer_TracksRollingTelemetry_And_SupportsUnlockedMode()
    {
        using var pacer = new HighResFramePacer(targetFps: 120.0);
        Assert.Equal(120.0, pacer.TargetFps);

        for (int i = 0; i < 3; i++)
        {
            double ms = pacer.WaitForNextFrame();
            Assert.True(ms >= 0.0);
        }

        Assert.Equal(3, pacer.TotalFrames);
        Assert.True(pacer.AverageFrameMs > 0.0);
        Assert.True(pacer.CurrentFps > 0.0);
        Assert.True(pacer.P99FrameMs >= pacer.AverageFrameMs * 0.5);

        pacer.TargetFps = 0.0; // Unlocked
        pacer.Reset();
        pacer.WaitForNextFrame();
        Assert.Equal(4, pacer.TotalFrames);
        pacer.Dispose();
    }

    [Fact]
    public void AudioRingBuffer_SupportsShortAndFloatSpsc_And_SmoothsUnderrunToZero()
    {
        var ring = new AudioRingBuffer(capacitySamples: 64, channels: 2);
        short[] inputPcm = [16000, -16000, 16000, -16000];
        Assert.Equal(4, ring.Write(inputPcm));
        Assert.Equal(4, ring.BufferedSamples);
        Assert.True(ring.FillRatio > 0.0);

        Span<short> outPcm = stackalloc short[16];
        int read = ring.Read(outPcm);
        Assert.Equal(4, read);
        Assert.Equal(1, ring.UnderrunCount);
        Assert.True(Math.Abs((int)outPcm[4]) > 0 && Math.Abs((int)outPcm[4]) < 16000);
        Assert.Equal(0, outPcm[14]);
        Assert.Equal(0, outPcm[15]);

        float[] floatIn = [0.25f, -0.25f];
        Assert.Equal(2, ring.Write(floatIn));
        Span<float> floatOut = stackalloc float[2];
        Assert.Equal(2, ring.Read(floatOut));
        Assert.InRange(floatOut[0], 0.24f, 0.26f);
    }

    [Fact]
    public void WavCaptureWriter_RgbaPngWriter_And_PixelOverlayCanvas_EmitValidHeaders()
    {
        string wavPath = Path.Combine(_tempDir, "capture.wav");
        using (var wav = new WavCaptureWriter(wavPath, sampleRate: 44100, channels: 2))
        {
            short[] pcm = new short[100];
            pcm[0] = 1234;
            pcm[1] = -1234;
            wav.WriteInterleavedSamples(pcm);
            Assert.Equal(100, wav.TotalSamplesWritten);
        }

        byte[] wavBytes = File.ReadAllBytes(wavPath);
        Assert.Equal(244, wavBytes.Length);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wavBytes, 0, 4));
        Assert.Equal(236u, BinaryPrimitives.ReadUInt32LittleEndian(wavBytes.AsSpan(4, 4)));
        Assert.Equal("WAVEfmt ", Encoding.ASCII.GetString(wavBytes, 8, 8));
        Assert.Equal("data", Encoding.ASCII.GetString(wavBytes, 36, 4));
        Assert.Equal(200u, BinaryPrimitives.ReadUInt32LittleEndian(wavBytes.AsSpan(40, 4)));

        var canvas = new PixelOverlayCanvas(48, 24);
        canvas.Clear(new RgbaColor(14, 17, 23, 255));
        canvas.FillRect(2, 2, 44, 20, new RgbaColor(14, 165, 233, 128));
        canvas.StrokeRect(2, 2, 44, 20, new RgbaColor(255, 255, 255, 255));
        var textSize = canvas.MeasureText("60 FPS");
        Assert.True(textSize.Width > 0 && textSize.Height == 7);
        canvas.DrawText(4, 6, "60 FPS", new RgbaColor(255, 255, 255, 255));

        string pngPath = Path.Combine(_tempDir, "hud.png");
        canvas.SavePng(pngPath);
        byte[] png = File.ReadAllBytes(pngPath);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);
        Assert.Equal("IHDR", Encoding.ASCII.GetString(png, 12, 4));
        Assert.Contains("IDAT", Encoding.ASCII.GetString(png));
        Assert.Equal("IEND", Encoding.ASCII.GetString(png, png.Length - 8, 4));
    }

    [Fact]
    public void AppEnvironment_PresentationModes_ResolveTrayOrBackgroundAndDesktopWindow()
    {
        var desktop = new AppEnvironment("restation", new[] { "--window", "--data-dir", _tempDir });
        Assert.Equal(UiPresentationMode.DesktopWindow, desktop.PresentationMode);

        var browser = new AppEnvironment("restation", new[] { "--browser", "--data-dir", _tempDir });
        Assert.Equal(UiPresentationMode.LocalBrowser, browser.PresentationMode);

        var tray = new AppEnvironment("restation", new[] { "--tray-only", "--data-dir", _tempDir });
        Assert.True(tray.TrayOnly);
        Assert.Equal(UiPresentationMode.TrayOrBackground, tray.PresentationMode);
    }

    [Fact]
    public async Task MediaContainerExtractor_VerifiesZipBinCueChecksums_RejectsTraversal_And_HandlesMissingTools()
    {
        byte[] binBytes = Encoding.UTF8.GetBytes("FAKE_PS1_SECTOR_DATA");
        string binSha = Convert.ToHexString(SHA256.HashData(binBytes)).ToLowerInvariant();
        string zipPath = Path.Combine(_tempDir, "disc.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var binEntry = zip.CreateEntry("track01.bin");
            await using (var s = binEntry.Open()) await s.WriteAsync(binBytes);
            var cueEntry = zip.CreateEntry("disc.cue");
            await using (var sw = new StreamWriter(cueEntry.Open())) await sw.WriteAsync("FILE \"track01.bin\" BINARY\n  TRACK 01 MODE2/2352\n    INDEX 01 00:00:00\n");
        }

        string outDir = Path.Combine(_tempDir, "extracted");
        MediaExtractionResult ok = await MediaContainerExtractor.ExtractOrVerifyAsync(
            zipPath, outDir, new MediaExtractionOptions(ExpectedChecksums: new[] { new DiscChecksumEntry("track01.bin", binSha, binBytes.Length) }));
        Assert.True(ok.Success);
        Assert.Equal(MediaContainerFormat.Zip, ok.Format);
        Assert.NotNull(ok.CueSheetPath);

        MediaExtractionResult badHash = await MediaContainerExtractor.ExtractOrVerifyAsync(
            ok.CueSheetPath!, Path.Combine(_tempDir, "verify_cue"),
            new MediaExtractionOptions(ExpectedChecksums: new[] { new DiscChecksumEntry("track01.bin", new string('0', 64)) }));
        Assert.False(badHash.Success);

        string slipZip = Path.Combine(_tempDir, "slip.zip");
        using (var zip = ZipFile.Open(slipZip, ZipArchiveMode.Create))
        {
            var bad = zip.CreateEntry("../escape.bin");
            await using var s = bad.Open();
            await s.WriteAsync(binBytes);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MediaContainerExtractor.ExtractOrVerifyAsync(slipZip, Path.Combine(_tempDir, "slip_out")));

        string missingCue = Path.Combine(_tempDir, "missing.cue");
        await File.WriteAllTextAsync(missingCue, "FILE \"does_not_exist.bin\" BINARY\n");
        MediaExtractionResult missingTrack = await MediaContainerExtractor.ExtractOrVerifyAsync(missingCue, _tempDir);
        Assert.False(missingTrack.Success);

        string dummyChd = Path.Combine(_tempDir, "game.chd");
        await File.WriteAllBytesAsync(dummyChd, [1, 2, 3, 4]);
        MediaExtractionResult missingTool = await MediaContainerExtractor.ExtractOrVerifyAsync(
            dummyChd, Path.Combine(_tempDir, "chd_out"),
            new MediaExtractionOptions(ChdmanOverridePath: Path.Combine(_tempDir, "missing_chdman.exe")));
        Assert.False(missingTool.Success);
        Assert.True(missingTool.IsToolMissingFallback || !string.IsNullOrWhiteSpace(missingTool.DiagnosticMessage));
    }

    [Fact]
    public async Task RecompContracts_TimingPatchesAndHostOptions_WorkCleanly()
    {
        var manifest = new SampleManifest();
        var host = new SampleRecompHost();
        var module = new SampleGameModule(manifest);
        await module.InitializeAsync(host);
        host.SetTargetFps(0, unlock: true);
        Assert.True(host.Options.UnlockFps);
        Assert.Single(module.TimingPatches);
        Assert.Equal(RecompExecutionTierMode.AotPrimary, host.Options.ExecutionTier);
    }

    [Fact]
    public async Task FoundationInstaller_AllowAutoStartFalse_HidesUiRebalancesLayoutAndClampsStartupShortcut()
    {
        var defaultDesc = new InstallerAppDescriptor("TestApp", "Test App", "Suite", "1.0.0");
        Assert.True(defaultDesc.AllowAutoStart);

        var gameDesc = new InstallerAppDescriptor("KingsFieldRecomp", "King's Field", "Suite", "0.1.0", AllowAutoStart: false);
        Assert.False(gameDesc.AllowAutoStart);

        string payloadZip = BuildTestPayloadZip("KingsFieldRecomp.exe");
        string isolatedDir = Path.Combine(_tempDir, "Isolated", "kf");
        var ctx = new InstallerRuntimeContext(cliDataDir: isolatedDir, envPrefix: gameDesc.EnvPrefix);
        var engine = new InstallerEngine(gameDesc, ctx);

        using (var wizard = new InstallerWizardForm(engine, ctx, initialAutoStart: true))
        {
            Assert.False(wizard.IsAutoStartAllowed);
            Assert.False(wizard.AutoStartCheckBox.Visible);
            Assert.False(wizard.AutoStartCheckBox.Enabled);
            Assert.False(wizard.AutoStartCheckBox.Checked);
            Assert.Equal(62, wizard.ShortcutsCheckBox.Top);
            Assert.Equal(88, wizard.LaunchAfterCheckBox.Top);
        }

        var installRes = await engine.InstallAsync(createShortcuts: true, registerAutoStart: true, externalPayloadPath: payloadZip);
        Assert.True(installRes.Success);
        Assert.False(Directory.Exists(Path.Combine(isolatedDir, "shortcuts", "startup")));

        var theme = new InstallerThemeOptions
        {
            AppId = "kf",
            DisplayName = "King's Field",
            Version = "0.1.0",
            DefaultInstallDir = isolatedDir,
            AllowAutoStart = false
        };
        using var tier2Wizard = new InstallerWizardForm(new InstallerEngineCore(theme, new AppEnvironment("kf", new[] { "--data-dir", isolatedDir })));
        Assert.False(tier2Wizard.AutoStartCheckBox.Visible);
    }

    [Fact]
    public void FileLockRetry_And_IsolatedInstallDirSafety_WorkFailClosed()
    {
        int attempts = 0;
        FileLockRetryHelper.ExecuteWithRetry(() =>
        {
            attempts++;
            if (attempts < 4) throw new IOException("Simulated AV transient lock");
        }, maxAttempts: 5);
        Assert.Equal(4, attempts);

        string payloadZip = BuildTestPayloadZip("RecompApp.exe");
        string safeInstallDir = Path.Combine(_tempDir, "Isolated", "AutoContained");
        string safeDataDir = Path.Combine(_tempDir, "Isolated", "AutoData");
        var desc = new InstallerAppDescriptor("RecompApp", "Recomp App", "Suite", "0.1.0", MainExecutableRelativePath: "RecompApp.exe", AllowAutoStart: false);

        int rc = StandardInstallerHost.Run(desc, new[] { "--install-dir", safeInstallDir, "--data-dir", safeDataDir, "--payload", payloadZip, "--silent" });
        Assert.Equal(0, rc);
        Assert.True(File.Exists(Path.Combine(safeDataDir, "logs", "installer.log")));

        var isolatedCtx = new InstallerRuntimeContext(cliDataDir: safeDataDir, envPrefix: desc.EnvPrefix);
        var isolatedEngine = new InstallerEngine(desc, isolatedCtx);
        string unsafeHostTarget = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "Programs", "UnsafeTarget");
        Assert.Throws<InvalidOperationException>(() => isolatedEngine.ValidateAndNormalizeInstallDirectory(unsafeHostTarget));
    }

    private string BuildTestPayloadZip(string exeName)
    {
        string path = Path.Combine(_tempDir, $"{exeName}.payload.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = zip.CreateEntry(exeName);
        using var sw = new StreamWriter(entry.Open(), Encoding.UTF8);
        sw.Write("MZ_TEST_BINARY");
        return path;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private sealed class SampleManifest : IRecompModuleManifest
    {
        public string ModuleId => "sample-recomp";
        public string DisplayName => "Sample Recomp";
        public string PlatformId => "ps1";
        public string Version => "0.1.0";
        public IReadOnlyList<DiscChecksumEntry> RequiredDiscChecksums => Array.Empty<DiscChecksumEntry>();
    }

    private sealed class SampleRenderBackend : IRecompRenderBackend
    {
        public string BackendId => "software-rgba";
        public int ScaleFactor { get; set; } = 1;
        public void BeginFrame(int width, int height) { }
        public void PresentRgba(int width, int height, ReadOnlySpan<byte> rgba) { }
    }

    private sealed class SampleRecompHost : IRecompHost
    {
        public RecompHostOptions Options { get; private set; } = new();
        public IRecompRenderBackend RenderBackend { get; } = new SampleRenderBackend();
        public RecompFrameStats GetFrameStats() => new(1, 16.6, 60.0, 16.7, 0.5, 0);
        public void SetTargetFps(double targetFps, bool unlock = false) => Options = Options with { TargetFps = targetFps, UnlockFps = unlock };
    }

    private sealed class SampleGameModule(IRecompModuleManifest manifest) : IRecompGameModule
    {
        public IRecompModuleManifest Manifest { get; } = manifest;
        public IReadOnlyList<RecompPatchDescriptor> TimingPatches { get; } = [new("unlock-60", "60 FPS Unlock", 60.0, IsUnlocked: true)];
        public Task InitializeAsync(IRecompHost host, CancellationToken ct = default) => Task.CompletedTask;
        public Task StepFrameAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task LoadSaveStateAsync(string saveStatePath, CancellationToken ct = default) => Task.CompletedTask;
    }
}
