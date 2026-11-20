// @module: Foundation.Installer.InstallerEngine.UninstallAndShortcuts
// @purpose: Clean uninstallation and Windows Desktop/Start Menu/Startup .lnk shortcut generation via WindowsShortcutWriter and KnownFolderPaths.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Text, System.Text.Json, System.Threading, System.Threading.Tasks, Foundation.Core
// @exports: InstallerEngine (partial: UninstallAsync, CreateApplicationShortcuts)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;

namespace Foundation.Installer;

public sealed partial class InstallerEngine
{
    public async Task<InstallerOperationResult> UninstallAsync(
        string? explicitInstallDir = null,
        bool purgeData = false,
        CancellationToken ct = default,
        IProgress<string>? progress = null)
    {
        string defaultBase;
        if (!string.IsNullOrWhiteSpace(explicitInstallDir))
        {
            defaultBase = explicitInstallDir;
        }
        else
        {
            string baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (File.Exists(Path.Combine(baseDir, InstallManifestFileName)))
            {
                string? parent = Path.GetDirectoryName(baseDir);
                defaultBase = (string.Equals(Path.GetFileName(baseDir), "app", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(parent))
                    ? parent
                    : baseDir;
            }
            else
            {
                defaultBase = ResolveDefaultInstallDirectory();
            }
        }

        string installDir = ValidateAndNormalizeInstallDirectory(defaultBase);

        if (!Directory.Exists(installDir))
        {
            return new InstallerOperationResult(false, installDir, string.Empty, 0, $"Install directory does not exist: '{installDir}'.");
        }

        InstallOwnershipCheckResult ownership = CheckDirectoryOwnership(installDir);
        if (ownership.Status == InstallDirectoryOwnership.OwnedByForeignProduct)
        {
            string mismatchMsg = BuildForeignOwnershipErrorMessage(installDir, ownership.ExistingProductName!, isUninstall: true);
            _ctx.WriteLog("ERROR", mismatchMsg);
            return new InstallerOperationResult(false, installDir, string.Empty, 0, mismatchMsg);
        }

        if (_ctx.DryRun)
        {
            return new InstallerOperationResult(
                true,
                installDir,
                string.Empty,
                0,
                $"[DRY-RUN] Would uninstall {Descriptor.DisplayName} from '{installDir}' (PurgeData={purgeData}).");
        }

        progress?.Report($"Stopping running {Descriptor.DisplayName} instances...");
        StopRunningInstanceInDirectory(installDir);

        string appManifestPath = Path.Combine(installDir, "app", InstallManifestFileName);
        string rootManifestPath = Path.Combine(installDir, InstallManifestFileName);
        InstallManifest? manifest = ownership.Manifest ?? TryReadInstallManifest(installDir, out _);

        int removedFiles = 0;

        progress?.Report("Removing installed application binaries and scripts...");
        if (manifest?.InstalledRelativeFiles is { Count: > 0 } relFiles &&
            (string.IsNullOrWhiteSpace(manifest.ProductName) || IsOwnInstallManifest(manifest, Descriptor.ProductName)))
        {
            foreach (string rel in relFiles)
            {
                string safeTarget = EnsureWithinDirectory(installDir, Path.Combine(installDir, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (File.Exists(safeTarget))
                {
                    File.Delete(safeTarget);
                    removedFiles++;
                }
            }
        }

        if (File.Exists(appManifestPath)) { File.Delete(appManifestPath); removedFiles++; }
        if (File.Exists(rootManifestPath)) { File.Delete(rootManifestPath); removedFiles++; }

        string? selfExe = Environment.ProcessPath;
        var uninstallerCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(installDir, "app", UninstallerExeName),
            Path.Combine(installDir, UninstallerExeName)
        };

        foreach (string uninstallerPath in uninstallerCandidates)
        {
            if (!File.Exists(uninstallerPath))
                continue;

            bool isRunningSelf = !string.IsNullOrWhiteSpace(selfExe) &&
                                 string.Equals(Path.GetFullPath(selfExe), Path.GetFullPath(uninstallerPath), StringComparison.OrdinalIgnoreCase);

            if (!isRunningSelf)
            {
                File.Delete(uninstallerPath);
                removedFiles++;
            }
            else if (!_ctx.ShouldSuppressHostMutations && OperatingSystem.IsWindows())
            {
                if (_ctx.IsSandboxActive)
                {
                    SandboxEnvironmentSeam.AssertSandboxPathIfActive(uninstallerPath, _ctx.Sandbox);
                    SandboxEnvironmentSeam.AssertSandboxPathIfActive(installDir, _ctx.Sandbox);
                }
                ScheduleDeferredSelfDelete(uninstallerPath, installDir);
            }
        }

        if (manifest?.InstalledTopDirectories is { Count: > 0 } topDirs)
        {
            foreach (string dirName in topDirs)
            {
                if (ProtectedRuntimeNames.Contains(dirName))
                    continue;
                string safeDir = EnsureWithinDirectory(installDir, Path.Combine(installDir, dirName));
                if (Directory.Exists(safeDir))
                {
                    try { Directory.Delete(safeDir, recursive: true); } catch { }
                }
            }
        }

        if (purgeData)
        {
            progress?.Report("Purging user saves, settings, logs, and backups...");
            foreach (string item in ProtectedRuntimeNames)
            {
                string target = EnsureWithinDirectory(installDir, Path.Combine(installDir, item));
                if (File.Exists(target))
                {
                    File.Delete(target);
                    removedFiles++;
                }
                else if (Directory.Exists(target))
                {
                    removedFiles += Directory.GetFiles(target, "*", SearchOption.AllDirectories).Length;
                    Directory.Delete(target, recursive: true);
                }
            }
        }

        removedFiles += RemoveApplicationShortcuts(installDir, manifest);

        RemoveUninstallerMetadata(manifest?.UninstallRegistryRecord);

        if (Directory.Exists(installDir) && !Directory.EnumerateFileSystemEntries(installDir).Any())
        {
            try { Directory.Delete(installDir, recursive: false); } catch { }
        }

        string msg = $"Uninstalled {Descriptor.DisplayName} from '{installDir}' ({removedFiles:N0} files removed, PurgeData={purgeData}).";
        _ctx.WriteLog("INFO", msg);
        progress?.Report(msg);
        return new InstallerOperationResult(true, installDir, string.Empty, removedFiles, msg);
    }

    private int RemoveApplicationShortcuts(string installDir, InstallManifest? manifest)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (manifest?.CreatedShortcuts != null)
        {
            foreach (string lnk in manifest.CreatedShortcuts)
            {
                if (!string.IsNullOrWhiteSpace(lnk))
                    candidates.Add(lnk.Trim());
            }
        }

        (string desktopDir, string startMenuDir, string startupDir) = ResolveShortcutDirectories(installDir, createIfMissing: false);
        string sanitizedName = $"{WindowsShortcutWriter.SanitizeShortcutFileName(Descriptor.DisplayName)}.lnk";
        string? rawName = !string.IsNullOrWhiteSpace(Descriptor.DisplayName) &&
                          Descriptor.DisplayName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
                          !Descriptor.DisplayName.Contains(':')
            ? $"{Descriptor.DisplayName}.lnk"
            : null;

        foreach (string dir in new[] { desktopDir, startMenuDir, startupDir })
        {
            candidates.Add(Path.Combine(dir, sanitizedName));
            if (!string.IsNullOrEmpty(rawName) && !string.Equals(rawName, sanitizedName, StringComparison.OrdinalIgnoreCase))
                candidates.Add(Path.Combine(dir, rawName));
        }

        int removed = 0;
        foreach (string lnk in candidates)
        {
            if (_ctx.IsSandboxActive)
            {
                SandboxEnvironmentSeam.AssertSandboxPathIfActive(lnk, _ctx.Sandbox);
            }
            else if (_ctx.ShouldSuppressHostMutations)
            {
                bool allowedInStage1 = (!string.IsNullOrWhiteSpace(_ctx.DataDirectory) && IsWithinDirectory(_ctx.DataDirectory, lnk)) ||
                                       IsWithinDirectory(installDir, lnk);
                if (!allowedInStage1) continue;
            }

            if (!File.Exists(lnk)) continue;

            try
            {
                File.Delete(lnk);
                removed++;
                string? lnkDir = Path.GetDirectoryName(lnk);
                if (!string.IsNullOrEmpty(lnkDir) &&
                    Directory.Exists(lnkDir) &&
                    string.Equals(Path.GetFileName(lnkDir), Descriptor.ProductName, StringComparison.OrdinalIgnoreCase) &&
                    !Directory.EnumerateFileSystemEntries(lnkDir).Any())
                {
                    if (_ctx.IsSandboxActive)
                        SandboxEnvironmentSeam.AssertSandboxPathIfActive(lnkDir, _ctx.Sandbox);
                    Directory.Delete(lnkDir, recursive: false);
                }
            }
            catch { }
        }

        return removed;
    }

    private (string DesktopDir, string StartMenuDir, string StartupDir) ResolveShortcutDirectories(string installDir, bool createIfMissing)
    {
        string desktopDir;
        string startMenuDir;
        string startupDir;

        if (_ctx.ShouldSuppressHostMutations)
        {
            string root = _ctx.DataDirectory ?? installDir;
            desktopDir = Path.Combine(root, "shortcuts", "desktop");
            startMenuDir = Path.Combine(root, "shortcuts", "startmenu", Descriptor.ProductName);
            startupDir = Path.Combine(root, "shortcuts", "startup");
        }
        else
        {
            SandboxResolution sandbox = _ctx.EffectiveSandbox;
            desktopDir = KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.DesktopDirectory, sandbox);
            startMenuDir = Path.Combine(KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.Programs, sandbox), Descriptor.ProductName);
            startupDir = KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.Startup, sandbox);
            if (_ctx.IsSandboxActive)
            {
                desktopDir = SandboxEnvironmentSeam.AssertSandboxPathIfActive(desktopDir, sandbox);
                startMenuDir = SandboxEnvironmentSeam.AssertSandboxPathIfActive(startMenuDir, sandbox);
                startupDir = SandboxEnvironmentSeam.AssertSandboxPathIfActive(startupDir, sandbox);
            }
        }

        if (createIfMissing)
        {
            Directory.CreateDirectory(desktopDir);
            Directory.CreateDirectory(startMenuDir);
        }

        return (desktopDir, startMenuDir, startupDir);
    }

    private List<string> CreateApplicationShortcuts(string installDir, bool includeStartupShortcut)
    {
        var created = new List<string>();
        string mainExe = ResolveInstalledExecutablePath(installDir);
        if (!File.Exists(mainExe))
        {
            return created;
        }

        (string desktopDir, string startMenuDir, string startupDir) = ResolveShortcutDirectories(installDir, createIfMissing: true);

        string lnkFileName = $"{WindowsShortcutWriter.SanitizeShortcutFileName(Descriptor.DisplayName)}.lnk";
        string desktopLnk = Path.Combine(desktopDir, lnkFileName);
        WindowsShortcutWriter.WriteShortcut(
            outputLnkPath: desktopLnk,
            targetExecutablePath: mainExe,
            arguments: string.Empty,
            workingDirectory: installDir,
            description: $"Launch {Descriptor.DisplayName}",
            iconFilePath: mainExe);
        created.Add(desktopLnk);

        string startLnk = Path.Combine(startMenuDir, lnkFileName);
        WindowsShortcutWriter.WriteShortcut(
            outputLnkPath: startLnk,
            targetExecutablePath: mainExe,
            arguments: string.Empty,
            workingDirectory: installDir,
            description: $"Launch {Descriptor.DisplayName}",
            iconFilePath: mainExe);
        created.Add(startLnk);

        if (includeStartupShortcut)
        {
            Directory.CreateDirectory(startupDir);
            string autoStartLnk = Path.Combine(startupDir, lnkFileName);
            WindowsShortcutWriter.WriteShortcut(
                outputLnkPath: autoStartLnk,
                targetExecutablePath: mainExe,
                arguments: string.Empty,
                workingDirectory: installDir,
                description: $"Launch {Descriptor.DisplayName} at Sign-In",
                iconFilePath: mainExe);
            created.Add(autoStartLnk);
        }

        return created;
    }
}
