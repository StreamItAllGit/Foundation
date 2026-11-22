// @module: Foundation.Tests.SandboxAndRegistry
// @purpose: Verifies 4-drive Stage 2 sandbox seams, KnownFolderPaths, JsonHivePlatformRegistry, cross-process C#/Python lock contention, ExitCode=3 containment, and v0.4.1 shortcut/header fixes.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.IO.Compression, System.Text, System.Text.Json, System.Threading.Tasks, Foundation.Core, Foundation.Installer, Xunit
// @exports: FoundationSandboxAndRegistryTests

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Foundation.Core;
using Foundation.Installer;
using Xunit;

namespace Foundation.Tests;

public sealed class FoundationSandboxAndRegistryTests : IDisposable
{
    private readonly string _tempDir;

    public FoundationSandboxAndRegistryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FoundationSandboxTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void ProductionSafety_WhenSandboxRootUnset_RemainsInactive_And_KnownFolderPathsDelegateToHost()
    {
        var unsetEnv = new Dictionary<string, string?>();
        SandboxResolution inactive = SandboxEnvironmentSeam.Resolve(unsetEnv);

        Assert.False(inactive.IsSandboxActive);
        Assert.Null(inactive.SandboxRoot);
        Assert.Null(inactive.SystemDriveRoot);
        Assert.Null(inactive.StorageDriveRoot);
        Assert.Null(inactive.RemovableDriveRoot);
        Assert.Null(inactive.DiscDriveRoot);

        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, inactive));
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.DesktopDirectory, inactive));
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.Programs, inactive));
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.UserProfile, inactive));

        var appEnv = new AppEnvironment(
            "prod-safety-app",
            new[] { "--data-dir", _tempDir },
            baseDirectoryOverride: null,
            defaultConfigFileName: "settings.json",
            sandboxOverride: inactive);
        Assert.False(appEnv.Sandbox.IsSandboxActive);
        Assert.IsType<NoOpPlatformRegistry>(PlatformRegistryFactory.CreateDefault(inactive, suppressMutations: true));
        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<WindowsPlatformRegistry>(PlatformRegistryFactory.CreateDefault(inactive, suppressMutations: false));
        }
    }

    [Fact]
    public void AppIsolatedTestFlag_AlwaysBeatsFoundationSandboxRoot()
    {
        (SandboxResolution active, string sandboxRoot, _, _, _, _) = CreateIsolatedFourDriveLayout("isolated-override");
        Assert.True(active.IsSandboxActive);

        foreach (string truthy in new[] { "1", "true", "yes" })
        {
            var envOverride = new Dictionary<string, string?>
            {
                [SandboxEnvironmentSeam.EnvIsolatedTest] = truthy,
                [SandboxEnvironmentSeam.EnvSandboxRoot] = sandboxRoot,
                [SandboxEnvironmentSeam.EnvSandboxCDrive] = active.SystemDriveRoot,
                [SandboxEnvironmentSeam.EnvSandboxDDrive] = active.StorageDriveRoot,
                [SandboxEnvironmentSeam.EnvSandboxRemovableDrive] = active.RemovableDriveRoot,
                [SandboxEnvironmentSeam.EnvSandboxDiscDrive] = active.DiscDriveRoot
            };

            SandboxResolution resolved = SandboxEnvironmentSeam.Resolve(envOverride);
            Assert.False(resolved.IsSandboxActive);
            Assert.Null(resolved.SandboxRoot);
            Assert.Equal(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, resolved));
        }
    }

    [Fact]
    public async Task Stage2Active_ResolvesFourDrives_KnownFolders_DataDirPrecedence_And_ContainmentGuards()
    {
        (SandboxResolution sandbox, string sandboxRoot, string cDrive, string dDrive, string remDrive, string discDrive) =
            CreateIsolatedFourDriveLayout("stage2-core");

        Assert.True(sandbox.IsSandboxActive);
        Assert.Equal(Path.GetFullPath(sandboxRoot), sandbox.SandboxRoot);
        Assert.Equal(Path.GetFullPath(cDrive), sandbox.SystemDriveRoot);
        Assert.Equal(Path.GetFullPath(dDrive), sandbox.StorageDriveRoot);
        Assert.Equal(Path.GetFullPath(remDrive), sandbox.RemovableDriveRoot);
        Assert.Equal(Path.GetFullPath(discDrive), sandbox.DiscDriveRoot);

        string expectedProfile = Path.Combine(cDrive, "Users", "Sandbox");
        Assert.Equal(expectedProfile, sandbox.SandboxProfileDir);
        Assert.Equal(Path.Combine(sandboxRoot, "registry", "registry.hive.json"), sandbox.RegistryHivePath);
        Assert.Equal(Path.Combine(sandboxRoot, "locks", "registry.hive.lock"), sandbox.RegistryLockPath);

        Assert.Equal(expectedProfile, KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.UserProfile, sandbox));
        Assert.Equal(Path.Combine(expectedProfile, "AppData", "Local"), KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, sandbox));
        Assert.Equal(Path.Combine(expectedProfile, "AppData", "Roaming"), KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.ApplicationData, sandbox));
        Assert.Equal(Path.Combine(expectedProfile, "Desktop"), KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.DesktopDirectory, sandbox));
        Assert.Equal(Path.Combine(expectedProfile, "Documents"), KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.MyDocuments, sandbox));
        Assert.Equal(Path.Combine(cDrive, "Program Files"), KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.ProgramFiles, sandbox));
        Assert.Equal(Path.Combine(cDrive, "Program Files (x86)"), KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86, sandbox));
        Assert.Equal(Path.Combine(cDrive, "ProgramData"), KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.CommonApplicationData, sandbox));
        Assert.Equal(Path.Combine(cDrive, "Windows"), KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.Windows, sandbox));
        Assert.Equal(Path.Combine(expectedProfile, "AppData", "Local", "Programs"), KnownFolderPaths.ResolveLocalProgramsDirectory(sandbox));

        string hostDPrograms = Path.Combine("D:" + Path.DirectorySeparatorChar, "Programs");
        string hostDProgramsApp = Path.Combine(hostDPrograms, "Stage2TestApp");
        Assert.Equal(
            Path.Combine(dDrive, "Programs", "Stage2TestApp"),
            KnownFolderPaths.MapHostPathToSandbox(hostDProgramsApp, sandbox));

        string sandboxDataDir = Path.Combine(dDrive, "AppData", "stage2-data");
        Directory.CreateDirectory(sandboxDataDir);
        var ctx = new InstallerRuntimeContext(
            cliDataDir: sandboxDataDir,
            cliConfigPath: null,
            cliLogDir: null,
            dryRun: false,
            envPrefix: "STAGE2APP",
            sandboxOverride: sandbox);

        Assert.True(ctx.IsSandboxActive);
        Assert.False(ctx.IsStage1Isolated);
        Assert.False(ctx.ShouldSuppressHostMutations);
        Assert.True(ctx.ShouldSuppressLiveHostMutations);
        Assert.IsType<JsonHivePlatformRegistry>(ctx.Registry);

        var desc = new InstallerAppDescriptor("Stage2App", "Stage2 App", "TestStudio", "0.4.2");
        var engine = new InstallerEngine(desc, ctx);

        string sandboxProgramsApp = Path.Combine(dDrive, "Programs", "Stage2App");
        Assert.False(FileLockRetryHelper.IsCanonicalProductionOrSystemPath(sandboxProgramsApp, sandbox));
        Assert.True(FileLockRetryHelper.IsSafeIsolatedInstallDirectory(sandboxProgramsApp, sandbox));
        Assert.Equal(Path.GetFullPath(sandboxProgramsApp), engine.ValidateAndNormalizeInstallDirectory(sandboxProgramsApp));

        string legacyAppDir = Path.Combine(dDrive, "Programs", "LegacySample");
        Directory.CreateDirectory(legacyAppDir);
        File.WriteAllText(Path.Combine(legacyAppDir, "settings.json"), "{}", Encoding.UTF8);

        var coreEnv = new AppEnvironment(
            "legacysample",
            new[] { "--data-dir", sandboxDataDir },
            baseDirectoryOverride: null,
            defaultConfigFileName: "settings.json",
            sandboxOverride: sandbox);
        var coreTheme = new InstallerThemeOptions
        {
            AppId = "legacysample",
            DisplayName = "LegacySample",
            Version = "0.4.2",
            DefaultInstallDir = hostDProgramsApp
        };
        var coreEngine = new InstallerEngineCore(coreTheme, coreEnv);
        Assert.False(coreEngine.ShouldSuppressHostMutations);
        Assert.True(coreEngine.ShouldSuppressLiveHostMutations);
        Assert.Equal(legacyAppDir, coreEngine.DetectLegacyFlatInstall());
        Assert.Equal(Path.Combine(dDrive, "Programs", "Stage2TestApp"), coreEngine.ResolveTargetInstallDir());

        // Escapes into host OS, user profile, or host D:\Programs must fail closed with ExitCode = 3.
        string[] canonicalSystemOrProgramsEscapes =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EscapeApp"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "EscapeApp"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "EscapeApp"),
            hostDProgramsApp
        };
        string desktopEscape = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "EscapeApp");
        string[] allHostEscapes =
        [
            .. canonicalSystemOrProgramsEscapes,
            desktopEscape
        ];

        foreach (string escape in canonicalSystemOrProgramsEscapes)
        {
            Assert.True(
                FileLockRetryHelper.IsCanonicalProductionOrSystemPath(escape, sandbox),
                $"Expected canonical production/system rejection for '{escape}'.");
        }

        foreach (string escape in allHostEscapes)
        {
            Assert.False(
                FileLockRetryHelper.IsSafeIsolatedInstallDirectory(escape, sandbox),
                $"Expected IsSafeIsolatedInstallDirectory == false for '{escape}'.");

            var exSeam = Assert.Throws<InvalidOperationException>(() => SandboxEnvironmentSeam.AssertSandboxPathIfActive(escape, sandbox));
            Assert.True(SandboxEnvironmentSeam.IsContainmentViolation(exSeam));

            var exEngine = Assert.Throws<InvalidOperationException>(() => engine.ValidateAndNormalizeInstallDirectory(escape));
            Assert.True(SandboxEnvironmentSeam.IsContainmentViolation(exEngine));

            var exCore = Assert.Throws<InvalidOperationException>(() => coreEngine.ResolveTargetInstallDir(escape));
            Assert.True(SandboxEnvironmentSeam.IsContainmentViolation(exCore));
        }

        var exLegacy = Assert.Throws<InvalidOperationException>(() => coreEngine.DetectLegacyFlatInstall(hostDPrograms));
        Assert.True(SandboxEnvironmentSeam.IsContainmentViolation(exLegacy));

        var exOutsideCtx = Assert.Throws<InvalidOperationException>(() =>
            new InstallerRuntimeContext(
                cliDataDir: Path.Combine(_tempDir, "outside-sandbox"),
                cliConfigPath: null,
                cliLogDir: null,
                dryRun: false,
                envPrefix: "STAGE2APP",
                sandboxOverride: sandbox));
        Assert.True(SandboxEnvironmentSeam.IsContainmentViolation(exOutsideCtx));

        var exCollision = Assert.Throws<InvalidOperationException>(() => SandboxEnvironmentSeam.Resolve(new Dictionary<string, string?>
        {
            [SandboxEnvironmentSeam.EnvSandboxRoot] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Collision")
        }));
        Assert.True(SandboxEnvironmentSeam.IsContainmentViolation(exCollision));

        string payloadZip = BuildTestPayloadZip("Stage2App", Path.Combine("app", "Stage2App.exe"));
        string stage1Data = Path.Combine(_tempDir, "stage1-cli-data");
        Directory.CreateDirectory(stage1Data);

        Assert.Equal(3, StandardInstallerHost.Run(desc, new[] { "--data-dir", stage1Data, "--install-dir", hostDProgramsApp, "--payload", payloadZip, "--silent" }));
        Assert.Equal(3, StandardInstallerHost.RunWithResult(desc, new[] { "--data-dir", stage1Data, "--install-dir", canonicalSystemOrProgramsEscapes[0], "--payload", payloadZip, "--silent" }).ExitCode);
        Assert.Equal(3, StandardInstallerHost.Run(desc, new[] { "--data-dir", stage1Data, "--install-dir", canonicalSystemOrProgramsEscapes[2], "--payload", payloadZip, "--silent" }));

        var installResult = await engine.InstallAsync(
            explicitInstallDir: sandboxProgramsApp,
            createShortcuts: false,
            externalPayloadPath: payloadZip);
        Assert.True(installResult.Success);
        Assert.Equal(Path.GetFullPath(sandboxProgramsApp), installResult.InstallDirectory);
    }

    [Fact]
    public async Task JsonHivePlatformRegistry_SupportsAllSixKinds_BakRecovery_Factory_And_CrossLanguageContention()
    {
        (SandboxResolution sandbox, string sandboxRoot, _, _, _, _) = CreateIsolatedFourDriveLayout("registry-suite");
        IPlatformRegistry created = PlatformRegistryFactory.CreateDefault(sandbox, suppressMutations: false);
        var reg = Assert.IsType<JsonHivePlatformRegistry>(created);

        byte[] binarySample = [0xDE, 0xAD, 0xBE, 0xEF, 0x0A, 0xFF];
        string subKey = @"Software\FoundationTest\AllKinds";

        reg.SetValues(PlatformRegistryHive.CurrentUser, subKey, new Dictionary<string, PlatformRegistryValue>
        {
            ["StrVal"] = new(PlatformRegistryValueKind.String, "hello-sandbox"),
            ["ExpandVal"] = new(PlatformRegistryValueKind.ExpandString, @"%USERPROFILE%\Saves"),
            ["MultiVal"] = new(PlatformRegistryValueKind.MultiString, new[] { "alpha", "beta", "gamma" }),
            ["DwordVal"] = new(PlatformRegistryValueKind.DWord, 4242),
            ["QwordVal"] = new(PlatformRegistryValueKind.QWord, 0x1_0000_0005L),
            ["BinVal"] = new(PlatformRegistryValueKind.Binary, binarySample)
        });
        reg.SetValue(PlatformRegistryHive.LocalMachine, @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64", "Installed", 1, PlatformRegistryValueKind.DWord);
        reg.CreateSubKey(PlatformRegistryHive.ClassesRoot, ".fndtest");

        Assert.True(reg.SubKeyExists(PlatformRegistryHive.CurrentUser, subKey));
        Assert.True(reg.SubKeyExists(PlatformRegistryHive.ClassesRoot, ".fndtest"));
        Assert.Equal("hello-sandbox", reg.GetValue(PlatformRegistryHive.CurrentUser, subKey, "StrVal")?.AsString());
        Assert.Equal(@"%USERPROFILE%\Saves", reg.GetValue(PlatformRegistryHive.CurrentUser, subKey, "ExpandVal")?.AsString());
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, reg.GetValue(PlatformRegistryHive.CurrentUser, subKey, "MultiVal")?.AsMultiString());
        Assert.Equal(4242, reg.GetValue(PlatformRegistryHive.CurrentUser, subKey, "DwordVal")?.AsInt32());
        Assert.Equal(0x1_0000_0005L, reg.GetValue(PlatformRegistryHive.CurrentUser, subKey, "QwordVal")?.AsInt64());
        Assert.Equal(binarySample, reg.GetValue(PlatformRegistryHive.CurrentUser, subKey, "BinVal")?.AsBinary());

        using (var doc = JsonDocument.Parse(File.ReadAllText(reg.HiveFilePath, Encoding.UTF8)))
        {
            JsonElement root = doc.RootElement;
            Assert.Equal(JsonHivePlatformRegistry.SchemaUri, root.GetProperty("$schema").GetString());
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(sandboxRoot, root.GetProperty("sandboxRoot").GetString());
            Assert.EndsWith("Z", root.GetProperty("updatedAtUtc").GetString());

            JsonElement binEntry = root
                .GetProperty("registryHives")
                .GetProperty("HKCU")
                .GetProperty(subKey)
                .GetProperty("BinVal");
            Assert.Equal("REG_BINARY", binEntry.GetProperty("registryValueKind").GetString());
            Assert.Equal("deadbeef0aff", binEntry.GetProperty("registryValueData").GetString());
        }

        // Write a second update to rotate .bak, then corrupt primary and verify .bak recovery.
        reg.SetValue(PlatformRegistryHive.CurrentUser, subKey, "BackupCheck", "step-2", PlatformRegistryValueKind.String);
        Assert.True(File.Exists(reg.BackupFilePath));
        File.WriteAllText(reg.HiveFilePath, "{ corrupted_hive: [", Encoding.UTF8);

        Assert.Equal("hello-sandbox", reg.GetValue(PlatformRegistryHive.CurrentUser, subKey, "StrVal")?.AsString());
        Assert.Null(reg.GetValue(PlatformRegistryHive.CurrentUser, subKey, "BackupCheck"));

        // Verify missing RegistryHivePath on active sandbox throws containment violation.
        var brokenSandbox = sandbox with { RegistryHivePath = null };
        var exMissingHive = Assert.Throws<InvalidOperationException>(() => PlatformRegistryFactory.CreateDefault(brokenSandbox));
        Assert.True(SandboxEnvironmentSeam.IsContainmentViolation(exMissingHive));

        // Concurrent C# <-> Python contention stress test over registry.hive.lock + registry.hive.json.
        string stressRoot = Path.Combine(_tempDir, "stress-contention");
        string stressHive = Path.Combine(stressRoot, "registry", "registry.hive.json");
        string stressLock = Path.Combine(stressRoot, "locks", "registry.hive.lock");
        var stressReg = new JsonHivePlatformRegistry(stressHive, stressRoot, stressLock, TimeSpan.FromSeconds(25));
        stressReg.EnsureInitialized();

        string pythonSrcRoot = Path.Combine(ResolveFoundationRoot(), "Dev", "src");
        string pyHelperPath = Path.Combine(_tempDir, "py_contention_worker.py");
        File.WriteAllText(pyHelperPath, string.Join('\n', new[]
        {
            "import sys",
            "from pathlib import Path",
            "sys.path.insert(0, sys.argv[1])",
            "from foundation_core.pseudo_registry import JsonHivePlatformRegistry, PlatformRegistryHive, PlatformRegistryValueKind",
            "reg = JsonHivePlatformRegistry(Path(sys.argv[2]), Path(sys.argv[3]), Path(sys.argv[4]), lock_timeout_seconds=25.0)",
            "count = int(sys.argv[5])",
            "for i in range(count):",
            "    reg.set_value(PlatformRegistryHive.CURRENT_USER, r'Software\\ContentionTest\\Python', f'py_{i}', i, PlatformRegistryValueKind.DWORD)"
        }), new UTF8Encoding(false));

        const int csharpWorkers = 6;
        const int csharpWritesPerWorker = 4;
        const int pythonWrites = 18;

        var pyStart = new ProcessStartInfo(ResolvePythonExecutable())
        {
            ArgumentList = { "-B", pyHelperPath, pythonSrcRoot, stressHive, stressRoot, stressLock, pythonWrites.ToString() },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using Process pyProc = Process.Start(pyStart)!;

        var csTasks = new List<Task>(csharpWorkers);
        for (int w = 0; w < csharpWorkers; w++)
        {
            int workerId = w;
            csTasks.Add(Task.Run(() =>
            {
                var workerReg = new JsonHivePlatformRegistry(stressHive, stressRoot, stressLock, TimeSpan.FromSeconds(25));
                for (int i = 0; i < csharpWritesPerWorker; i++)
                {
                    workerReg.SetValue(
                        PlatformRegistryHive.CurrentUser,
                        @"Software\ContentionTest\CSharp",
                        $"cs_{workerId}_{i}",
                        $"val_{workerId}_{i}",
                        PlatformRegistryValueKind.String);
                }
            }));
        }

        await Task.WhenAll(csTasks);
        string pyErr = await pyProc.StandardError.ReadToEndAsync();
        string pyOut = await pyProc.StandardOutput.ReadToEndAsync();
        Assert.True(pyProc.WaitForExit(30000), $"Python contention worker timed out. stderr={pyErr}");
        Assert.True(pyProc.ExitCode == 0, $"Python contention worker exited {pyProc.ExitCode}: {pyErr}\n{pyOut}");

        IReadOnlyDictionary<string, PlatformRegistryValue> csValues = stressReg.GetValues(PlatformRegistryHive.CurrentUser, @"Software\ContentionTest\CSharp");
        IReadOnlyDictionary<string, PlatformRegistryValue> pyValues = stressReg.GetValues(PlatformRegistryHive.CurrentUser, @"Software\ContentionTest\Python");

        for (int w = 0; w < csharpWorkers; w++)
        {
            for (int i = 0; i < csharpWritesPerWorker; i++)
            {
                Assert.Equal($"val_{w}_{i}", csValues[$"cs_{w}_{i}"].AsString());
            }
        }
        for (int i = 0; i < pythonWrites; i++)
        {
            Assert.Equal(i, pyValues[$"py_{i}"].AsInt32());
        }
        Assert.False(File.Exists(stressLock));
    }

    [Fact]
    public async Task Installer041BugFixes_SanitizesColonShortcuts_And_ClampsLongWizardTitleWithoutOverlap()
    {
        Assert.Equal("Harvest Moon - Test", WindowsShortcutWriter.SanitizeShortcutFileName("Harvest Moon: Test"));
        Assert.Equal("Application", WindowsShortcutWriter.SanitizeShortcutFileName("   "));
        Assert.DoesNotContain(':', WindowsShortcutWriter.SanitizeShortcutFileName("A:B/C\\D*E?F<G>H|I\"J"));

        (SandboxResolution sandbox, _, _, string dDrive, _, _) = CreateIsolatedFourDriveLayout("bugfix-041");
        string sandboxDataDir = Path.Combine(dDrive, "AppData", "harvest-data");
        Directory.CreateDirectory(sandboxDataDir);

        var colonDesc = new InstallerAppDescriptor(
            ProductName: "HarvestMoonTest",
            DisplayName: "Harvest Moon: Test",
            Publisher: "TestStudio",
            Version: "0.4.2");

        var ctx = new InstallerRuntimeContext(
            cliDataDir: sandboxDataDir,
            cliConfigPath: null,
            cliLogDir: null,
            dryRun: false,
            envPrefix: colonDesc.EnvPrefix,
            sandboxOverride: sandbox);
        var engine = new InstallerEngine(colonDesc, ctx);

        string installDir = Path.Combine(dDrive, "Programs", "HarvestMoonTest");
        string payloadZip = BuildTestPayloadZip("HarvestMoonTest", Path.Combine("app", "HarvestMoonTest.exe"));
        InstallerOperationResult installRes = await engine.InstallAsync(
            explicitInstallDir: installDir,
            createShortcuts: true,
            externalPayloadPath: payloadZip);
        Assert.True(installRes.Success);

        string expectedDesktopLnk = Path.Combine(
            KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.DesktopDirectory, sandbox),
            "Harvest Moon - Test.lnk");
        string expectedStartMenuLnk = Path.Combine(
            KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.Programs, sandbox),
            "HarvestMoonTest",
            "Harvest Moon - Test.lnk");

        if (OperatingSystem.IsWindows())
        {
            Assert.True(File.Exists(expectedDesktopLnk));
            Assert.True(File.Exists(expectedStartMenuLnk));
            Assert.True(WindowsShortcutWriter.IsValidShellLinkHeader(File.ReadAllBytes(expectedDesktopLnk)));
            Assert.Equal(
                "Harvest Moon: Test",
                ctx.Registry.GetValue(PlatformRegistryHive.CurrentUser, colonDesc.WindowsUninstallRegistrySubKey, "DisplayName")?.AsString());
        }

        InstallerOperationResult uninstallRes = await engine.UninstallAsync(installDir, purgeData: true);
        Assert.True(uninstallRes.Success);
        Assert.False(File.Exists(expectedDesktopLnk));
        Assert.False(File.Exists(expectedStartMenuLnk));
        Assert.False(ctx.Registry.SubKeyExists(PlatformRegistryHive.CurrentUser, colonDesc.WindowsUninstallRegistrySubKey));

        // 50-char title header badge non-overlap & DPI-safe height assertion on InstallerWizardForm.
        const string longTitle = "Harvest Moon Ultra Long Recompilation Title Fifty!";
        Assert.Equal(50, longTitle.Length);

        var longDesc = new InstallerAppDescriptor(
            ProductName: "LongTitleApp",
            DisplayName: longTitle,
            Publisher: "TestStudio",
            Version: "0.4.2");

        using var wizard = new InstallerWizardForm(new InstallerEngine(longDesc, ctx), ctx);
        Assert.Equal(650, wizard.ClientSize.Width);
        Assert.Equal(460, wizard.ClientSize.Height);
        Assert.False(wizard.TitleLabel.AutoSize);
        Assert.True(wizard.TitleLabel.AutoEllipsis);
        Assert.True(wizard.TitleLabel.Height >= 28);
        Assert.True(
            wizard.TitleLabel.Right <= wizard.ModeBadgeLabel.Left - 12,
            $"TitleLabel.Right ({wizard.TitleLabel.Right}) overlapped ModeBadgeLabel.Left - 12 ({wizard.ModeBadgeLabel.Left - 12}).");
        Assert.True(wizard.SubtitleLabel.Right <= wizard.ModeBadgeLabel.Left - 12);

        wizard.SetDetailsExpanded(true);
        Assert.Equal(650, wizard.ClientSize.Width);
        Assert.Equal(570, wizard.ClientSize.Height);
        Assert.True(wizard.TitleLabel.Right <= wizard.ModeBadgeLabel.Left - 12);

        // Verify secondaryDir containment guard on InstallAsync and Tier 2 InstallerWizardForm(InstallerEngineCore) header layout.
        var exSecondary = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.InstallAsync(
            explicitInstallDir: installDir,
            createShortcuts: false,
            registerAutoStart: false,
            externalPayloadPath: payloadZip,
            installProfile: null,
            secondaryInstallDir: Path.Combine("D:" + Path.DirectorySeparatorChar, "Programs", "SecondaryEscape"),
            secondaryFlagName: "--suite-dir"));
        Assert.True(SandboxEnvironmentSeam.IsContainmentViolation(exSecondary));

        var coreTheme = new InstallerThemeOptions
        {
            AppId = "longtitlecore",
            DisplayName = longTitle,
            Version = "0.4.2",
            DefaultInstallDir = Path.Combine(dDrive, "Programs", "LongTitleCore")
        };
        var coreEnv = new AppEnvironment(
            "longtitlecore",
            new[] { "--data-dir", sandboxDataDir },
            baseDirectoryOverride: null,
            defaultConfigFileName: "settings.json",
            sandboxOverride: sandbox);
        using var coreWizard = new InstallerWizardForm(new InstallerEngineCore(coreTheme, coreEnv));
        Assert.Equal(650, coreWizard.ClientSize.Width);
        Assert.Equal(460, coreWizard.ClientSize.Height);
        Assert.True(coreWizard.TitleLabel.Height >= 28);
        Assert.True(coreWizard.TitleLabel.Right <= coreWizard.ModeBadgeLabel.Left - 12);
        coreWizard.SetDetailsExpanded(true);
        Assert.Equal(570, coreWizard.ClientSize.Height);
    }

    [Fact]
    public void SandboxDriveController_LoadsEnvStore_InfersRoot_And_ResolvesFourDriveMounts()
    {
        string ctrlRoot = Path.Combine(_tempDir, "ctrl-root");
        string secretsEnv = Path.Combine(_tempDir, "secrets.env");
        string devEnv = Path.Combine(_tempDir, "dev.env");
        string cBacking = Path.Combine(ctrlRoot, "C");
        string dBacking = Path.Combine(ctrlRoot, "D");
        Directory.CreateDirectory(cBacking);
        Directory.CreateDirectory(dBacking);

        File.WriteAllText(secretsEnv, $"SANDBOX_C_DRIVE=\"Q:\"\nSANDBOX_D_DRIVE=R:\nSANDBOX_DISC_DRIVE=T:\n", Encoding.UTF8);
        File.WriteAllText(devEnv, "SANDBOX_REMOVABLE_DRIVE=U:\nSANDBOX_D_DRIVE=S:\n", Encoding.UTF8);

        var envStore = SandboxDriveController.LoadEnvStore(
            devEnvPath: devEnv,
            secretsEnvPath: secretsEnv,
            includeUserSecretsFallback: false,
            cliOverrides: new Dictionary<string, string?> { ["SANDBOX_DISC_DRIVE"] = "V:" },
            processEnvOverride: new Dictionary<string, string?>());

        Assert.Equal("Q:", envStore["SANDBOX_C_DRIVE"]);
        Assert.Equal("S:", envStore["SANDBOX_D_DRIVE"]);
        Assert.Equal("U:", envStore["SANDBOX_REMOVABLE_DRIVE"]);
        Assert.Equal("V:", envStore["SANDBOX_DISC_DRIVE"]);

        var substMap = SandboxDriveController.ParseSubstOutput($"Q:\\: => {cBacking}\r\nS:\\: => {dBacking}\r\n");
        Assert.Equal(Path.GetFullPath(cBacking), substMap["Q:"]);
        Assert.Equal(Path.GetFullPath(dBacking), substMap["S:"]);
        Assert.Equal(Path.GetFullPath(ctrlRoot), SandboxDriveController.InferSandboxRoot(envStore, substMap));

        IReadOnlyList<SandboxMountSpec> mounts = SandboxDriveController.ResolveDriveMounts(envStore, substMap);
        Assert.Equal(4, mounts.Count);
        Assert.True(mounts[0].IsEnabled);
        Assert.Equal(Path.GetFullPath(cBacking), mounts[0].DirectoryPath);
        Assert.True(mounts[1].IsEnabled);
        Assert.False(mounts[2].IsEnabled);
        Assert.False(mounts[3].IsEnabled);
    }

    [Fact]
    public void RedirectedProfileEnvVars_StillResolveSandbox_And_MapHostStartMenuPrograms()
    {
        if (!OperatingSystem.IsWindows())
            return;

        (SandboxResolution res, string root, string cDrive, _, _, _) = CreateIsolatedFourDriveLayout("redirected-env");
        string sandboxProfile = Path.Combine(cDrive, "Users", "Sandbox");
        string sandboxAppData = Path.Combine(sandboxProfile, "AppData", "Roaming");
        string sandboxLocalAppData = Path.Combine(sandboxProfile, "AppData", "Local");
        Directory.CreateDirectory(sandboxAppData);
        Directory.CreateDirectory(sandboxLocalAppData);

        string? prevProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        string? prevAppData = Environment.GetEnvironmentVariable("APPDATA");
        string? prevLocal = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        try
        {
            Environment.SetEnvironmentVariable("USERPROFILE", sandboxProfile);
            Environment.SetEnvironmentVariable("APPDATA", sandboxAppData);
            Environment.SetEnvironmentVariable("LOCALAPPDATA", sandboxLocalAppData);

            SandboxResolution resolved = SandboxEnvironmentSeam.Resolve(new Dictionary<string, string?>
            {
                [SandboxEnvironmentSeam.EnvSandboxRoot] = root,
                [SandboxEnvironmentSeam.EnvSandboxCDrive] = cDrive,
                [SandboxEnvironmentSeam.EnvSandboxProfileDir] = sandboxProfile
            });

            Assert.True(resolved.IsSandboxActive);
            string hostPrograms = KnownFolderPaths.GetHostFolderPath(Environment.SpecialFolder.Programs);
            Assert.False(hostPrograms.StartsWith(sandboxProfile, StringComparison.OrdinalIgnoreCase));
            string hostDesktop = KnownFolderPaths.GetHostFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string hostDocs = KnownFolderPaths.GetHostFolderPath(Environment.SpecialFolder.MyDocuments);
            Assert.False(hostDesktop.StartsWith(sandboxProfile, StringComparison.OrdinalIgnoreCase));
            Assert.False(hostDocs.StartsWith(sandboxProfile, StringComparison.OrdinalIgnoreCase));

            string hostShortcut = Path.Combine(hostPrograms, "ProbeApp", "ProbeApp.lnk");
            string mapped = KnownFolderPaths.MapHostPathToSandbox(hostShortcut, res);
            Assert.StartsWith(sandboxAppData, mapped, StringComparison.OrdinalIgnoreCase);
            Assert.False(FileLockRetryHelper.IsCanonicalProductionOrSystemPath(
                Path.Combine(sandboxLocalAppData, "Programs", "ProbeApp"), resolved));
        }
        finally
        {
            Environment.SetEnvironmentVariable("USERPROFILE", prevProfile);
            Environment.SetEnvironmentVariable("APPDATA", prevAppData);
            Environment.SetEnvironmentVariable("LOCALAPPDATA", prevLocal);
        }
    }

    private (SandboxResolution Resolution, string Root, string CDrive, string DDrive, string RemDrive, string DiscDrive) CreateIsolatedFourDriveLayout(string label)
    {
        string root = Path.Combine(_tempDir, label);
        string cDrive = Path.Combine(root, "C");
        string dDrive = Path.Combine(root, "D");
        string remDrive = Path.Combine(root, "E");
        string discDrive = Path.Combine(root, "Disc");

        Directory.CreateDirectory(cDrive);
        Directory.CreateDirectory(dDrive);
        Directory.CreateDirectory(remDrive);
        Directory.CreateDirectory(discDrive);

        SandboxResolution resolution = SandboxEnvironmentSeam.Resolve(new Dictionary<string, string?>
        {
            [SandboxEnvironmentSeam.EnvSandboxRoot] = root,
            [SandboxEnvironmentSeam.EnvSandboxCDrive] = cDrive,
            [SandboxEnvironmentSeam.EnvSandboxDDrive] = dDrive,
            [SandboxEnvironmentSeam.EnvSandboxRemovableDrive] = remDrive,
            [SandboxEnvironmentSeam.EnvSandboxDiscDrive] = discDrive
        });

        return (resolution, root, cDrive, dDrive, remDrive, discDrive);
    }

    private string BuildTestPayloadZip(string appName, string exeRelativePath)
    {
        string zipPath = Path.Combine(_tempDir, $"{appName}-{Guid.NewGuid():N}.zip");
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entry = zip.CreateEntry(exeRelativePath.Replace('\\', '/'));
        using var sw = new StreamWriter(entry.Open(), Encoding.UTF8);
        sw.Write($"MZ_TEST_{appName}");
        return zipPath;
    }

    private static string ResolveFoundationRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Foundation.sln")))
            {
                return dir.Parent!.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Unable to locate Foundation repository root containing Dev/Foundation.sln.");
    }

    private static string ResolvePythonExecutable()
    {
        string py313 = Path.Combine("C:" + Path.DirectorySeparatorChar, "Python313", "python.exe");
        return File.Exists(py313) ? py313 : "python";
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
            // Ignore cleanup errors on temp directories
        }
    }
}
