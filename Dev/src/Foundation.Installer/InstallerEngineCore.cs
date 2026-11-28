// @module: Foundation.Installer.Engine
// @purpose: Implements shared clean-root deployment (app\, scripts\), upgrade data/logs preservation, Mother manifest sync, and isolated runtime support.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.IO.Compression, System.Security.Cryptography, System.Text.Json, Foundation.Core
// @exports: FoundationInstallManifest, FoundationInstallResult, InstallerEngineCore

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;

namespace Foundation.Installer;

public sealed record FoundationInstallManifest
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; } = 1;
    [JsonPropertyName("appId")] public required string AppId { get; init; }
    [JsonPropertyName("displayName")] public required string DisplayName { get; init; }
    [JsonPropertyName("version")] public required string Version { get; init; }
    [JsonPropertyName("installedAtUtc")] public required string InstalledAtUtc { get; init; }
    [JsonPropertyName("installDirectory")] public required string InstallDirectory { get; init; }
    [JsonPropertyName("payloadSha256")] public required string PayloadSha256 { get; init; }
    [JsonPropertyName("installedRelativeFiles")] public List<string> InstalledRelativeFiles { get; init; } = new();
}

public sealed record FoundationInstallResult(
    bool Success,
    string InstallDirectory,
    string PayloadSha256,
    int FileCount,
    string Message,
    bool WasUpgrade
);

/// <summary>
/// Shared clean-root installation, upgrade, verification, and uninstallation engine (Standard 03 §5 &amp; Standard 12 §3.1).
/// Preserves user data\ and logs\ during upgrades and respects isolated runtime overrides (Standard 02).
/// </summary>
public sealed partial class InstallerEngineCore
{
    private const long MaxExpandedPayloadBytes = 2L * 1024 * 1024 * 1024;
    private static readonly HashSet<string> ProtectedTopDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "data", "logs", "backups", "drops", "saves_vault", "_Quarantine", "registry"
    };

    public InstallerThemeOptions Theme { get; }
    public AppEnvironment RuntimeEnvironment { get; }
    public SandboxResolution Sandbox => RuntimeEnvironment.Sandbox;
    public bool IsIsolatedTestProcess => IsTruthyEnv("APP_ISOLATED_TEST");
    public bool IsSandboxActive => Sandbox.IsSandboxActive && !IsIsolatedTestProcess;
    internal SandboxResolution EffectiveSandbox => IsSandboxActive ? Sandbox : SandboxResolution.Inactive;
    public bool IsStage1Isolated =>
        IsIsolatedTestProcess ||
        (!IsSandboxActive && !string.Equals(
            NormalizeDir(RuntimeEnvironment.DataDirectory),
            NormalizeDir(Path.Combine(RuntimeEnvironment.BaseDirectory, "data")),
            StringComparison.OrdinalIgnoreCase));
    public bool ShouldSuppressHostMutations => IsStage1Isolated;
    public bool ShouldSuppressLiveHostMutations => IsStage1Isolated || IsSandboxActive;
    public IPlatformRegistry Registry { get; }

    internal bool IsIsolatedOverride => ShouldSuppressLiveHostMutations;

    public InstallerEngineCore(InstallerThemeOptions theme, AppEnvironment runtimeEnvironment)
        : this(theme, runtimeEnvironment, registryOverride: null)
    {
    }

    public InstallerEngineCore(
        InstallerThemeOptions theme,
        AppEnvironment runtimeEnvironment,
        IPlatformRegistry? registryOverride)
    {
        Theme = theme ?? throw new ArgumentNullException(nameof(theme));
        RuntimeEnvironment = runtimeEnvironment ?? throw new ArgumentNullException(nameof(runtimeEnvironment));
        Registry = registryOverride ?? PlatformRegistryFactory.CreateDefault(EffectiveSandbox, ShouldSuppressHostMutations);
    }

    public string ResolveTargetInstallDir(string? explicitInstallDir = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitInstallDir))
        {
            string fullExplicit = Path.GetFullPath(explicitInstallDir);
            if (ShouldSuppressLiveHostMutations && FileLockRetryHelper.IsCanonicalProductionOrSystemPath(fullExplicit, EffectiveSandbox))
            {
                SandboxEnvironmentSeam.ThrowContainmentViolation(
                    $"Fail-closed isolation violation: refusing to target canonical production or system directory '{fullExplicit}'.");
            }
            if (IsSandboxActive)
            {
                fullExplicit = SandboxEnvironmentSeam.AssertSandboxPathIfActive(fullExplicit, Sandbox);
            }
            return fullExplicit;
        }

        if (IsStage1Isolated)
            return Path.Combine(RuntimeEnvironment.DataDirectory, "installed-app");

        string fallback = IsSandboxActive
            ? KnownFolderPaths.MapHostPathToSandbox(Theme.DefaultInstallDir, Sandbox)
            : Path.GetFullPath(Theme.DefaultInstallDir);

        if (!ShouldSuppressHostMutations)
        {
            try
            {
                string? regLoc = Registry.GetValue(PlatformRegistryHive.CurrentUser, $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{Theme.DisplayName}", "InstallLocation")?.AsString()
                    ?? Registry.GetValue(PlatformRegistryHive.CurrentUser, $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{Theme.AppId}", "InstallLocation")?.AsString();
                if (!string.IsNullOrWhiteSpace(regLoc))
                {
                    string candidate = Path.GetFullPath(regLoc.Trim());
                    if ((!IsSandboxActive || SandboxEnvironmentSeam.IsPathWithinSandbox(candidate, Sandbox)) && Directory.Exists(candidate))
                        return candidate;
                }

                if (!HasExistingInstallation(fallback) &&
                    InstallerEngine.TryResolveSuggestedInstallDirFromHistory(Registry, Theme.GetEffectiveUmbrellaSubpath(), EffectiveSandbox, out string suggested))
                {
                    return suggested;
                }
            }
            catch (Exception ex)
            {
                _ = ex; // Registry lookup falls back cleanly to default directory.
            }
        }

        return fallback;
    }

    public bool HasExistingInstallation(string? targetDir)
    {
        if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir)) return false;
        if (InstallerEngine.CheckForeignOwnershipForTheme(targetDir, Theme.AppId, Theme.DisplayName, out _))
            return false;
        string exeName = Theme.ExecutableFileName ?? $"{Theme.DisplayName}.exe";
        return File.Exists(Path.Combine(targetDir, exeName)) ||
               File.Exists(Path.Combine(targetDir, "app", exeName)) ||
               File.Exists(Path.Combine(targetDir, "app", "install-manifest.json")) ||
               File.Exists(Path.Combine(targetDir, "install-manifest.json"));
    }

    public bool IsTargetProcessRunning(string targetDir)
    {
        string exeStem = Path.GetFileNameWithoutExtension(Theme.ExecutableFileName ?? Theme.DisplayName);
        try
        {
            foreach (Process proc in Process.GetProcessesByName(exeStem))
            {
                using (proc)
                {
                    if (proc.Id == Environment.ProcessId) continue;
                    try
                    {
                        string? mod = proc.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(mod) &&
                            mod.StartsWith(Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch { /* ignore cross-session access */ }
                }
            }
        }
        catch { /* ignore enumeration error */ }
        return false;
    }

    public async Task<FoundationInstallResult> InstallOrUpgradeFromZipAsync(
        Stream payloadZipStream,
        string? targetInstallDir = null,
        Action<int, string>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payloadZipStream);
        string installDir = ResolveTargetInstallDir(targetInstallDir);
        if (InstallerEngine.CheckForeignOwnershipForTheme(installDir, Theme.AppId, Theme.DisplayName, out string? existingOwner))
        {
            return new FoundationInstallResult(false, installDir, string.Empty, 0,
                $"ProductMismatch: Directory '{installDir}' is owned by '{existingOwner}', not '{Theme.DisplayName}'. Refusing to overwrite.", false);
        }
        bool wasUpgrade = HasExistingInstallation(installDir);

        onProgress?.Invoke(5, $"Preparing {(wasUpgrade ? "upgrade" : "clean installation")} in '{installDir}'...");
        Directory.CreateDirectory(installDir);
        Directory.CreateDirectory(Path.Combine(installDir, "app"));
        Directory.CreateDirectory(Path.Combine(installDir, "data"));
        Directory.CreateDirectory(Path.Combine(installDir, "logs"));

        using var ms = new MemoryStream();
        await payloadZipStream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        byte[] zipBytes = ms.ToArray();
        string payloadSha256 = Convert.ToHexString(SHA256.HashData(zipBytes)).ToLowerInvariant();

        if (!RuntimeEnvironment.DryRun) CleanPreviousAppCodePreservingData(installDir);

        ms.Position = 0;
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        var installedFiles = new List<string>();
        long totalExpanded = 0;
        int totalEntries = Math.Max(1, archive.Entries.Count);
        int processed = 0;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processed++;
            string relPath = entry.FullName.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relPath)) continue;

            string destPath = AppEnvironment.AssertPathContained(Path.Combine(installDir, relPath), installDir);
            if (string.IsNullOrEmpty(entry.Name))
            {
                if (!RuntimeEnvironment.DryRun) Directory.CreateDirectory(destPath);
                continue;
            }

            string topSegment = relPath.Split(Path.DirectorySeparatorChar)[0];
            if (wasUpgrade && ProtectedTopDirectories.Contains(topSegment) && File.Exists(destPath)) continue;

            totalExpanded += entry.Length;
            if (totalExpanded > MaxExpandedPayloadBytes)
            {
                throw new InvalidDataException("Payload archive exceeds maximum 2 GB bounded extraction limit.");
            }

            if (!RuntimeEnvironment.DryRun)
            {
                string? parent = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                FileLockRetryHelper.ExecuteWithRetry(() => entry.ExtractToFile(destPath, overwrite: true), ct: cancellationToken);
            }

            installedFiles.Add(relPath.Replace('\\', '/'));
            onProgress?.Invoke(15 + (int)((double)processed / totalEntries * 75.0), $"Extracted {relPath}");
        }

        if (!RuntimeEnvironment.DryRun)
        {
            var manifest = new FoundationInstallManifest
            {
                SchemaVersion = 1,
                AppId = Theme.AppId,
                DisplayName = Theme.DisplayName,
                Version = Theme.Version,
                InstalledAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                InstallDirectory = installDir,
                PayloadSha256 = payloadSha256,
                InstalledRelativeFiles = installedFiles
            };
            File.WriteAllText(Path.Combine(installDir, "app", "install-manifest.json"), JsonSerializer.Serialize(manifest, FoundationInstallerJsonContext.Default.FoundationInstallManifest), new UTF8Encoding(false));
            RegisterSuiteManifestIfPresent(installDir);
            InstallerEngine.TrySaveLastInstallBaseDir(
                Registry,
                installDir,
                Theme.GetEffectiveUmbrellaSubpath(),
                EffectiveSandbox,
                ShouldSuppressHostMutations);
        }

        onProgress?.Invoke(100, $"Completed {(wasUpgrade ? "upgrade" : "installation")} ({installedFiles.Count} files).");
        return new FoundationInstallResult(true, installDir, payloadSha256, installedFiles.Count, "Installation succeeded.", wasUpgrade);
    }

    public bool VerifyInstallation(string? targetInstallDir, out string diagnosticMessage)
    {
        string installDir = ResolveTargetInstallDir(targetInstallDir);
        string manifestPath = Path.Combine(installDir, "app", "install-manifest.json");
        if (!File.Exists(manifestPath))
        {
            diagnosticMessage = $"Missing install-manifest.json at '{manifestPath}'.";
            return false;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize(File.ReadAllText(manifestPath, Encoding.UTF8), FoundationInstallerJsonContext.Default.FoundationInstallManifest);
            if (manifest is null || manifest.InstalledRelativeFiles.Count == 0)
            {
                diagnosticMessage = "Install manifest is empty or unreadable.";
                return false;
            }
            foreach (string rel in manifest.InstalledRelativeFiles)
            {
                if (!File.Exists(Path.Combine(installDir, rel.Replace('/', Path.DirectorySeparatorChar))))
                {
                    diagnosticMessage = $"Missing expected installed file: {rel}";
                    return false;
                }
            }
            diagnosticMessage = $"Verified {manifest.InstalledRelativeFiles.Count} installed files for {manifest.DisplayName} v{manifest.Version}.";
            return true;
        }
        catch (Exception ex)
        {
            diagnosticMessage = $"Verification failed: {ex.Message}";
            return false;
        }
    }

    public FoundationInstallResult Uninstall(string? targetInstallDir = null, bool purgeUserData = false)
    {
        string installDir = ResolveTargetInstallDir(targetInstallDir);
        if (!Directory.Exists(installDir))
            return new FoundationInstallResult(true, installDir, string.Empty, 0, "Directory already absent.", false);
        if (InstallerEngine.CheckForeignOwnershipForTheme(installDir, Theme.AppId, Theme.DisplayName, out string? existingOwner))
        {
            return new FoundationInstallResult(false, installDir, string.Empty, 0,
                $"ProductMismatch: Directory '{installDir}' is owned by '{existingOwner}', not '{Theme.DisplayName}'. Refusing to uninstall.", false);
        }

        if (purgeUserData) Directory.Delete(installDir, recursive: true);
        else CleanPreviousAppCodePreservingData(installDir);

        WindowsShortcutWriter.RemoveCurrentUserUninstallEntry(Theme.DisplayName, ShouldSuppressHostMutations, Registry);
        return new FoundationInstallResult(true, installDir, string.Empty, 1, "Uninstall completed.", false);
    }

    private static void CleanPreviousAppCodePreservingData(string installDir)
    {
        foreach (string subDir in new[] { "app", "scripts" })
        {
            string fullSub = Path.Combine(installDir, subDir);
            if (Directory.Exists(fullSub))
            {
                try { Directory.Delete(fullSub, recursive: true); } catch { /* ignore */ }
            }
        }
    }

    public string ResolveSuiteManifestDirectory(string installDir)
    {
        if (IsStage1Isolated)
        {
            return Path.Combine(RuntimeEnvironment.DataDirectory, "Mother", "manifests");
        }

        string? envDir = Environment.GetEnvironmentVariable("MOTHER_MANIFESTS_DIR")
            ?? Environment.GetEnvironmentVariable("SUITE_MANIFESTS_DIR");
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            string fullEnv = Path.GetFullPath(envDir.Trim());
            return IsSandboxActive ? SandboxEnvironmentSeam.AssertSandboxPathIfActive(fullEnv, Sandbox) : fullEnv;
        }

        string fullInstall = NormalizeDir(installDir);
        string? parent = Path.GetDirectoryName(fullInstall);
        if (!string.IsNullOrWhiteSpace(parent) &&
            string.Equals(Path.GetFileName(parent), "Mother", StringComparison.OrdinalIgnoreCase))
        {
            string candidate = Path.Combine(parent, "manifests");
            return IsSandboxActive ? SandboxEnvironmentSeam.AssertSandboxPathIfActive(candidate, Sandbox) : candidate;
        }

        string localAppData = KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, EffectiveSandbox);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.Combine(
                KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.UserProfile, EffectiveSandbox),
                "AppData",
                "Local");
        }

        string studio = string.IsNullOrWhiteSpace(Theme.StudioName) ? "StreamItAll" : Theme.StudioName.Trim();
        return Path.Combine(localAppData, "Programs", studio, "Mother", "manifests");
    }

    private static string NormalizeDir(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsTruthyEnv(string name)
    {
        string? v = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(v)) return false;
        v = v.Trim();
        return v.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private void RegisterSuiteManifestIfPresent(string installDir)
    {
        string sourceManifest = Path.Combine(installDir, "app", "suite.manifest.json");
        if (!File.Exists(sourceManifest)) return;

        string motherDir = ResolveSuiteManifestDirectory(installDir);

        try
        {
            Directory.CreateDirectory(motherDir);
            File.Copy(sourceManifest, Path.Combine(motherDir, $"{Theme.AppId}.manifest.json"), overwrite: true);
        }
        catch { /* non-fatal if target is unavailable */ }
    }
}
