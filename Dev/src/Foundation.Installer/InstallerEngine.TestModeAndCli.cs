// @module: Foundation.Installer.InstallerEngine.TestModeAndCli
// @purpose: HKCU Uninstall registry registration, pre-upgrade state backups, running process shutdown, and fail-closed path containment.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.Reflection, System.Text, System.Text.Json, System.Threading, System.Threading.Tasks, Microsoft.Win32
// @exports: InstallerEngine (partial: RegisterUninstallerMetadata, RemoveUninstallerMetadata, EnsureWithinDirectory, CreatePreUpgradeBackupAsync, OpenPayloadStream)

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

public sealed partial class InstallerEngine
{
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

    private static void ScheduleDeferredSelfDelete(string uninstallerPath, string installDir)
    {
        try
        {
            string appDir = Path.Combine(installDir, "app");
            var psi = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add($"ping 127.0.0.1 -n 2 > nul & del /f /q \"{uninstallerPath}\" & rmdir \"{appDir}\" 2>nul & rmdir \"{installDir}\" 2>nul");
            Process.Start(psi);
        }
        catch { }
    }
}
