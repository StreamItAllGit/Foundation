// @module: Foundation.Installer.InstallerEngine.InstallAndExtract
// @purpose: Atomic payload extraction, protected user state preservation, pre-upgrade backup, and install manifest persistence.
// @depends-on: System, System.Collections.Generic, System.IO, System.IO.Compression, System.Linq, System.Text, System.Text.Json, System.Threading, System.Threading.Tasks, Foundation.Core
// @exports: InstallerEngine (partial: InstallAsync)

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;

namespace Foundation.Installer;

public sealed partial class InstallerEngine
{
    public Task<InstallerOperationResult> InstallAsync(
        string? explicitInstallDir = null,
        bool createShortcuts = true,
        bool registerAutoStart = false,
        string? externalPayloadPath = null,
        CancellationToken ct = default,
        IProgress<string>? progress = null) =>
        InstallAsync(
            new InstallerInstallOptions(
                ExplicitInstallDir: explicitInstallDir,
                CreateShortcuts: createShortcuts,
                RegisterAutoStart: registerAutoStart,
                ExternalPayloadPath: externalPayloadPath),
            ct,
            progress);

    public Task<InstallerOperationResult> InstallAsync(
        string? explicitInstallDir,
        bool createShortcuts,
        bool registerAutoStart,
        string? externalPayloadPath,
        string? installProfile,
        string? secondaryInstallDir = null,
        string? secondaryFlagName = null,
        bool migrateLegacy = false,
        CancellationToken ct = default,
        IProgress<string>? progress = null) =>
        InstallAsync(
            new InstallerInstallOptions(
                ExplicitInstallDir: explicitInstallDir,
                CreateShortcuts: createShortcuts,
                RegisterAutoStart: registerAutoStart,
                ExternalPayloadPath: externalPayloadPath,
                InstallProfile: installProfile,
                SecondaryInstallDir: secondaryInstallDir,
                SecondaryFlagName: secondaryFlagName,
                MigrateLegacy: migrateLegacy),
            ct,
            progress);

    public async Task<InstallerOperationResult> InstallAsync(
        InstallerInstallOptions options,
        CancellationToken ct = default,
        IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        string installDir = ValidateAndNormalizeInstallDirectory(options.ExplicitInstallDir);
        _ctx.EnsureDefaultLogDirectory(installDir);
        bool effectiveShortcuts = _ctx.ConfigOverride?.CreateShortcuts ?? options.CreateShortcuts;
        bool requestedAutoStart = _ctx.ConfigOverride?.AutoStart ?? options.RegisterAutoStart;
        bool effectiveAutoStart = Descriptor.AllowAutoStart && requestedAutoStart;
        string effectiveProfile = NormalizeOptionalProfile(options.InstallProfile)
            ?? NormalizeOptionalProfile(_ctx.ConfigOverride?.InstallProfile)
            ?? Descriptor.DefaultProfile;
        (string? reEngineDir, string? suiteDir) = ResolveSecondaryDirectories(
            options.SecondaryInstallDir,
            options.SecondaryFlagName);
        bool effectiveMigrateLegacy = options.MigrateLegacy || (_ctx.ConfigOverride?.MigrateLegacy ?? false);

        if (requestedAutoStart && !Descriptor.AllowAutoStart)
        {
            _ctx.WriteLog("WARN", $"AutoStart requested for {Descriptor.ProductName}, but AllowAutoStart is false; skipping Startup shortcut.");
        }

        progress?.Report("Verifying embedded payload SHA-256 & archive integrity...");
        var verify = await VerifyPayloadAsync(options.ExternalPayloadPath, ct);
        if (!verify.Valid)
        {
            return new InstallerOperationResult(false, installDir, string.Empty, 0, verify.Message);
        }

        if (_ctx.DryRun)
        {
            string dryMsg = $"[DRY-RUN] Verified payload ({verify.EntryCount:N0} files, SHA-256: {verify.Sha256}); would install {Descriptor.DisplayName} v{InstallerVersion} (profile: {effectiveProfile}) into '{installDir}'.";
            return new InstallerOperationResult(true, installDir, verify.Sha256, 0, dryMsg);
        }

        progress?.Report($"Checking for running {Descriptor.DisplayName} instances...");
        bool wasRunning = StopRunningInstanceInDirectory(installDir);
        Directory.CreateDirectory(installDir);

        string appSubManifestPath = Path.Combine(installDir, "app", InstallManifestFileName);
        string rootManifestPath = Path.Combine(installDir, InstallManifestFileName);
        string existingManifestPath = File.Exists(appSubManifestPath) ? appSubManifestPath : rootManifestPath;
        InstallManifest? previousManifest = null;
        if (File.Exists(existingManifestPath))
        {
            try
            {
                string prevRaw = await File.ReadAllTextAsync(existingManifestPath, Encoding.UTF8, ct);
                previousManifest = JsonSerializer.Deserialize<InstallManifest>(prevRaw, JsonOpts);
            }
            catch (Exception ex)
            {
                _ = ex; // Unreadable previous manifest falls back cleanly to directory artifact detection.
            }
        }

        bool isUpgrade = previousManifest != null || HasExistingInstallationArtifacts(installDir);

        if (isUpgrade)
        {
            progress?.Report("Creating pre-upgrade state backup in backups\\...");
        }
        string? backupZipPath = isUpgrade ? await CreatePreUpgradeBackupAsync(installDir, progress, ct) : null;

        var installedRelFiles = new List<string>();
        var installedTopDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using Stream payloadStream = OpenPayloadStream(options.ExternalPayloadPath)
            ?? throw new FileNotFoundException("Unable to open installer payload stream.");

        using (var zip = new ZipArchive(payloadStream, ZipArchiveMode.Read, leaveOpen: false))
        {
            long expandedBytes = 0;
            int totalEntries = zip.Entries.Count;
            int currentEntry = 0;
            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                currentEntry++;
                string normalizedRel = entry.FullName.Replace('\\', '/').TrimStart('/');
                if (string.IsNullOrWhiteSpace(normalizedRel))
                    continue;

                string topSegment = normalizedRel.Split('/', 2)[0];
                if (ProtectedRuntimeNames.Contains(topSegment))
                {
                    continue;
                }

                string destPath = EnsureWithinDirectory(installDir, Path.Combine(installDir, normalizedRel.Replace('/', Path.DirectorySeparatorChar)));

                if (normalizedRel.EndsWith("/", StringComparison.Ordinal) || string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destPath);
                    if (!string.Equals(topSegment, normalizedRel.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) || Directory.Exists(destPath))
                    {
                        installedTopDirs.Add(topSegment);
                    }
                    continue;
                }

                expandedBytes += entry.Length;
                if (expandedBytes > MaxArchiveExpandedBytes)
                {
                    throw new InvalidDataException("Payload archive exceeded 2 GB uncompressed safety cap during extraction.");
                }

                if (normalizedRel.Contains('/'))
                {
                    installedTopDirs.Add(topSegment);
                }

                string? parent = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                if (currentEntry == 1 || currentEntry % 15 == 0 || currentEntry == totalEntries)
                {
                    progress?.Report($"Extracting ({currentEntry}/{totalEntries}): {normalizedRel}");
                }

                string tempDest = destPath + ".tmp_" + Guid.NewGuid().ToString("N")[..8];
                try
                {
                    await using (Stream entryStream = entry.Open())
                    await using (var outFs = new FileStream(tempDest, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await entryStream.CopyToAsync(outFs, ct);
                    }
                    FileLockRetryHelper.MoveWithOverwrite(tempDest, destPath, onRetryLog: msg => _ctx.WriteLog("WARN", msg), ct: ct);
                    installedRelFiles.Add(normalizedRel);
                }
                finally
                {
                    if (File.Exists(tempDest))
                    {
                        try { File.Delete(tempDest); } catch { }
                    }
                }
            }
        }

        Directory.CreateDirectory(EnsureWithinDirectory(installDir, Path.Combine(installDir, "data")));
        Directory.CreateDirectory(EnsureWithinDirectory(installDir, Path.Combine(installDir, "logs")));
        Directory.CreateDirectory(EnsureWithinDirectory(installDir, Path.Combine(installDir, "backups")));

        bool hasAppSubfolder = installedTopDirs.Contains("app") || Directory.Exists(Path.Combine(installDir, "app"));
        if (previousManifest?.InstalledRelativeFiles is { Count: > 0 } prevFiles)
        {
            var currentFileSet = new HashSet<string>(installedRelFiles, StringComparer.OrdinalIgnoreCase);
            foreach (string oldRel in prevFiles)
            {
                if (string.IsNullOrWhiteSpace(oldRel) || currentFileSet.Contains(oldRel))
                    continue;

                string topSegment = oldRel.Replace('\\', '/').TrimStart('/').Split('/', 2)[0];
                if (ProtectedRuntimeNames.Contains(topSegment))
                    continue;

                try
                {
                    string obsoletePath = EnsureWithinDirectory(installDir, Path.Combine(installDir, oldRel.Replace('/', Path.DirectorySeparatorChar)));
                    FileLockRetryHelper.DeleteIfExists(obsoletePath, onRetryLog: msg => _ctx.WriteLog("WARN", msg), ct: ct);
                }
                catch (Exception ex)
                {
                    _ = ex; // Best-effort cleanup of obsolete files from previous release.
                }
            }
        }

        string metaContainerDir = hasAppSubfolder
            ? EnsureWithinDirectory(installDir, Path.Combine(installDir, "app"))
            : installDir;
        Directory.CreateDirectory(metaContainerDir);

        string? selfExe = Environment.ProcessPath;
        string uninstallerPath = Path.Combine(metaContainerDir, UninstallerExeName);
        if (!string.IsNullOrWhiteSpace(selfExe) &&
            File.Exists(selfExe) &&
            selfExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            !Path.GetFileName(selfExe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase) &&
            !Path.GetFileName(selfExe).Equals("testhost.exe", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Path.GetFullPath(selfExe), Path.GetFullPath(uninstallerPath), StringComparison.OrdinalIgnoreCase))
        {
            FileLockRetryHelper.CopyWithOverwrite(selfExe, uninstallerPath, onRetryLog: msg => _ctx.WriteLog("WARN", msg), ct: ct);
        }

        var createdShortcuts = new List<string>();
        if (effectiveShortcuts && OperatingSystem.IsWindows())
        {
            progress?.Report("Creating Windows Desktop & Start Menu shortcuts...");
            createdShortcuts.AddRange(CreateApplicationShortcuts(installDir, effectiveAutoStart));
        }

        string? registryRecord = RegisterUninstallerMetadata(installDir, uninstallerPath);

        var manifest = new InstallManifest
        {
            ProductName = Descriptor.ProductName,
            Version = InstallerVersion,
            InstalledAtUtc = DateTimeOffset.UtcNow,
            InstallDirectory = installDir,
            PayloadSha256 = verify.Sha256,
            InstalledRelativeFiles = installedRelFiles,
            InstalledTopDirectories = installedTopDirs.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
            CreatedShortcuts = createdShortcuts,
            UninstallRegistryRecord = registryRecord,
            InstallProfile = effectiveProfile,
            ReEngineInstallDir = reEngineDir,
            SuiteInstallDir = suiteDir,
            UmbrellaSubpath = Descriptor.UmbrellaSubpath,
            MigrateLegacy = effectiveMigrateLegacy
        };

        string targetManifestPath = Path.Combine(metaContainerDir, InstallManifestFileName);
        await File.WriteAllTextAsync(
            targetManifestPath,
            JsonSerializer.Serialize(manifest, JsonOpts),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            ct);

        string actionVerb = isUpgrade ? "Upgraded" : "Installed";
        string backupSuffix = !string.IsNullOrWhiteSpace(backupZipPath) ? $" (Pre-upgrade backup: '{Path.GetFileName(backupZipPath)}')" : "";
        string doneMsg = $"{actionVerb} {Descriptor.DisplayName} v{InstallerVersion} ({installedRelFiles.Count:N0} files) to '{installDir}'{backupSuffix}.";
        _ctx.WriteLog(
            "INFO",
            $"{doneMsg} SHA-256={verify.Sha256} Profile={effectiveProfile} ReEngineDir={reEngineDir ?? "none"} SuiteDir={suiteDir ?? "none"} MigrateLegacy={effectiveMigrateLegacy}");
        progress?.Report(doneMsg);
        return new InstallerOperationResult(true, installDir, verify.Sha256, installedRelFiles.Count, doneMsg, wasRunning, backupZipPath);
    }

    private static string? NormalizeOptionalProfile(string? rawProfile) =>
        string.IsNullOrWhiteSpace(rawProfile) ? null : rawProfile.Trim();

    private (string? ReEngineDir, string? SuiteDir) ResolveSecondaryDirectories(
        string? explicitSecondaryDir,
        string? explicitSecondaryFlagName)
    {
        bool guardLiveHost = _ctx.ShouldSuppressLiveHostMutations;
        SandboxResolution sandbox = _ctx.EffectiveSandbox;
        string? reEngineDir = NormalizeSecondaryPath(_ctx.ConfigOverride?.ReEngineInstallDir, guardLiveHost, sandbox);
        string? suiteDir = NormalizeSecondaryPath(_ctx.ConfigOverride?.SuiteInstallDir, guardLiveHost, sandbox);

        if (!string.IsNullOrWhiteSpace(explicitSecondaryDir))
        {
            string normalizedExplicit = NormalizeSecondaryPath(explicitSecondaryDir, guardLiveHost, sandbox)!;
            if (string.Equals(explicitSecondaryFlagName, "--reengine-dir", StringComparison.OrdinalIgnoreCase))
            {
                reEngineDir = normalizedExplicit;
            }
            else if (string.Equals(explicitSecondaryFlagName, "--suite-dir", StringComparison.OrdinalIgnoreCase))
            {
                suiteDir = normalizedExplicit;
            }
            else if (!string.IsNullOrWhiteSpace(reEngineDir) && string.IsNullOrWhiteSpace(suiteDir))
            {
                reEngineDir = normalizedExplicit;
            }
            else
            {
                suiteDir = normalizedExplicit;
            }
        }

        return (reEngineDir, suiteDir);
    }

    private static string? NormalizeSecondaryPath(string? rawPath, bool guardLiveHost, SandboxResolution sandbox)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return null;

        string fullPath = Path.GetFullPath(rawPath.Trim());
        if (guardLiveHost && FileLockRetryHelper.IsCanonicalProductionOrSystemPath(fullPath, sandbox))
        {
            SandboxEnvironmentSeam.ThrowContainmentViolation(
                $"Fail-closed isolation violation: refusing to target canonical production or system secondary directory '{fullPath}'.");
        }
        if (sandbox.IsSandboxActive)
        {
            fullPath = SandboxEnvironmentSeam.AssertSandboxPathIfActive(fullPath, sandbox);
        }
        return fullPath;
    }
}
