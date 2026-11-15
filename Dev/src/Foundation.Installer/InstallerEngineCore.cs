// @module: Foundation.Installer.Engine
// @purpose: Implements shared clean-root deployment (app\, scripts\), upgrade data/logs preservation, Mother manifest sync, and --test-mode lifecycle.
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
/// Preserves user data\ and logs\ during upgrades and supports full headless --test-mode sandbox verification.
/// </summary>
public sealed partial class InstallerEngineCore
{
    private const long MaxExpandedPayloadBytes = 2L * 1024 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly HashSet<string> ProtectedTopDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "data", "logs", "backups", "drops", "saves_vault", "_Quarantine", "registry"
    };

    public InstallerThemeOptions Theme { get; }
    public SandboxEnvironment Sandbox { get; }

    public InstallerEngineCore(InstallerThemeOptions theme, SandboxEnvironment sandbox)
    {
        Theme = theme ?? throw new ArgumentNullException(nameof(theme));
        Sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
    }

    public string ResolveTargetInstallDir(string? explicitInstallDir = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitInstallDir))
        {
            string fullExplicit = Path.GetFullPath(explicitInstallDir);
            if (Sandbox.IsContainedSandbox) SandboxEnvironment.AssertPathContained(fullExplicit, Sandbox.DataDirectory);
            return fullExplicit;
        }
        return Sandbox.IsContainedSandbox
            ? Path.Combine(Sandbox.DataDirectory, "installed-app")
            : Path.GetFullPath(Theme.DefaultInstallDir);
    }

    public bool HasExistingInstallation(string? targetDir)
    {
        if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir)) return false;
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

        if (!Sandbox.DryRun) CleanPreviousAppCodePreservingData(installDir);

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

            string destPath = SandboxEnvironment.AssertPathContained(Path.Combine(installDir, relPath), installDir);
            if (string.IsNullOrEmpty(entry.Name))
            {
                if (!Sandbox.DryRun) Directory.CreateDirectory(destPath);
                continue;
            }

            string topSegment = relPath.Split(Path.DirectorySeparatorChar)[0];
            if (wasUpgrade && ProtectedTopDirectories.Contains(topSegment) && File.Exists(destPath)) continue;

            totalExpanded += entry.Length;
            if (totalExpanded > MaxExpandedPayloadBytes)
            {
                throw new InvalidDataException("Payload archive exceeds maximum 2 GB bounded extraction limit.");
            }

            if (!Sandbox.DryRun)
            {
                string? parent = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                FileLockRetryHelper.ExecuteWithRetry(() => entry.ExtractToFile(destPath, overwrite: true), ct: cancellationToken);
            }

            installedFiles.Add(relPath.Replace('\\', '/'));
            onProgress?.Invoke(15 + (int)((double)processed / totalEntries * 75.0), $"Extracted {relPath}");
        }

        if (!Sandbox.DryRun)
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
            File.WriteAllText(Path.Combine(installDir, "app", "install-manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
            RegisterSuiteManifestIfPresent(installDir);
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
            var manifest = JsonSerializer.Deserialize<FoundationInstallManifest>(File.ReadAllText(manifestPath, Encoding.UTF8), JsonOptions);
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

        if (purgeUserData) Directory.Delete(installDir, recursive: true);
        else CleanPreviousAppCodePreservingData(installDir);

        WindowsShortcutWriter.RemoveCurrentUserUninstallEntry(Theme.DisplayName, Sandbox.IsContainedSandbox);
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
        if (Sandbox.IsContainedSandbox)
        {
            return Path.Combine(Sandbox.DataDirectory, "Mother", "manifests");
        }

        string? envDir = Environment.GetEnvironmentVariable("MOTHER_MANIFESTS_DIR")
            ?? Environment.GetEnvironmentVariable("SUITE_MANIFESTS_DIR");
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            return Path.GetFullPath(envDir.Trim());
        }

        string fullInstall = Path.GetFullPath(installDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? parent = Path.GetDirectoryName(fullInstall);
        if (!string.IsNullOrWhiteSpace(parent) &&
            string.Equals(Path.GetFileName(parent), "Mother", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(parent, "manifests");
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData",
                "Local");
        }

        string studio = string.IsNullOrWhiteSpace(Theme.StudioName) ? "StreamItAll" : Theme.StudioName.Trim();
        return Path.Combine(localAppData, "Programs", studio, "Mother", "manifests");
    }

    public string? DetectLegacyFlatInstall(string? legacyBaseDir = null)
    {
        string searchRoot;
        if (!string.IsNullOrWhiteSpace(legacyBaseDir))
        {
            searchRoot = Path.GetFullPath(legacyBaseDir);
            if (Sandbox.IsContainedSandbox)
            {
                SandboxEnvironment.AssertPathContained(searchRoot, Sandbox.DataDirectory);
            }
        }
        else if (Sandbox.IsContainedSandbox)
        {
            searchRoot = Path.Combine(Sandbox.DataDirectory, "legacy-programs");
        }
        else
        {
            searchRoot = Path.Combine("D:" + Path.DirectorySeparatorChar, "Programs");
        }

        if (!Directory.Exists(searchRoot))
        {
            return null;
        }

        foreach (string folderName in new[] { Theme.DisplayName, Theme.AppId })
        {
            if (string.IsNullOrWhiteSpace(folderName)) continue;
            string candidate = Path.Combine(searchRoot, folderName.Trim());
            if (HasExistingInstallation(candidate) || File.Exists(Path.Combine(candidate, "settings.json")))
            {
                return candidate;
            }
        }

        return null;
    }

    public async Task<FoundationInstallResult> MigrateLegacyInstallAsync(
        string legacySourceDir,
        string targetInstallDir,
        bool optInConfirmed,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        if (!optInConfirmed)
        {
            throw new InvalidOperationException("Legacy install migration requires explicit opt-in (--migrate-legacy or wizard checkbox).");
        }

        if (string.IsNullOrWhiteSpace(legacySourceDir))
        {
            throw new ArgumentException("Legacy source directory is required.", nameof(legacySourceDir));
        }

        string fullSource = Path.GetFullPath(legacySourceDir);
        string fullTarget = ResolveTargetInstallDir(targetInstallDir);
        if (Sandbox.IsContainedSandbox)
        {
            SandboxEnvironment.AssertPathContained(fullSource, Sandbox.DataDirectory);
            SandboxEnvironment.AssertPathContained(fullTarget, Sandbox.DataDirectory);
        }

        if (!Directory.Exists(fullSource))
        {
            return new FoundationInstallResult(false, fullTarget, string.Empty, 0, $"Legacy directory not found: '{fullSource}'.", false);
        }

        if (string.Equals(fullSource.TrimEnd(Path.DirectorySeparatorChar), fullTarget.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return new FoundationInstallResult(true, fullTarget, string.Empty, 0, "Legacy source and target directory are identical.", false);
        }

        if (Sandbox.DryRun || dryRun)
        {
            return new FoundationInstallResult(true, fullTarget, string.Empty, 0, $"[DRY-RUN] Would migrate legacy install from '{fullSource}' to '{fullTarget}'.", false);
        }

        Directory.CreateDirectory(fullTarget);
        int migratedCount = 0;

        foreach (string entryName in ProtectedTopDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string srcSub = Path.Combine(fullSource, entryName);
            string dstSub = Path.Combine(fullTarget, entryName);
            if (Directory.Exists(srcSub))
            {
                migratedCount += await CopyDirectoryPreservingAsync(srcSub, dstSub, cancellationToken).ConfigureAwait(false);
            }
        }

        string srcSettings = Path.Combine(fullSource, "settings.json");
        string dstSettings = Path.Combine(fullTarget, "settings.json");
        if (File.Exists(srcSettings) && !File.Exists(dstSettings))
        {
            FileLockRetryHelper.CopyWithOverwrite(srcSettings, dstSettings, ct: cancellationToken);
            migratedCount++;
        }

        try
        {
            Directory.Delete(fullSource, recursive: true);
        }
        catch { /* non-fatal if locked handle remains in legacy dir */ }

        return new FoundationInstallResult(true, fullTarget, string.Empty, migratedCount, $"Migrated {migratedCount} legacy items from '{fullSource}' to '{fullTarget}'.", false);
    }

    private static async Task<int> CopyDirectoryPreservingAsync(string sourceDir, string targetDir, CancellationToken ct)
    {
        Directory.CreateDirectory(targetDir);
        int count = 0;
        foreach (string file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            string rel = Path.GetRelativePath(sourceDir, file);
            string dest = Path.Combine(targetDir, rel);
            string? parent = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            if (!File.Exists(dest))
            {
                await using var inFs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                await using var outFs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await inFs.CopyToAsync(outFs, ct).ConfigureAwait(false);
                count++;
            }
        }
        return count;
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
