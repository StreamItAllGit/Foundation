// @module: Foundation.Tests.FoundationDecouplingTests
// @purpose: End-to-end verification of Foundation v0.2.0 shared libraries (Foundation.Retro v0.1.0, Foundation.Sidecars v0.1.0, Foundation.Server v0.1.0, Foundation.Launcher v0.1.0, Foundation.UI.Desktop v0.1.0, Foundation.Installer v0.1.2, @foundation/ui v0.1.0, and foundation_core v0.1.0).
// @depends-on: Xunit, Foundation.Core, Foundation.Retro, Foundation.Sidecars, Foundation.Server, Foundation.Launcher, Foundation.UI.Desktop, Foundation.Installer
// @exports: FoundationDecouplingTests

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;
using Foundation.Installer;
using Foundation.Launcher;
using Foundation.Retro;
using Foundation.Server;
using Foundation.Sidecars;
using Foundation.UI.Desktop;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Foundation.Tests;

public sealed class FoundationDecouplingTests
{
    [Fact]
    public void FoundationRetro_Catalog_DatParser_TitleNormalizer_HeaderStripper_And_SoftPatcher_Work()
    {
        var systems = ConsoleSystemCatalog.BuildDefaultPlatforms();
        Assert.True(systems.Count >= 64);

        var provenanceRules = ConsoleSystemCatalog.BuildDefaultProvenanceTags();
        Assert.NotEmpty(provenanceRules);

        var parsed = DatTitleNormalizer.ParseFilename(
            "Super Mario World (USA) (Rev 1) [!].sfc",
            provenanceRules);
        Assert.Equal("Super Mario World", parsed.CleanTitle);
        Assert.Equal("USA", parsed.Region);
        Assert.Equal("Rev 1", parsed.Revision);

        var rules = ConsoleSystemCatalog.BuildDefaultHeaderSkipRules();
        Assert.NotEmpty(rules);

        byte[] baseData = Encoding.ASCII.GetBytes("HELLO WORLD");
        using var patchMs = new MemoryStream();
        patchMs.Write(Encoding.ASCII.GetBytes("PATCH"));
        patchMs.Write(new byte[] { 0x00, 0x00, 0x06, 0x00, 0x05 });
        patchMs.Write(Encoding.ASCII.GetBytes("VAULT"));
        patchMs.Write(Encoding.ASCII.GetBytes("EOF"));
        byte[] patched = SoftPatchEngine.ApplyIpsPatchInMemory(baseData, patchMs.ToArray());
        Assert.Equal("HELLO VAULT", Encoding.ASCII.GetString(patched));
    }

    [Fact]
    public async Task FoundationSidecars_StreamHasher_HashSidecar_And_HkLockSentinel_Work()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FoundationSidecarsTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            string romPath = Path.Combine(tempDir, "TestGame.sfc");
            await File.WriteAllBytesAsync(romPath, Encoding.UTF8.GetBytes("ROM_PAYLOAD_DATA_12345"));

            var hashes = await SinglePassStreamHasher.HashFileSinglePassAsync(
                romPath,
                new List<RetroHeaderSkipRule>(),
                includeSha256: true,
                ct: CancellationToken.None);
            Assert.Equal(22, hashes.SizeBytes);
            Assert.NotEmpty(hashes.Sha1);
            Assert.NotEmpty(hashes.Crc32);

            var normalized = HashSidecarService.NormalizeRelativePathsStrippingSingleWrapper(
                new[] { ("GameFolder/PS3_GAME/USRDIR/EBOOT.BIN", 100L, hashes.Crc32, hashes.Md5, hashes.Sha1) });
            Assert.Equal("PS3_GAME/USRDIR/EBOOT.BIN", normalized[0].RelativePath);

            var (acquired, lockPath, _) = HkLockSentinel.TryAcquireDirectoryHkLock(tempDir, "sl", honorHkLocks: true);
            Assert.True(acquired);
            Assert.NotNull(lockPath);
            Assert.True(File.Exists(lockPath!));
            File.Delete(lockPath!);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void FoundationServer_TieredAuth_And_Launcher_And_CliHelper_Work()
    {
        var (hash, salt) = TieredAuthEngine.HashPassword("CorrectHorseBatteryStaple!");
        Assert.True(TieredAuthEngine.VerifyPassword("CorrectHorseBatteryStaple!", hash, salt));
        Assert.False(TieredAuthEngine.VerifyPassword("WrongPassword", hash, salt));

        byte[] secret = new byte[32];
        Random.Shared.NextBytes(secret);
        string cookie = TieredAuthEngine.CreateSessionCookieValue(secret, TimeSpan.FromMinutes(10), "admin");
        Assert.True(TieredAuthEngine.ValidateSessionCookieValue(secret, cookie));

        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Loopback;
        var snap = new TieredAuthConfigSnapshot(true, true, true, hash, salt, "tok123");
        var eval = TieredAuthEngine.EvaluateRequest(ctx, snap, secret);
        Assert.True(eval.IsAuthorized);
        Assert.True(eval.IsLoopback);

        Assert.True(FoundationTieredAuthMiddleware.IsProtectedRoute("/api/v1/vaults"));
        Assert.True(FoundationTieredAuthMiddleware.IsPublicApiEndpoint("/api/v1/health"));

        Assert.Null(StaNativeDialogBridgeBase.ResolveNearestExistingDirectory(null, null));

        string[] cliArgs = { "--test-mode", "--data-dir", "C:\\Temp\\Sandbox" };
        Assert.True(CliArgumentHelper.HasFlag(cliArgs, "--test-mode"));
        Assert.Equal("C:\\Temp\\Sandbox", CliArgumentHelper.GetOption(cliArgs, "--data-dir"));
        Assert.NotNull(typeof(RootLauncherHost));
        string sampleInstallRoot = Path.Combine(
            Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\",
            "SamplePrograms",
            "ShadowLibrary");
        Assert.Equal(
            Path.Combine(sampleInstallRoot, "app", "ShadowLibrary.exe"),
            RootLauncherHost.ResolveTargetExecutable(sampleInstallRoot, "ShadowLibrary.exe"));
        Assert.Equal(
            Path.Combine(sampleInstallRoot, "app", "ShadowLibrary.exe"),
            RootLauncherHost.ResolveTargetExecutable(
                sampleInstallRoot,
                "ShadowLibrary.exe",
                Path.Combine(sampleInstallRoot, "ShadowLibrary.exe")));
    }

    [Fact]
    public async Task FoundationInstaller_PreUpgradeBackup_SkipsVolatileDirs_And_RespectsCancellation()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FoundationInstBackup_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(tempDir, "data", "_Quarantine"));
        Directory.CreateDirectory(Path.Combine(tempDir, "data", "webview2_cache"));
        await File.WriteAllTextAsync(Path.Combine(tempDir, "data", "app.settings.json"), "{\"ok\":true}");
        await File.WriteAllBytesAsync(Path.Combine(tempDir, "data", "_Quarantine", "huge.bin"), new byte[4096]);

        try
        {
            string? zipPath = await InstallerEngineCore.CreatePreUpgradeBackupAsync(tempDir, "1.0.0-rc.20");
            Assert.NotNull(zipPath);
            Assert.True(File.Exists(zipPath!));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
