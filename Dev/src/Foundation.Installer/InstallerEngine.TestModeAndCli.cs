// @module: Foundation.Installer.InstallerEngine.TestModeAndCli
// @purpose: CLI argument parsing, HKCU Uninstall registry registration, pre-upgrade state backups, running process shutdown, and bounded self-delete trampoline.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.Reflection, System.Text, System.Text.Json, System.Threading, System.Threading.Tasks, Microsoft.Win32
// @exports: InstallerCliOptions, InstallerEngine (partial: ParseCliArgs, RegisterUninstallerMetadata, RemoveUninstallerMetadata, EnsureWithinDirectory, CreatePreUpgradeBackupAsync, OpenPayloadStream, BuildSelfDeleteTrampolineCommand)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Foundation.Installer;

public sealed record InstallerCliOptions(
    string? InstallDir,
    string? DataDir,
    string? SandboxRoot,
    string? ConfigPath,
    string? LogDir,
    string? PayloadPath,
    string? ProfileId,
    string? ReEngineDir,
    string? SuiteDir,
    bool Uninstall,
    bool PurgeData,
    bool VerifyOnly,
    bool NoShortcuts,
    bool AutoStart,
    bool LaunchAfter,
    bool NoLaunch,
    bool Silent,
    bool TestMode,
    bool DryRun,
    bool Headless,
    bool VisibleTest,
    bool MigrateLegacy,
    bool ShowHelp,
    bool ShowVersion)
{
    public string? SecondaryDir => !string.IsNullOrWhiteSpace(ReEngineDir) ? ReEngineDir : SuiteDir;
    public string? SecondaryFlagName => !string.IsNullOrWhiteSpace(ReEngineDir) ? "--reengine-dir" : (!string.IsNullOrWhiteSpace(SuiteDir) ? "--suite-dir" : null);
}

public sealed partial class InstallerEngine
{
    public static InstallerCliOptions ParseCliArgs(string[] args, InstallerAppDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        args ??= Array.Empty<string>();

        string? installDir = null, dataDir = null, sandboxRoot = null, configPath = null, logDir = null, payloadPath = null;
        string? profileId = null, reEngineDir = null, suiteDir = null;
        bool uninstall = string.Equals(Path.GetFileName(Environment.ProcessPath), descriptor.UninstallerExeName, StringComparison.OrdinalIgnoreCase);
        bool purgeData = false, verifyOnly = false, noShortcuts = false, autoStart = false, launchAfter = false, noLaunch = false;
        bool silent = false, testMode = false, dryRun = false, headless = false, visibleTest = false, migrateLegacy = false;
        bool showHelp = false, showVersion = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i].Trim();
            switch (arg.ToLowerInvariant())
            {
                case "--install-dir":
                case "--target-dir": installDir = RequireNextCliValue(args, ref i, arg); break;
                case "--data-dir": dataDir = RequireNextCliValue(args, ref i, arg); break;
                case "--sandbox-root": sandboxRoot = RequireNextCliValue(args, ref i, arg); break;
                case "--config": configPath = RequireNextCliValue(args, ref i, arg); break;
                case "--log-dir": logDir = RequireNextCliValue(args, ref i, arg); break;
                case "--payload": payloadPath = RequireNextCliValue(args, ref i, arg); break;
                case "--profile": profileId = RequireNextCliValue(args, ref i, arg); break;
                case "--reengine-dir": reEngineDir = RequireNextCliValue(args, ref i, arg); break;
                case "--suite-dir": suiteDir = RequireNextCliValue(args, ref i, arg); break;
                case "--uninstall": uninstall = true; break;
                case "--purge-data": purgeData = true; break;
                case "--verify": verifyOnly = true; break;
                case "--no-shortcuts": noShortcuts = true; break;
                case "--autostart": autoStart = true; break;
                case "--launch": launchAfter = true; break;
                case "--no-launch":
                case "--no-restart": noLaunch = true; break;
                case "--silent":
                case "--quiet": silent = true; break;
                case "--test-mode": testMode = true; break;
                case "--dry-run": dryRun = true; break;
                case "--headless": headless = true; break;
                case "--visible-test": visibleTest = true; break;
                case "--migrate-legacy": migrateLegacy = true; break;
                case "--version":
                case "-v": showVersion = true; break;
                case "--help":
                case "-h":
                case "/?":
                case "help": showHelp = true; break;
                default: throw new ArgumentException($"Unknown option '{arg}'. Pass --help for usage.");
            }
        }

        return new InstallerCliOptions(
            installDir, dataDir, sandboxRoot, configPath, logDir, payloadPath,
            profileId, reEngineDir, suiteDir,
            uninstall, purgeData, verifyOnly, noShortcuts, autoStart, launchAfter, noLaunch,
            silent, testMode, dryRun, headless, visibleTest, migrateLegacy, showHelp, showVersion);
    }

    private static string RequireNextCliValue(string[] args, ref int index, string flag)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            throw new ArgumentException($"Option '{flag}' requires a value.");
        index++;
        return args[index].Trim();
    }
    private string? RegisterUninstallerMetadata(string installDir, string uninstallerPath)
    {
        string displayIcon = ResolveInstalledExecutablePath(installDir);
        string uninstallCmd = $"\"{uninstallerPath}\" --uninstall";
        string quietUninstallCmd = $"\"{uninstallerPath}\" --uninstall --silent";

        if (_ctx.IsContainedMode || !OperatingSystem.IsWindows())
        {
            string virtualRegDir = _ctx.DataDirectory ?? installDir;
            Directory.CreateDirectory(virtualRegDir);
            string virtualRegPath = Path.Combine(virtualRegDir, "uninstall-registry.json");
            var record = new Dictionary<string, object>
            {
                ["DisplayName"] = Descriptor.DisplayName,
                ["DisplayVersion"] = InstallerVersion,
                ["Publisher"] = Descriptor.Publisher,
                ["InstallLocation"] = installDir,
                ["DisplayIcon"] = displayIcon,
                ["UninstallString"] = uninstallCmd,
                ["QuietUninstallString"] = quietUninstallCmd,
                ["NoModify"] = 1,
                ["NoRepair"] = 1
            };
            File.WriteAllText(virtualRegPath, JsonSerializer.Serialize(record, JsonOpts), new UTF8Encoding(false));
            return virtualRegPath;
        }

        using RegistryKey? key = Registry.CurrentUser.CreateSubKey(Descriptor.WindowsUninstallRegistrySubKey, writable: true);
        if (key != null)
        {
            key.SetValue("DisplayName", Descriptor.DisplayName, RegistryValueKind.String);
            key.SetValue("DisplayVersion", InstallerVersion, RegistryValueKind.String);
            key.SetValue("Publisher", Descriptor.Publisher, RegistryValueKind.String);
            key.SetValue("InstallLocation", installDir, RegistryValueKind.String);
            key.SetValue("DisplayIcon", displayIcon, RegistryValueKind.String);
            key.SetValue("UninstallString", uninstallCmd, RegistryValueKind.String);
            key.SetValue("QuietUninstallString", quietUninstallCmd, RegistryValueKind.String);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        return $"HKCU\\{Descriptor.WindowsUninstallRegistrySubKey}";
    }

    private void RemoveUninstallerMetadata(string? recordedLocation)
    {
        if (!string.IsNullOrWhiteSpace(recordedLocation) &&
            !recordedLocation.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(recordedLocation))
        {
            try { File.Delete(recordedLocation); } catch { }
            return;
        }

        if (!_ctx.IsContainedMode && OperatingSystem.IsWindows())
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(Descriptor.WindowsUninstallRegistrySubKey, throwOnMissingSubKey: false); } catch { }
        }
    }

    public Task<string?> CreatePreUpgradeBackupAsync(
        string installDir,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (_ctx.DryRun)
            return Task.FromResult<string?>(null);

        _ctx.EnsureDefaultLogDirectory(installDir);
        string[] topFilesToBackup = ["settings.json", InstallManifestFileName];
        string[] subDirsToBackup = ["saves", "data", "registry", "saves_vault", "config_history"];

        return InstallerEngineCore.CreatePreUpgradeBackupAsync(
            installDir,
            InstallerVersion,
            topFilesToBackup,
            subDirsToBackup,
            progress,
            _ctx.WriteLog,
            _ctx.DryRun,
            ct);
    }

    private bool StopRunningInstanceInDirectory(string installDir)
    {
        if (_ctx.DryRun || !Directory.Exists(installDir))
            return false;

        return InstallerEngineCore.StopRunningInstancesInDirectory(
            installDir,
            new[] { Descriptor.MainProcessStem, Descriptor.ProductName });
    }

    public static string EnsureWithinDirectory(string boundaryDir, string candidatePath) =>
        InstallerEngineCore.EnsureWithinDirectory(boundaryDir, candidatePath);

    private static bool IsWithinDirectory(string boundaryDir, string candidatePath) =>
        InstallerEngineCore.IsWithinDirectory(boundaryDir, candidatePath);

    public Stream? OpenPayloadStream(string? externalPayloadPath)
    {
        if (!string.IsNullOrWhiteSpace(externalPayloadPath))
        {
            string full = Path.GetFullPath(externalPayloadPath);
            return File.Exists(full) ? File.OpenRead(full) : null;
        }

        Stream? direct = _payloadAssembly.GetManifestResourceStream(Descriptor.EffectiveEmbeddedPayloadResource);
        if (direct != null)
            return direct;

        foreach (string resName in _payloadAssembly.GetManifestResourceNames())
        {
            if (resName.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase))
            {
                return _payloadAssembly.GetManifestResourceStream(resName);
            }
        }

        return null;
    }

    public string ResolveInstalledExecutablePath(string installDir)
    {
        string rootLauncher = Path.Combine(installDir, Descriptor.MainExecutableFileName);
        if (File.Exists(rootLauncher))
            return rootLauncher;

        string relCandidate = Path.Combine(installDir, Descriptor.EffectiveMainExecutableRelativePath);
        if (File.Exists(relCandidate))
            return relCandidate;

        string appCandidate = Path.Combine(installDir, "app", Descriptor.MainExecutableFileName);
        if (File.Exists(appCandidate))
            return appCandidate;

        return rootLauncher;
    }

    public static string BuildSelfDeleteTrampolineCommand(string uninstallerPath, string installDir, int maxAttempts = 15)
    {
        int boundedAttempts = Math.Clamp(maxAttempts, 1, 60);
        string appDir = Path.Combine(installDir, "app");
        string fullUninstaller = Path.GetFullPath(uninstallerPath);
        string fullAppDir = Path.GetFullPath(appDir);
        string fullInstallDir = Path.GetFullPath(installDir);
        string wildUninstallerApp = Path.Combine(fullAppDir, "*-Uninstall.exe");
        string wildUninstallerRoot = Path.Combine(fullInstallDir, "*-Uninstall.exe");

        return $"for /L %i in (1,1,{boundedAttempts}) do @if exist \"{fullUninstaller}\" (ping 127.0.0.1 -n 2 >nul & del /f /q \"{fullUninstaller}\" 2>nul) & del /f /q \"{wildUninstallerApp}\" 2>nul & del /f /q \"{wildUninstallerRoot}\" 2>nul & rmdir \"{fullAppDir}\" 2>nul & rmdir \"{fullInstallDir}\" 2>nul";
    }

    private static void ScheduleDeferredSelfDelete(string uninstallerPath, string installDir)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(BuildSelfDeleteTrampolineCommand(uninstallerPath, installDir));
            Process.Start(psi);
        }
        catch { }
    }
}
