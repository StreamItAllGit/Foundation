// @module: Foundation.Tests.MarkupInstaller
// @purpose: Verifies declarative installer.manifest.json schema v2 loading, layout/profile/tooltip wiring, and HKCU\Software\StreamItAll\LastInstallBaseDir precedence & isolation.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.IO.Compression, System.Text, System.Threading.Tasks, Foundation.Core, Foundation.Installer, Xunit
// @exports: FoundationMarkupInstallerTests

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;
using Foundation.Installer;
using Xunit;

namespace Foundation.Tests;

public sealed class FoundationMarkupInstallerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly List<string> _junctions = new();

    public FoundationMarkupInstallerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FoundationMarkupTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task MarkupManifest_ParsesSchemaV2_And_AppliesToDescriptor_And_WizardSurface()
    {
        const string manifestJson = """
        {
          "$schema": "https://schemas.local/manifest.v2.json",
          "schemaVersion": 2,
          "manifestType": "installer-manifest",
          "appId": "demogame",
          "version": "0.5.0",
          "updatedAtUtc": "2026-11-16T18:00:00.000Z",
          "appTitle": "Demo Game Title",
          "publisher": "SunkenRootStudios",
          "subtitle": "Multi-line declarative installer verification banner.",
          "studioName": "SunkenRootStudios",
          "umbrellaSubpath": "SunkenRootStudios\\DemoGame",
          "mainExecutableRelativePath": "app\\DemoGame.exe",
          "layoutMode": "multi-line",
          "supportedProfiles": ["lightweight", "full", "custom"],
          "defaultProfile": "custom",
          "allowAutoStart": false,
          "theme": {
            "background": "#10141C",
            "surface": "#1B2230",
            "accent": "#22C55E",
            "text": "#F8FAFC",
            "border": "#334155"
          },
          "buttons": {
            "installText": "Begin Setup",
            "upgradeText": "Update Game",
            "cancelText": "Exit"
          },
          "tooltips": {
            "installDir": {
              "title": "Install Directory",
              "description": "Choose a drive with at least 2 GB of free space."
            }
          },
          "secondaryPickers": [
            {
              "flagName": "--reengine-dir",
              "label": "Companion reEngine Directory:",
              "defaultPath": "C:\\Games\\reEngine"
            }
          ]
        }
        """;

        InstallerMarkupManifest manifest = InstallerManifestStore.LoadFromJson(manifestJson);
        Assert.Equal("multi-line", manifest.LayoutMode);
        Assert.Equal("custom", manifest.DefaultProfile);
        Assert.Contains("custom", manifest.SupportedProfiles);

        var baseDesc = new InstallerAppDescriptor("DemoGame", "Old Name", "OldPub", "0.1.0");
        InstallerAppDescriptor merged = InstallerManifestStore.ApplyToDescriptor(baseDesc, manifest);
        Assert.Equal("Demo Game Title", merged.DisplayName);
        Assert.Equal("0.5.0", merged.Version);
        Assert.False(merged.AllowAutoStart);
        Assert.Equal("multi-line", merged.LayoutMode);

        InstallerThemeOptions theme = InstallerManifestStore.ToThemeOptions(manifest, Path.Combine(_tempDir, "install"));
        Assert.Equal("multi-line", theme.LayoutMode);
        Assert.Equal("custom", theme.DefaultProfileId);

        var ctx = new InstallerRuntimeContext(cliDataDir: Path.Combine(_tempDir, "isolated"), envPrefix: "DEMOGAME");
        var engine = new InstallerEngine(merged, ctx);
        SynchronizationContext? prevSyncCtx = SynchronizationContext.Current;
        try
        {
            using var wizard = new InstallerWizardForm(engine, ctx);
            Assert.Equal("multi-line", wizard.LayoutMode);
            Assert.Contains("custom", wizard.SupportedProfiles);
            Assert.True(wizard.CustomProfileRadioButton.Checked);
            Assert.Equal("custom", wizard.SelectedProfileId);
            Assert.False(wizard.IsAutoStartAllowed);
            Assert.Equal("Begin Setup", wizard.PrimaryButton.Text);
            Assert.Equal("Exit", wizard.CancelButton.Text);
            string? tip = wizard.GetControlTooltip(wizard.InstallDirTextBox);
            Assert.NotNull(tip);
            Assert.Contains("Install Directory", tip);
            Assert.Contains(Environment.NewLine, tip);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prevSyncCtx);
        }

        string manifestDir = Path.Combine(_tempDir, "disk-manifest");
        Directory.CreateDirectory(manifestDir);
        await File.WriteAllTextAsync(Path.Combine(manifestDir, "installer.manifest.json"), manifestJson);
        Assert.NotNull(InstallerManifestStore.TryLoadFromDirectoryOrAssembly(manifestDir, null));

        Assert.Throws<InvalidDataException>(() => InstallerManifestStore.LoadFromJson("{\"schemaVersion\":1,\"manifestType\":\"installer-manifest\",\"appId\":\"a\"}"));
        Assert.Throws<InvalidDataException>(() => InstallerManifestStore.LoadFromJson("{\"schemaVersion\":2,\"manifestType\":\"installer-manifest\",\"appId\":\"a\",\"layoutMode\":\"grid\"}"));

        InstallerMarkupManifest noLw = InstallerManifestStore.LoadFromJson("{\"schemaVersion\":2,\"manifestType\":\"installer-manifest\",\"appId\":\"a\",\"supportedProfiles\":[\"full\",\"custom\"],\"defaultProfile\":\"lightweight\"}");
        Assert.Equal("full", noLw.DefaultProfile);
        SynchronizationContext? prevNoLwSyncCtx = SynchronizationContext.Current;
        try
        {
            using var noLwWizard = new InstallerWizardForm(new InstallerEngine(InstallerManifestStore.ApplyToDescriptor(baseDesc, noLw), ctx), ctx);
            Assert.Equal("full", noLwWizard.SelectedProfileId);
            Assert.True(noLwWizard.FullProfileRadioButton.Checked);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prevNoLwSyncCtx);
        }
    }

    [Fact]
    public async Task LastInstallBaseDir_ResolvesFreshSuggestion_HonorsUpgrade_And_RejectsIsolatedTempReparse()
    {
        string sandboxRoot = Path.Combine(_tempDir, "sbx");
        string cDrive = Path.Combine(sandboxRoot, "C");
        string dDrive = Path.Combine(sandboxRoot, "D");
        string profileDir = Path.Combine(cDrive, "Users", "Sandbox");
        Directory.CreateDirectory(profileDir);
        Directory.CreateDirectory(dDrive);

        var sandbox = new SandboxResolution(
            IsSandboxActive: true, SandboxRoot: sandboxRoot, SystemDriveRoot: cDrive, StorageDriveRoot: dDrive,
            RemovableDriveRoot: null, DiscDriveRoot: null, SandboxProfileDir: profileDir,
            RegistryHivePath: Path.Combine(sandboxRoot, "registry", "registry.hive.json"),
            RegistryBaselinePath: Path.Combine(sandboxRoot, "registry", "registry.baseline.json"),
            RegistryLockPath: Path.Combine(sandboxRoot, "locks", "registry.hive.lock"));

        var registry = new JsonHivePlatformRegistry(sandbox.RegistryHivePath!, sandboxRoot, sandbox.RegistryLockPath);
        string customBase = Path.Combine(dDrive, "CustomApps");
        Directory.CreateDirectory(customBase);
        registry.SetValue(PlatformRegistryHive.CurrentUser, InstallerEngine.SharedStreamItAllRegistrySubKey, InstallerEngine.LastInstallBaseDirValueName, customBase);

        var descriptor = new InstallerAppDescriptor("DemoApp", "Demo App", "StreamItAll", "0.5.0", UmbrellaSubpath: @"StreamItAll\DemoApp");
        var ctx = new InstallerRuntimeContext(null, null, null, false, "DEMOAPP", sandbox, registry);
        var engine = new InstallerEngine(descriptor, ctx);

        Assert.Equal(Path.GetFullPath(Path.Combine(customBase, "StreamItAll", "DemoApp")), engine.ResolveDefaultInstallDirectory());

        string existingUpgradeDir = Path.Combine(dDrive, "ExistingDir", "DemoApp");
        Directory.CreateDirectory(existingUpgradeDir);
        registry.SetValue(PlatformRegistryHive.CurrentUser, descriptor.WindowsUninstallRegistrySubKey, "InstallLocation", existingUpgradeDir);
        Assert.Equal(Path.GetFullPath(existingUpgradeDir), engine.ResolveDefaultInstallDirectory());
        registry.DeleteSubKeyTree(PlatformRegistryHive.CurrentUser, descriptor.WindowsUninstallRegistrySubKey);

        string payloadPath = Path.Combine(_tempDir, "demo.zip");
        using (var fs = File.Create(payloadPath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            using var sw = new StreamWriter(zip.CreateEntry("DemoApp.exe").Open(), Encoding.UTF8);
            sw.Write("MZ_DEMO");
        }

        string chosenInstallDir = Path.Combine(dDrive, "NewVaultBase", "StreamItAll", "DemoApp");
        var installRes = await engine.InstallAsync(new InstallerInstallOptions(ExplicitInstallDir: chosenInstallDir, CreateShortcuts: false, ExternalPayloadPath: payloadPath));
        Assert.True(installRes.Success);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(dDrive, "NewVaultBase")),
            registry.GetValue(PlatformRegistryHive.CurrentUser, InstallerEngine.SharedStreamItAllRegistrySubKey, InstallerEngine.LastInstallBaseDirValueName)?.AsString());

        var stage1Ctx = new InstallerRuntimeContext(Path.Combine(_tempDir, "stage1"), null, null, false, "DEMOAPP", null, registry);
        Assert.False(InstallerEngine.TrySaveLastInstallBaseDir(registry, Path.Combine(dDrive, "IgnoredBase", "StreamItAll", "DemoApp"), @"StreamItAll\DemoApp", SandboxResolution.Inactive, stage1Ctx.ShouldSuppressHostMutations));

        Assert.False(InstallerEngine.TryValidateLastInstallBaseDir(Path.Combine(Path.GetTempPath(), "TempInstallBase"), SandboxResolution.Inactive, out _));
        Assert.False(InstallerEngine.TryValidateLastInstallBaseDir(@"C:\", SandboxResolution.Inactive, out _));
        Assert.False(InstallerEngine.TryValidateLastInstallBaseDir("D:", SandboxResolution.Inactive, out _));
        Assert.False(InstallerEngine.TrySaveLastInstallBaseDir(registry, @"D:\DemoApp", @"StreamItAll\DemoApp", SandboxResolution.Inactive, false));

        string? prevIso = Environment.GetEnvironmentVariable("APP_ISOLATED_TEST");
        try
        {
            Environment.SetEnvironmentVariable("APP_ISOLATED_TEST", "1");
            var isoCtx = new InstallerRuntimeContext(Path.Combine(_tempDir, "iso-e2e"), null, null, false, "DEMOAPP", null, null);
            var isoEng = new InstallerEngine(descriptor, isoCtx);
            string preVal = registry.GetValue(PlatformRegistryHive.CurrentUser, InstallerEngine.SharedStreamItAllRegistrySubKey, InstallerEngine.LastInstallBaseDirValueName)?.AsString() ?? string.Empty;
            var isoRes = await isoEng.InstallAsync(new InstallerInstallOptions(CreateShortcuts: false, ExternalPayloadPath: payloadPath));
            Assert.True(isoRes.Success);
            Assert.Equal(preVal, registry.GetValue(PlatformRegistryHive.CurrentUser, InstallerEngine.SharedStreamItAllRegistrySubKey, InstallerEngine.LastInstallBaseDirValueName)?.AsString());
        }
        finally { Environment.SetEnvironmentVariable("APP_ISOLATED_TEST", prevIso); }

        if (OperatingSystem.IsWindows())
        {
            string targetDir = Path.Combine(dDrive, "JunctionTarget");
            string junctionDir = Path.Combine(dDrive, "JunctionBase");
            Directory.CreateDirectory(targetDir);
            using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junctionDir}\" \"{targetDir}\"") { CreateNoWindow = true, UseShellExecute = false })!;
            p.WaitForExit(5000);
            if (Directory.Exists(junctionDir))
            {
                _junctions.Add(junctionDir);
                Assert.False(InstallerEngine.TryValidateLastInstallBaseDir(junctionDir, sandbox, out _));
            }
        }
    }

    [Fact]
    public void MarkupManifest_PrefersSuiteId_WithLegacySuiteNameFallback()
    {
        InstallerMarkupManifest suiteIdOnly = InstallerManifestStore.LoadFromJson(
            "{\"schemaVersion\":2,\"manifestType\":\"installer-manifest\",\"appId\":\"reengine\",\"suiteId\":\"reEngine\"}");
        Assert.Equal("reEngine", suiteIdOnly.ResolveEffectiveSuiteId());
        Assert.Equal("reEngine", InstallerManifestStore.ToThemeOptions(suiteIdOnly).SuiteName);

        InstallerMarkupManifest legacySuiteNameOnly = InstallerManifestStore.LoadFromJson(
            "{\"schemaVersion\":2,\"manifestType\":\"installer-manifest\",\"appId\":\"reengine\",\"suiteName\":\"reEngine\"}");
        Assert.Equal("reEngine", legacySuiteNameOnly.ResolveEffectiveSuiteId());
        Assert.Equal("reEngine", InstallerManifestStore.ToThemeOptions(legacySuiteNameOnly).SuiteName);

        InstallerMarkupManifest bothPresent = InstallerManifestStore.LoadFromJson(
            "{\"schemaVersion\":2,\"manifestType\":\"installer-manifest\",\"appId\":\"reengine\",\"suiteId\":\"reEngine\",\"suiteName\":\"LegacySuite\"}");
        Assert.Equal("reEngine", bothPresent.ResolveEffectiveSuiteId());
        var baseDesc = new InstallerAppDescriptor("reEngine", "reEngine", "StreamItAll", "0.3.0", SuiteName: "OldSuite");
        Assert.Equal("reEngine", InstallerManifestStore.ApplyToDescriptor(baseDesc, bothPresent).SuiteName);
        Assert.Equal("reEngine", InstallerManifestStore.ToThemeOptions(bothPresent).SuiteName);
    }

    [Fact]
    public void UmbrellaSubpath_And_LastInstallBaseDir_PreventVersionLeafDoubling_And_SelfHealPoisonedRegistry()
    {
        const string versionedUmbrella = @"StreamItAll\reEngine\engines\reSlate\v0.3.0-hf.1";
        var desc = new InstallerAppDescriptor("reSlate", "reSlate", "StreamItAll", "0.3.0-hf.1", UmbrellaSubpath: versionedUmbrella);
        Assert.Equal(Path.Combine("StreamItAll", "reEngine", "engines", "reSlate", "v0.3.0-hf.1"), desc.GetEffectiveUmbrellaSubpath());

        var theme = new InstallerThemeOptions
        {
            AppId = "reslate",
            DisplayName = "reSlate",
            Version = "0.3.0-hf.1",
            DefaultInstallDir = _tempDir,
            UmbrellaSubpath = versionedUmbrella
        };
        Assert.Equal(Path.Combine("StreamItAll", "reEngine", "engines", "reSlate", "v0.3.0-hf.1"), theme.GetEffectiveUmbrellaSubpath());
        var customDesc = new InstallerAppDescriptor("CustomApp", "Custom App", "StreamItAll", "0.5.2", UmbrellaSubpath: @"CustomStudio\CustomSuite");
        Assert.Equal(Path.Combine("CustomStudio", "CustomSuite", "CustomApp"), customDesc.GetEffectiveUmbrellaSubpath());

        string sbxRoot = Path.Combine(_tempDir, "sbx2");
        string cDrive = Path.Combine(sbxRoot, "C");
        string dDrive = Path.Combine(sbxRoot, "D");
        string profileDir = Path.Combine(cDrive, "Users", "Sandbox");
        Directory.CreateDirectory(profileDir);
        Directory.CreateDirectory(dDrive);
        var sandbox = new SandboxResolution(
            true, sbxRoot, cDrive, dDrive, null, null, profileDir,
            Path.Combine(sbxRoot, "registry", "registry.hive.json"),
            Path.Combine(sbxRoot, "registry", "registry.baseline.json"),
            Path.Combine(sbxRoot, "locks", "registry.hive.lock"));
        var reg = new JsonHivePlatformRegistry(sandbox.RegistryHivePath!, sbxRoot, sandbox.RegistryLockPath);

        string cleanGamesMisc = Path.Combine(dDrive, "Games", "Misc");
        Directory.CreateDirectory(cleanGamesMisc);
        string reSlateInstallDir = Path.Combine(cleanGamesMisc, "StreamItAll", "reEngine", "engines", "reSlate", "v0.3.0-hf.1");
        Assert.Equal(Path.GetFullPath(cleanGamesMisc), InstallerEngine.ExtractInstallBaseDirectory(reSlateInstallDir, versionedUmbrella));
        Assert.Equal(Path.GetFullPath(cleanGamesMisc), InstallerEngine.ExtractInstallBaseDirectory(reSlateInstallDir, @"StreamItAll\reEngine\engines\reSlate"));
        Assert.True(InstallerEngine.TrySaveLastInstallBaseDir(reg, reSlateInstallDir, versionedUmbrella, sandbox, false));
        Assert.Equal(Path.GetFullPath(cleanGamesMisc), reg.GetValue(PlatformRegistryHive.CurrentUser, InstallerEngine.SharedStreamItAllRegistrySubKey, InstallerEngine.LastInstallBaseDirValueName)?.AsString());

        string poisonedBase = Path.Combine(cleanGamesMisc, "StreamItAll", "reEngine", "engines", "reSlate");
        reg.SetValue(PlatformRegistryHive.CurrentUser, InstallerEngine.SharedStreamItAllRegistrySubKey, InstallerEngine.LastInstallBaseDirValueName, poisonedBase);
        Assert.True(InstallerEngine.TryResolveSuggestedInstallDirFromHistory(reg, @"StreamItAll\reEngine\games\MeadowValley-BackToEarth-SlateOne-04", sandbox, out string suggested));
        Assert.Equal(Path.GetFullPath(Path.Combine(cleanGamesMisc, "StreamItAll", "reEngine", "games", "MeadowValley-BackToEarth-SlateOne-04")), suggested);

        Assert.True(InstallerEngine.TryValidateLastInstallBaseDir(cleanGamesMisc, sandbox, out _));
        Assert.False(InstallerEngine.TryValidateLastInstallBaseDir(Path.Combine(dDrive, "Custom", "engines", "reSlate"), sandbox, out _));
        Assert.False(InstallerEngine.TryResolveSuggestedInstallDirFromHistory(
            regWith(dDrive, sbxRoot, reg, Path.Combine(dDrive, "StreamItAll", "reEngine", "engines", "reSlate")),
            @"StreamItAll\reEngine\games\Demo", sandbox, out _));

        static IPlatformRegistry regWith(string dDrivePath, string rootPath, JsonHivePlatformRegistry targetReg, string value)
        {
            _ = dDrivePath; _ = rootPath;
            targetReg.SetValue(PlatformRegistryHive.CurrentUser, InstallerEngine.SharedStreamItAllRegistrySubKey, InstallerEngine.LastInstallBaseDirValueName, value);
            return targetReg;
        }
    }

    public void Dispose()
    {
        foreach (string j in _junctions)
            try { if (Directory.Exists(j)) Directory.Delete(j, recursive: false); } catch (Exception ex) { _ = ex; /* junction cleanup */ }
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch (Exception ex) { _ = ex; /* temp cleanup */ }
    }
}
