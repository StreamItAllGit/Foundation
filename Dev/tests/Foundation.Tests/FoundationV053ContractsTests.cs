// @module: Foundation.Tests.V053Contracts
// @purpose: Isolated unit & loopback HTTP tests for Foundation v0.5.3 (ownership guard, Dual Online/Offline Setup, SSRF redirect guards, RollingFileBackupStore, HighResFramePacer late-latch, WinForms-free IAutomationSurfaceTarget).
// @depends-on: System, System.Collections.Generic, System.IO, System.IO.Compression, System.Net, System.Net.Http, System.Net.Http.Headers, System.Net.Sockets, System.Security.Cryptography, System.Text, System.Text.Json, System.Threading, System.Threading.Tasks, Foundation.Core, Foundation.Installer, Foundation.Automation, Xunit
// @exports: FoundationV053ContractsTests

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Automation;
using Foundation.Core;
using Foundation.Installer;
using Xunit;

namespace Foundation.Tests;

public sealed class FoundationV053ContractsTests : IDisposable
{
    private readonly string _tempDir;

    public FoundationV053ContractsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FoundationV053Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task InstallerOwnership_RejectsForeignInstallAndUninstall_WithoutMutatingFiles()
    {
        string targetDir = Path.Combine(_tempDir, "StreamItAll", "reEngine");
        string reEngineZip = BuildZipFile("reEngine.zip", ("app/reEngine.exe", "MZ_REENGINE"));
        string reSlateZip = BuildZipFile("reSlate.zip", ("app/reSlate.exe", "MZ_RESLATE"));

        var reEngineDesc = new InstallerAppDescriptor("reEngine", "reEngine", "StreamItAll", "0.5.0", MainExecutableRelativePath: @"app\reEngine.exe");
        var reEngineCtx = new InstallerRuntimeContext(Path.Combine(_tempDir, "reengine-iso"), null, null, false, "REENGINE");
        var reEngineInstaller = new InstallerEngine(reEngineDesc, reEngineCtx);
        InstallerOperationResult reEngineInstall = await reEngineInstaller.InstallAsync(
            new InstallerInstallOptions(ExplicitInstallDir: targetDir, CreateShortcuts: false, ExternalPayloadPath: reEngineZip));
        Assert.True(reEngineInstall.Success, reEngineInstall.Message);
        File.WriteAllText(Path.Combine(targetDir, "data", "settings.json"), "{\"ok\":true}\n");

        var reSlateDesc = new InstallerAppDescriptor("reSlate", "reSlate", "StreamItAll", "0.3.0", MainExecutableRelativePath: @"app\reSlate.exe");
        var reSlateCtx = new InstallerRuntimeContext(Path.Combine(_tempDir, "reslate-iso"), null, null, false, "RESLATE");
        var reSlateInstaller = new InstallerEngine(reSlateDesc, reSlateCtx);

        Assert.False(reSlateInstaller.HasExistingInstallationArtifacts(targetDir));
        Assert.Equal(InstallDirectoryOwnership.OwnedByForeignProduct, reSlateInstaller.CheckDirectoryOwnership(targetDir).Status);

        InstallerOperationResult badInstall = await reSlateInstaller.InstallAsync(
            new InstallerInstallOptions(ExplicitInstallDir: targetDir, CreateShortcuts: false, ExternalPayloadPath: reSlateZip));
        Assert.False(badInstall.Success);
        Assert.Contains("ProductMismatch", badInstall.Message);
        Assert.True(File.Exists(Path.Combine(targetDir, "app", "reEngine.exe")));
        Assert.False(File.Exists(Path.Combine(targetDir, "app", "reSlate.exe")));

        InstallerOperationResult badUninstall = await reSlateInstaller.UninstallAsync(targetDir, purgeData: true);
        Assert.False(badUninstall.Success);
        Assert.Contains("ProductMismatch", badUninstall.Message);
        Assert.True(File.Exists(Path.Combine(targetDir, "app", "reEngine.exe")));
        Assert.True(File.Exists(Path.Combine(targetDir, "data", "settings.json")));

        string trampoline = InstallerEngine.BuildSelfDeleteTrampolineCommand(Path.Combine(targetDir, "app", "reSlate-Uninstall.exe"), targetDir);
        Assert.DoesNotContain("*-Uninstall.exe", trampoline, StringComparison.OrdinalIgnoreCase);

        SynchronizationContext? prevSync = SynchronizationContext.Current;
        try
        {
            using var wizard = new InstallerWizardForm(reSlateInstaller, reSlateCtx, initialInstallDir: targetDir);
            Assert.Equal("FOREIGN APP DETECTED", wizard.ModeBadgeLabel.Text);
        }
        finally { SynchronizationContext.SetSynchronizationContext(prevSync); }

        var tier2Foreign = new InstallerEngineCore(
            new InstallerThemeOptions { AppId = "reslate", DisplayName = "reSlate", Version = "0.3.0", DefaultInstallDir = targetDir },
            new AppEnvironment("reslate", new[] { "--data-dir", Path.Combine(_tempDir, "tier2-iso") }));
        Assert.False(tier2Foreign.HasExistingInstallation(targetDir));
        using (var ms = new MemoryStream(File.ReadAllBytes(reSlateZip)))
        {
            FoundationInstallResult t2Install = await tier2Foreign.InstallOrUpgradeFromZipAsync(ms, targetDir);
            Assert.False(t2Install.Success);
            Assert.Contains("ProductMismatch", t2Install.Message);
        }
        Assert.False(tier2Foreign.Uninstall(targetDir, purgeUserData: true).Success);
        Assert.True(File.Exists(Path.Combine(targetDir, "app", "reEngine.exe")));
        InstallerOperationResult reEngineUninstall = await reEngineInstaller.UninstallAsync(targetDir, purgeData: false);
        Assert.True(reEngineUninstall.Success, reEngineUninstall.Message);
    }

    [Fact]
    public async Task SafeNetworkTransfer_EnforcesHttpsSsrfRedirectAndSizeGuards()
    {
        Assert.Throws<InvalidOperationException>(() => SafeNetworkTransfer.ValidateRequestUri(new Uri("file:///C:/Windows/win.ini")));
        Assert.Throws<InvalidOperationException>(() => SafeNetworkTransfer.ValidateRequestUri(new Uri("http://169.254.169.254/latest/meta-data/")));
        Assert.Throws<InvalidOperationException>(() => SafeNetworkTransfer.ValidateRequestUri(new Uri("http://[::ffff:169.254.169.254]/")));
        Assert.Throws<InvalidOperationException>(() => SafeNetworkTransfer.ValidateRequestUri(new Uri("http://[fe80::1]/")));
        Assert.Throws<InvalidOperationException>(() => SafeNetworkTransfer.ValidateRequestUri(new Uri("http://[fd00:ec2::254]/")));
        Assert.Throws<InvalidOperationException>(() => SafeNetworkTransfer.ValidateRequestUri(new Uri("https://metadata.google.internal/computeMetadata/v1/")));
        Assert.Throws<InvalidOperationException>(() => SafeNetworkTransfer.ValidateRequestUri(new Uri("http://example.invalid/file.zip"), requireHttps: true, allowLoopbackHttp: true));

        byte[] payload = Encoding.UTF8.GetBytes("loopback-verified-bytes");
        string shaHex = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        await using var server = new EphemeralHttpServer(path => path switch
        {
            "/redirect-imds" => (302, Array.Empty<byte>(), "http://169.254.169.254/latest/meta-data/"),
            "/redirect-http" => (302, Array.Empty<byte>(), "http://example.invalid/drop.zip"),
            "/ok.bin" => (200, payload, null),
            _ => (404, Array.Empty<byte>(), null)
        });
        server.Start();

        await Assert.ThrowsAsync<InvalidOperationException>(() => SafeNetworkTransfer.DownloadVerifiedAsync(
            new Uri($"{server.BaseUrl}/redirect-imds"), Path.Combine(_tempDir, "ssrf.bin"), requireHttps: true, allowLoopbackHttp: true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => SafeNetworkTransfer.DownloadVerifiedAsync(
            new Uri($"{server.BaseUrl}/redirect-http"), Path.Combine(_tempDir, "downgrade.bin"), requireHttps: true, allowLoopbackHttp: true));

        string badSizeDest = Path.Combine(_tempDir, "wrong-size.bin");
        await Assert.ThrowsAsync<InvalidDataException>(() => SafeNetworkTransfer.DownloadVerifiedAsync(
            new Uri($"{server.BaseUrl}/ok.bin"), badSizeDest, expectedSha256Hex: shaHex, expectedSizeBytes: payload.Length + 10, requireHttps: true, allowLoopbackHttp: true));
        Assert.False(File.Exists(badSizeDest));

        SafeTransferResult okRes = await SafeNetworkTransfer.DownloadVerifiedAsync(
            new Uri($"{server.BaseUrl}/ok.bin"), Path.Combine(_tempDir, "ok.bin"),
            expectedSha256Hex: shaHex, expectedSizeBytes: payload.Length, requireHttps: true, allowLoopbackHttp: true);
        Assert.Equal(shaHex, okRes.Sha256Hex);
        Assert.Equal(payload.Length, okRes.BytesTransferred);
    }

    [Fact]
    public async Task OnlineSetup_StreamsFromLocalLoopback_FailsClosedOn404OrHashMismatch_AndSupportsOfflineBundle()
    {
        string coreZip = BuildZipFile("app-core.zip", ("app/DemoOnline.exe", "MZ_ONLINE_APP"));
        string fullZip = BuildZipFile("extras.zip", ("packs/readme.txt", "EXTRA_PACK"));
        byte[] coreBytes = await File.ReadAllBytesAsync(coreZip);
        byte[] fullBytes = await File.ReadAllBytesAsync(fullZip);
        string coreSha = Convert.ToHexString(SHA256.HashData(coreBytes)).ToLowerInvariant();
        string fullSha = Convert.ToHexString(SHA256.HashData(fullBytes)).ToLowerInvariant();

        string manifestJson = $$"""
        {
          "$schema": "https://schemas.local/manifest.v2.json",
          "schemaVersion": 2,
          "manifestType": "installer-manifest",
          "appId": "demoonline",
          "version": "0.5.3",
          "installerDeliveryKind": "onlineSetup",
          "payloadDownloadUrl": "https://cdn.example.invalid/releases/v0.5.3/",
          "payloadAssets": [
            { "installProfile": "lightweight", "fileName": "app-core.zip", "expectedSizeBytes": {{coreBytes.Length}}, "expectedSha256": "{{coreSha}}" },
            { "installProfile": "full", "fileName": "extras.zip", "expectedSizeBytes": {{fullBytes.Length}}, "expectedSha256": "{{fullSha}}", "relativeSubdir": "addons" }
          ]
        }
        """;
        InstallerMarkupManifest parsed = InstallerManifestStore.LoadFromJson(manifestJson);
        Assert.Equal(InstallerDeliveryKinds.OnlineSetup, parsed.InstallerDeliveryKind);
        Assert.Equal(2, parsed.PayloadAssets!.Count);
        Assert.Throws<InvalidDataException>(() => InstallerManifestStore.LoadFromJson(manifestJson.Replace("\"addons\"", "\"../escape\"")));

        bool tamperCore = false;
        await using var server = new EphemeralHttpServer(path => path switch
        {
            "/releases/v0.5.3/app-core.zip" => (200, tamperCore ? Encoding.UTF8.GetBytes("TAMPERED") : coreBytes, null),
            "/releases/v0.5.3/extras.zip" => (200, fullBytes, null),
            _ => (404, Array.Empty<byte>(), null)
        });
        server.Start();

        InstallerAppDescriptor desc = InstallerManifestStore.ApplyToDescriptor(
            new InstallerAppDescriptor("DemoOnline", "Demo Online", "StreamItAll", "0.5.3", MainExecutableRelativePath: @"app\DemoOnline.exe"),
            parsed) with { PayloadDownloadUrl = $"{server.BaseUrl}/releases/v0.5.3/" };

        var ctx = new InstallerRuntimeContext(Path.Combine(_tempDir, "online-iso"), null, null, false, "DEMOONLINE");
        var engine = new InstallerEngine(desc, ctx) { AllowLoopbackHttpForTesting = true };

        string installDir = Path.Combine(_tempDir, "installed-online");
        InstallerOperationResult okRes = await engine.InstallAsync(new InstallerInstallOptions(
            ExplicitInstallDir: installDir, CreateShortcuts: false, InstallProfile: "full"));
        Assert.True(okRes.Success, okRes.Message);
        Assert.True(File.Exists(Path.Combine(installDir, "app", "DemoOnline.exe")));
        Assert.True(File.Exists(Path.Combine(installDir, "addons", "packs", "readme.txt")));

        tamperCore = true;
        InstallerOperationResult badHash = await engine.InstallAsync(new InstallerInstallOptions(
            ExplicitInstallDir: Path.Combine(_tempDir, "bad-hash"), CreateShortcuts: false, InstallProfile: "lightweight"));
        Assert.False(badHash.Success);
        Assert.Contains("Offline-Setup", badHash.Message);
        Assert.Contains("--offline-bundle", badHash.Message);

        var missingUrlEngine = new InstallerEngine(desc with { PayloadDownloadUrl = $"{server.BaseUrl}/missing/" }, ctx) { AllowLoopbackHttpForTesting = true };
        InstallerOperationResult fail404 = await missingUrlEngine.InstallAsync(new InstallerInstallOptions(
            ExplicitInstallDir: Path.Combine(_tempDir, "fail-404"), CreateShortcuts: false, InstallProfile: "lightweight"));
        Assert.False(fail404.Success);
        Assert.Contains("Portable.zip", fail404.Message);

        string offlineDir = Path.Combine(_tempDir, "offline-bundle");
        Directory.CreateDirectory(offlineDir);
        File.Copy(coreZip, Path.Combine(offlineDir, "app-core.zip"));
        File.Copy(fullZip, Path.Combine(offlineDir, "extras.zip"));
        string offlineTarget = Path.Combine(_tempDir, "installed-offline-bundle");
        var cliOpts = InstallerEngine.ParseCliArgs(new[] { "--offline-bundle", offlineDir }, desc);
        Assert.Equal(offlineDir, cliOpts.OfflineBundlePath);
        InstallerOperationResult bundleRes = await missingUrlEngine.InstallAsync(new InstallerInstallOptions(
            ExplicitInstallDir: offlineTarget, CreateShortcuts: false, InstallProfile: "full") { OfflineBundlePath = cliOpts.OfflineBundlePath });
        Assert.True(bundleRes.Success, bundleRes.Message);
        Assert.True(File.Exists(Path.Combine(offlineTarget, "addons", "packs", "readme.txt")));
    }

    [Fact]
    public async Task RollingFileBackupStore_And_HighResFramePacer_LateLatch_WorkDeterministically()
    {
        string vaultDir = Path.Combine(_tempDir, "vault_backups");
        string saveFile = Path.Combine(_tempDir, "saves", "slot1.sav");
        var store = new RollingFileBackupStore(vaultDir, maxBackups: 3);

        for (int i = 1; i <= 5; i++)
        {
            RollingBackupCommitResult commit = await store.WriteAtomicallyWithBackupAsync(
                saveFile, Encoding.UTF8.GetBytes($"SAVE_GEN_{i}\n"), label: $"gen{i}");
            Assert.Equal($"SAVE_GEN_{i}\n", File.ReadAllText(commit.TargetFilePath));
        }

        IReadOnlyList<RollingBackupEntry> backups = store.ListBackups(saveFile);
        Assert.Equal(3, backups.Count);
        Assert.True(File.Exists(saveFile + ".bak"));
        File.WriteAllText(saveFile, "CORRUPTED");
        RollingBackupEntry? restored = store.RestoreLatestBackup(saveFile);
        Assert.NotNull(restored);
        Assert.Equal("SAVE_GEN_4\n", File.ReadAllText(saveFile));
        Assert.Throws<InvalidOperationException>(() => store.RestoreBackup(Path.Combine(_tempDir, "outside.bak"), saveFile));

        using var pacer = new HighResFramePacer(120.0) { LateLatchMarginMs = 2.0 };
        pacer.BeginLateLatchFrame();
        double syncWait = pacer.WaitBeforePresent();
        pacer.BeginLateLatchFrame();
        double asyncWait = await pacer.WaitBeforePresentAsync();
        Assert.True(syncWait >= 0.0);
        Assert.True(asyncWait >= 0.0);
        Assert.True(pacer.AverageLateLatchWaitMs >= 0.0);

        pacer.TargetFps = 0.0;
        pacer.BeginLateLatchFrame();
        Assert.Equal(0.0, pacer.WaitBeforePresent());
    }

    [Fact]
    public async Task AutomationSurfaceTarget_RunsWinFormsFree_AcrossTreeInputAndSnapshotPng()
    {
        string contractsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Foundation.Automation", "AutomationContracts.cs"));
        Assert.DoesNotContain("System.Windows.Forms", File.ReadAllText(contractsPath), StringComparison.Ordinal);

        var target = new FakeSurfaceTarget();
        await using AutomationLoopbackServer? server = AutomationCliHelper.TryCreateForSurface(
            "sdl-host", new[] { "--automation-port", "0", "--automation-token", "tok-053" }, target, allowedSnapshotRoot: _tempDir);
        Assert.NotNull(server);
        server!.Start();

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.AutomationPort}") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "tok-053");

        Assert.Contains("canvasRoot", await client.GetStringAsync("/v1/tree"));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/v1/pointer", new StringContent("{\"action\":\"click\",\"x\":10,\"y\":12}", Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/v1/keyboard", new StringContent("{\"action\":\"press\",\"key\":\"Enter\"}", Encoding.UTF8, "application/json"))).StatusCode);
        Assert.True(target.PointerCalls == 1 && target.KeyboardCalls == 1);

        byte[] rawPng = await (await client.GetAsync("/v1/snapshot.png")).Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, rawPng[..4]);
    }

    private string BuildZipFile(string fileName, params (string Entry, string Content)[] entries)
    {
        string path = Path.Combine(_tempDir, fileName);
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach ((string entry, string content) in entries)
        {
            using var sw = new StreamWriter(zip.CreateEntry(entry).Open(), new UTF8Encoding(false));
            sw.Write(content);
        }
        return path;
    }

    private sealed class FakeSurfaceTarget : IAutomationSurfaceTarget
    {
        public int PointerCalls { get; private set; }
        public int KeyboardCalls { get; private set; }
        public AutomationControlNode? InspectTree() => new("canvasRoot", "SdlWindow", "Game", "Ready", 0, 0, 128, 72, true, true, Array.Empty<AutomationControlNode>());
        public bool ExecutePointer(AutomationPointerRequest request) { PointerCalls++; return true; }
        public bool ExecuteKeyboard(AutomationKeyboardRequest request) { KeyboardCalls++; return true; }
        public bool TryCapturePng(string? targetId, out byte[] pngBytes, out int width, out int height)
        {
            width = 4; height = 4;
            pngBytes = RgbaPngWriter.Encode(4, 4, new byte[4 * 4 * 4]);
            return true;
        }
    }

    private sealed class EphemeralHttpServer(Func<string, (int Status, byte[] Body, string? Location)> handler) : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private Task? _loop;
        public string BaseUrl { get; private set; } = string.Empty;

        public void Start()
        {
            _listener.Start();
            BaseUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            _loop = Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    try
                    {
                        TcpClient c = await _listener.AcceptTcpClientAsync(_cts.Token);
                        _ = Task.Run(() => ServeAsync(c), _cts.Token);
                    }
                    catch { break; }
                }
            });
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            await using (NetworkStream s = client.GetStream())
            {
                byte[] buf = new byte[4096];
                int n = await s.ReadAsync(buf);
                if (n <= 0) return;
                string req = Encoding.ASCII.GetString(buf, 0, n);
                string path = req.Split(' ', 3)[1].Split('?', 2)[0];
                (int status, byte[] body, string? loc) = handler(path);
                string reason = status switch { 200 => "OK", 302 => "Found", _ => "Not Found" };
                string locHdr = loc is null ? string.Empty : $"Location: {loc}\r\n";
                byte[] head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {reason}\r\n{locHdr}Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await s.WriteAsync(head);
                if (body.Length > 0) await s.WriteAsync(body);
                await s.FlushAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            if (_loop is not null) try { await _loop; } catch { }
            _cts.Dispose();
        }
    }
}
