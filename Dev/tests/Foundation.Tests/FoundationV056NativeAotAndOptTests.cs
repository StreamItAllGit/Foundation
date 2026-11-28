// @module: Foundation.Tests.V056NativeAotAndOpt
// @purpose: Verifies v0.5.6 NativeAOT Win32 installer, single-profile collapse, Start Menu collision guard, slim uninstaller extraction, SqliteWalPool recreation safety, and C#<->Python DPAPI interop.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.IO.Compression, System.Linq, System.Text, System.Threading.Tasks, Microsoft.Data.Sqlite, Foundation.Core, Foundation.Installer, Xunit
// @exports: FoundationV056NativeAotAndOptTests

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Foundation.Core;
using Foundation.Installer;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Foundation.Tests;

public sealed class FoundationV056NativeAotAndOptTests : IDisposable
{
    private readonly string _tempDir;

    public FoundationV056NativeAotAndOptTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FoundationV056Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string BuildLargeFakePayloadZip(string exeName = "App.exe")
    {
        string path = Path.Combine(_tempDir, $"payload_{Guid.NewGuid():N}.zip");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var entry = zip.CreateEntry($"app/{exeName}");
        using var s = entry.Open();
        byte[] dummy = Encoding.UTF8.GetBytes("MAIN_APP_PAYLOAD_" + new string('X', 2048));
        s.Write(dummy, 0, dummy.Length);
        return path;
    }

    [Fact]
    public async Task SqliteWalPool_SkipsRepeatedWalLock_HandlesDbDeletionAndRecreation_And_Concurrency()
    {
        string dbPath = Path.Combine(_tempDir, "wal-recreate.db");
        var pool = new SqliteWalPool(dbPath, busyTimeoutMs: 5000, pooling: false);

        await using (var conn1 = await pool.OpenConnectionAsync())
        {
            await using var setup = conn1.CreateCommand();
            setup.CommandText = "CREATE TABLE IF NOT EXISTS items (id INTEGER PRIMARY KEY, val TEXT); INSERT INTO items (val) VALUES ('one');";
            await setup.ExecuteNonQueryAsync();
        }

        // Concurrent opens should succeed without write-lock stalls.
        Task[] workers = Enumerable.Range(0, 12).Select(async i =>
        {
            await using var c = await pool.OpenConnectionAsync();
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM items;";
            object? count = await cmd.ExecuteScalarAsync();
            Assert.Equal(1L, count);
        }).ToArray();
        await Task.WhenAll(workers);

        // Simulate mid-run deletion & recreation of DB file WITHOUT calling ResetJournalModeCache();
        // SqliteWalPool must detect the new 0-byte recreated DB and re-enforce WAL mode automatically.
        SqliteConnection.ClearAllPools();
        File.Delete(dbPath);
        if (File.Exists(dbPath + "-wal")) File.Delete(dbPath + "-wal");
        if (File.Exists(dbPath + "-shm")) File.Delete(dbPath + "-shm");

        await using (var recreated = await pool.OpenConnectionAsync())
        {
            await using var modeCmd = recreated.CreateCommand();
            modeCmd.CommandText = "PRAGMA journal_mode;";
            string? mode = Convert.ToString(await modeCmd.ExecuteScalarAsync());
            Assert.Equal("wal", mode, ignoreCase: true);
        }

        // Also verify ResetJournalModeCache() forces WAL pragma re-enforcement cleanly.
        pool.ResetJournalModeCache();
        await using (var afterReset = await pool.OpenConnectionAsync())
        {
            await using var modeCmd = afterReset.CreateCommand();
            modeCmd.CommandText = "PRAGMA journal_mode;";
            string? mode = Convert.ToString(await modeCmd.ExecuteScalarAsync());
            Assert.Equal("wal", mode, ignoreCase: true);
        }
    }

    [Fact]
    public async Task InstallerEngine_HandlesStartMenuZeroByteAndNonEmptyFileCollisions()
    {
        if (!OperatingSystem.IsWindows()) return;

        // 1. Stray 0-byte file blocking Programs\<ProductName> is auto-cleaned.
        string payload1 = BuildLargeFakePayloadZip("CollisionOne.exe");
        string dataDir1 = Path.Combine(_tempDir, "col1-data");
        string installDir1 = Path.Combine(_tempDir, "col1-install");
        string startRoot1 = Path.Combine(dataDir1, "shortcuts", "startmenu");
        Directory.CreateDirectory(startRoot1);
        string blockingZeroFile = Path.Combine(startRoot1, "CollisionOne");
        await File.WriteAllBytesAsync(blockingZeroFile, Array.Empty<byte>());

        var desc1 = new InstallerAppDescriptor("CollisionOne", "Collision One", "Test", "0.5.6", AllowAutoStart: false);
        var engine1 = new InstallerEngine(desc1, new InstallerRuntimeContext(cliDataDir: dataDir1, envPrefix: "COL1"));
        InstallerOperationResult res1 = await engine1.InstallAsync(explicitInstallDir: installDir1, createShortcuts: true, externalPayloadPath: payload1);
        Assert.True(res1.Success, res1.Message);
        Assert.True(Directory.Exists(blockingZeroFile));
        Assert.True(File.Exists(Path.Combine(blockingZeroFile, "Collision One.lnk")));

        // 2. Non-empty file occupying Programs\<ProductName> falls back cleanly to parent Programs\.
        string payload2 = BuildLargeFakePayloadZip("CollisionTwo.exe");
        string dataDir2 = Path.Combine(_tempDir, "col2-data");
        string installDir2 = Path.Combine(_tempDir, "col2-install");
        string startRoot2 = Path.Combine(dataDir2, "shortcuts", "startmenu");
        Directory.CreateDirectory(startRoot2);
        string blockingNonEmptyFile = Path.Combine(startRoot2, "CollisionTwo");
        await File.WriteAllTextAsync(blockingNonEmptyFile, "do-not-delete-user-file");

        var desc2 = new InstallerAppDescriptor("CollisionTwo", "Collision Two", "Test", "0.5.6", AllowAutoStart: false);
        var engine2 = new InstallerEngine(desc2, new InstallerRuntimeContext(cliDataDir: dataDir2, envPrefix: "COL2"));
        InstallerOperationResult res2 = await engine2.InstallAsync(explicitInstallDir: installDir2, createShortcuts: true, externalPayloadPath: payload2);
        Assert.True(res2.Success, res2.Message);
        Assert.True(File.Exists(blockingNonEmptyFile));
        Assert.True(File.Exists(Path.Combine(startRoot2, "Collision Two.lnk")));
    }

    [Fact]
    public async Task InstallerEngine_ExtractsEmbeddedSlimUninstallerResource_WithoutCopyingPayloadBloat()
    {
        string payload = BuildLargeFakePayloadZip("SlimTestApp.exe");
        string dataDir = Path.Combine(_tempDir, "slim-data");
        string installDir = Path.Combine(_tempDir, "slim-install");

        var desc = new InstallerAppDescriptor("SlimTestApp", "Slim Test App", "Test", "0.5.6", AllowAutoStart: false);
        var ctx = new InstallerRuntimeContext(cliDataDir: dataDir, envPrefix: "SLIM");
        var engine = new InstallerEngine(desc, ctx, Assembly.GetExecutingAssembly());

        InstallerOperationResult res = await engine.InstallAsync(explicitInstallDir: installDir, createShortcuts: false, externalPayloadPath: payload);
        Assert.True(res.Success, res.Message);

        string uninstallerPath = Path.Combine(installDir, "app", "SlimTestApp-Uninstall.exe");
        Assert.True(File.Exists(uninstallerPath));
        string extracted = await File.ReadAllTextAsync(uninstallerPath, Encoding.UTF8);
        Assert.Equal("MZ_SLIM_UNINSTALLER_STUB_V056", extracted);
    }

    [Fact]
    public void InstallerWizardForm_HidesSingleProfileRadioRow_And_PreservesWinFormsFreeBuild()
    {
        var singleDesc = new InstallerAppDescriptor("SingleApp", "Single App", "Test", "0.5.6", AllowAutoStart: false)
        {
            SupportedProfiles = new[] { "full" },
            DefaultProfile = "full"
        };
        var singleCtx = new InstallerRuntimeContext(cliDataDir: Path.Combine(_tempDir, "single-ui"), envPrefix: "SINGLE");
        using var singleWizard = new InstallerWizardForm(new InstallerEngine(singleDesc, singleCtx), singleCtx);
        Assert.Equal(new[] { "full" }, singleWizard.SupportedProfiles);
        Assert.Equal("full", singleWizard.SelectedProfileId);
        Assert.False(singleWizard.LightweightProfileRadioButton.Visible);
        Assert.False(singleWizard.FullProfileRadioButton.Visible);
        Assert.False(singleWizard.CustomProfileRadioButton.Visible);
        Assert.Equal(34, singleWizard.ShortcutsCheckBox.Top);

        // Verify Foundation.Installer is 100% WinForms-free and IsAotCompatible.
        string installerDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Foundation.Installer"));
        string csprojText = File.ReadAllText(Path.Combine(installerDir, "Foundation.Installer.csproj"));
        Assert.DoesNotContain("UseWindowsForms", csprojText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<IsAotCompatible>true</IsAotCompatible>", csprojText, StringComparison.OrdinalIgnoreCase);
        foreach (string csFile in Directory.EnumerateFiles(installerDir, "*.cs"))
        {
            string body = File.ReadAllText(csFile);
            Assert.DoesNotContain("using System.Windows.Forms", body, StringComparison.Ordinal);
            Assert.DoesNotContain("using System.Drawing", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DpapiSecretStore_CSharpAndPython_AreWireCompatible()
    {
        string vaultDir = Path.Combine(_tempDir, "dpapi-interop");
        var store = new DpapiSecretStore(vaultDir);
        store.SetSecret("CsOrigin", "HelloFromCSharp_056");
        Assert.Equal("HelloFromCSharp_056", store.GetSecret("csorigin"));

        string pySrc = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src"));
        var psi = new ProcessStartInfo("python")
        {
            ArgumentList =
            {
                "-B", "-c",
                "import sys; sys.path.insert(0, sys.argv[1]); from foundation_core import DpapiSecretStore; " +
                "s = DpapiSecretStore(sys.argv[2]); " +
                "assert s.get_secret('CSORIGIN') == 'HelloFromCSharp_056', 'mismatch'; " +
                "s.set_secret('PyOrigin', 'HelloFromPython_056')",
                pySrc,
                vaultDir
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi);
        if (proc is not null)
        {
            string err = proc.StandardError.ReadToEnd();
            proc.WaitForExit(15000);
            Assert.True(proc.ExitCode == 0, $"Python DPAPI interop check failed: {err}");
            Assert.Equal("HelloFromPython_056", store.GetSecret("pyorigin"));
        }
    }
}
