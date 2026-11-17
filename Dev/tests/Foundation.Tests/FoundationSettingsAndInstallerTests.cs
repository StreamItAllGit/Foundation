// @module: Foundation.Tests.SettingsAndInstaller
// @purpose: Unit tests for GranularSettingsManager, DpapiSecretStore, SafeNetworkTransfer, ToolResolver, InstallerEngineCore, WindowsShortcutWriter, and InstallerWizardForm.
// @depends-on: System, System.IO, System.IO.Compression, System.Net, System.Net.Http, System.Text, System.Threading, System.Threading.Tasks, Foundation.Core, Foundation.Installer, Foundation.UI, Xunit
// @exports: SampleSettingsModel, FoundationSettingsAndInstallerTests

using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;
using Foundation.Installer;
using Foundation.UI;
using Xunit;

namespace Foundation.Tests;

public sealed record SampleSettingsModel
{
    public int SchemaVersion { get; init; } = 2;
    public string Theme { get; init; } = "deep-slate";
    public int MaxConcurrentJobs { get; init; } = 1;
}

public sealed class FoundationSettingsAndInstallerTests : IDisposable
{
    private readonly string _tempDir;

    public FoundationSettingsAndInstallerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FoundationInstTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void GranularSettingsManager_Rotates20SnapshotsAndSelfHealsFromBackup()
    {
        var mgr = new GranularSettingsManager(_tempDir, maxHistorySnapshots: 20);

        for (int i = 1; i <= 25; i++)
        {
            mgr.SaveAtomically("settings.json", new SampleSettingsModel
            {
                SchemaVersion = 2,
                Theme = $"theme-{i}",
                MaxConcurrentJobs = i
            });
        }

        Assert.Equal(20, mgr.GetHistorySnapshots("settings.json").Count);

        string primaryPath = mgr.ResolveSettingsPath("settings.json");
        Assert.True(File.Exists(primaryPath + ".bak"));

        // Corrupt primary settings.json and verify self-healing from .bak
        File.WriteAllText(primaryPath, "{ corrupted_json: [", Encoding.UTF8);

        SettingsLoadOutcome<SampleSettingsModel> healed = mgr.LoadOrSelfHeal(
            "settings.json",
            () => new SampleSettingsModel());

        Assert.True(healed.HealedFromBackup);
        Assert.Equal(24, healed.Value.MaxConcurrentJobs);
    }

    [Fact]
    public void DpapiSecretStore_EncryptsAndRoundTripsSecretsWithoutPlaintextLeak()
    {
        var store = new DpapiSecretStore(_tempDir);
        const string webhook = "https://example.invalid/api/hooks/000000000/test-placeholder-value";
        store.SetSecret("discord_webhook_url", webhook);

        Assert.True(File.Exists(store.SecretsFilePath));
        string rawDiskContent = File.ReadAllText(store.SecretsFilePath, Encoding.UTF8);
        Assert.DoesNotContain("test-placeholder-value", rawDiskContent);

        Assert.Equal(webhook, store.GetSecret("discord_webhook_url"));
        Assert.True(store.RemoveSecret("discord_webhook_url"));
        Assert.Null(store.GetSecret("discord_webhook_url"));
    }

    [Fact]
    public async Task InstallerEngineCore_And_WizardForm_CompleteFullIsolatedLifecycle()
    {
        var env = new AppEnvironment("shadowlibrary", new[] { "--data-dir", _tempDir });
        var theme = new InstallerThemeOptions
        {
            AppId = "shadowlibrary",
            DisplayName = "ShadowLibrary",
            Version = "1.0.0-rc.14",
            BrandAccentHex = "#6366F1",
            DefaultInstallDir = Path.Combine(_tempDir, "installed-app")
        };
        var engine = new InstallerEngineCore(theme, env);

        using var payloadZip = BuildSamplePayloadZip("v1");
        FoundationInstallResult installRes = await engine.InstallOrUpgradeFromZipAsync(payloadZip);
        Assert.True(installRes.Success);
        Assert.False(installRes.WasUpgrade);
        Assert.True(engine.VerifyInstallation(null, out _));

        // Write a user file inside data\ and verify upgrade preserves it
        string userDbPath = Path.Combine(installRes.InstallDirectory, "data", "shadowlibrary.db");
        File.WriteAllText(userDbPath, "USER_CANONICAL_DATA", Encoding.UTF8);

        using var upgradeZip = BuildSamplePayloadZip("v2");
        FoundationInstallResult upgradeRes = await engine.InstallOrUpgradeFromZipAsync(upgradeZip);
        Assert.True(upgradeRes.Success);
        Assert.True(upgradeRes.WasUpgrade);
        Assert.Equal("USER_CANONICAL_DATA", File.ReadAllText(userDbPath, Encoding.UTF8));

        // Verify .lnk shortcut binary header
        string lnkPath = Path.Combine(_tempDir, "ShadowLibrary.lnk");
        WindowsShortcutWriter.WriteShortcut(lnkPath, Path.Combine(installRes.InstallDirectory, "ShadowLibrary.exe"));
        Assert.True(WindowsShortcutWriter.IsValidShellLinkHeader(File.ReadAllBytes(lnkPath)));

        // Verify 650x460 collapsed / 650x570 expanded wizard dimensions & ShellBrandingOptions default SdkVersion
        using var wizard = new InstallerWizardForm(engine);
        Assert.Equal(650, wizard.ClientSize.Width);
        Assert.Equal(460, wizard.ClientSize.Height);
        wizard.ToggleDetailsDrawer();
        Assert.Equal(570, wizard.ClientSize.Height);

        var branding = new ShellBrandingOptions { AppId = "shadowlibrary", DisplayName = "ShadowLibrary" };
        Assert.Equal("0.5.0", branding.SdkVersion);

        FoundationInstallResult uninstallRes = engine.Uninstall(purgeUserData: false);
        Assert.True(uninstallRes.Success);
        Assert.True(File.Exists(userDbPath));
    }

    [Fact]
    public async Task StandardInstallerHost_And_InstallerEngine_ExecuteParameterizedLifecycle()
    {
        var descriptor = new InstallerAppDescriptor(
            ProductName: "NightmaresEve",
            DisplayName: "Nightmare's Eve",
            Publisher: "TeamBurgers",
            Version: "0.0.1-hf.1",
            MainExecutableRelativePath: Path.Combine("app", "NightmaresEve.exe"));

        Assert.Equal(0, StandardInstallerHost.Run(descriptor, new[] { "--version" }));
        Assert.Equal(0, StandardInstallerHost.Run(descriptor, new[] { "--help" }));
        Assert.Equal(2, StandardInstallerHost.Run(descriptor, new[] { "--unknown-flag" }));

        string payloadZipPath = Path.Combine(_tempDir, "sample-payload.zip");
        using (var payloadMs = BuildSamplePayloadZip("v1"))
        {
            await File.WriteAllBytesAsync(payloadZipPath, payloadMs.ToArray());
        }

        string isolatedDataDir = Path.Combine(_tempDir, "ne-isolated");
        var ctx = new InstallerRuntimeContext(cliDataDir: isolatedDataDir, envPrefix: descriptor.EnvPrefix);
        var engine = new InstallerEngine(descriptor, ctx);

        var verifyRes = await engine.VerifyPayloadAsync(payloadZipPath);
        Assert.True(verifyRes.Valid);

        var installRes = await engine.InstallAsync(externalPayloadPath: payloadZipPath);
        Assert.True(installRes.Success);

        using var wizard = new InstallerWizardForm(engine, ctx);
        Assert.Equal(650, wizard.ClientSize.Width);
        Assert.Equal(460, wizard.ClientSize.Height);
        wizard.SetDetailsExpanded(true);
        Assert.Equal(570, wizard.ClientSize.Height);

        var uninstallRes = await engine.UninstallAsync(installRes.InstallDirectory, purgeData: true);
        Assert.True(uninstallRes.Success);
    }

    [Fact]
    public async Task SafeNetworkTransfer_And_ToolResolver_VerifyHashesAndFallback()
    {
        byte[] payload = Encoding.UTF8.GetBytes("Foundation SafeNetworkTransfer Payload Verification Test");
        string expectedSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant();
        string destFile = Path.Combine(_tempDir, "downloads", "rom_pack.7z");

        var handler = new FakePayloadHttpHandler(payload, failFirstAttempt: true);
        SafeTransferResult res = await SafeNetworkTransfer.DownloadFileAsync(
            new Uri("https://example.invalid/rom_pack.7z"),
            destFile,
            new SafeTransferOptions
            {
                JobId = "test-job",
                ExpectedSizeBytes = payload.Length,
                ExpectedSha256Hex = expectedSha256,
                MinFreeSpaceReserveBytes = 1024
            },
            handler);

        Assert.True(File.Exists(res.FinalFilePath));
        Assert.Equal(payload.Length, res.BytesTransferred);
        Assert.Equal(expectedSha256, res.Sha256Hex);
        Assert.Equal(1, res.RetryCount);

        ToolResolutionResult missingTool = ToolResolver.ResolveTool("nonexistent-tool-xyz", new[] { "nonexistent-tool-xyz.exe" });
        Assert.False(missingTool.IsAvailable);
        Assert.Equal(ToolResolutionSource.MissingFallback, missingTool.Source);
    }

    private sealed class FakePayloadHttpHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;
        private bool _failNext;

        public FakePayloadHttpHandler(byte[] payload, bool failFirstAttempt)
        {
            _payload = payload;
            _failNext = failFirstAttempt;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_failNext)
            {
                _failNext = false;
                var retryResp = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                retryResp.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(20));
                return Task.FromResult(retryResp);
            }

            var okResp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_payload)
            };
            okResp.Content.Headers.ContentLength = _payload.Length;
            return Task.FromResult(okResp);
        }
    }

    private static MemoryStream BuildSamplePayloadZip(string versionMarker)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry launcher = zip.CreateEntry("ShadowLibrary.exe");
            using (var sw = new StreamWriter(launcher.Open(), Encoding.UTF8))
            {
                sw.Write($"MZ_LAUNCHER_{versionMarker}");
            }

            ZipArchiveEntry suiteManifest = zip.CreateEntry("app/suite.manifest.json");
            using (var sw = new StreamWriter(suiteManifest.Open(), Encoding.UTF8))
            {
                sw.Write("{\"schemaVersion\":2,\"appId\":\"shadowlibrary\",\"sdkVersion\":\"0.1.0\"}");
            }
        }
        ms.Position = 0;
        return ms;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
