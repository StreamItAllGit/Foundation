// @module: Foundation.Installer.InstallerEngine.InstallAndExtract
// @purpose: Atomic payload extraction, protected user state preservation, pre-upgrade backup, and install manifest persistence.
// @depends-on: System, System.Collections.Generic, System.IO, System.IO.Compression, System.Linq, System.Text, System.Text.Json, System.Threading, System.Threading.Tasks, Foundation.Core
// @exports: InstallerEngine (partial: InstallAsync)

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
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

        InstallOwnershipCheckResult ownership = CheckDirectoryOwnership(installDir);
        if (ownership.Status == InstallDirectoryOwnership.OwnedByForeignProduct)
        {
            string mismatchMsg = BuildForeignOwnershipErrorMessage(installDir, ownership.ExistingProductName!, isUninstall: false);
            _ctx.WriteLog("ERROR", mismatchMsg);
            return new InstallerOperationResult(false, installDir, string.Empty, 0, mismatchMsg);
        }

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

        progress?.Report(Descriptor.IsOnlineSetup && string.IsNullOrWhiteSpace(options.OfflineBundlePath) && string.IsNullOrWhiteSpace(options.ExternalPayloadPath)
            ? "Resolving & downloading verified Online Setup payload..."
            : "Verifying payload SHA-256 & archive integrity...");
        await using ResolvedPayloadBatch batch = await ResolvePayloadBatchAsync(
            options.ExternalPayloadPath,
            options.OfflineBundlePath,
            effectiveProfile,
            progress,
            ct).ConfigureAwait(false);
        if (!batch.Valid)
        {
            return new InstallerOperationResult(false, installDir, string.Empty, 0, batch.Message);
        }

        if (_ctx.DryRun)
        {
            string dryMsg = $"[DRY-RUN] Verified payload ({batch.TotalEntries:N0} files, SHA-256: {batch.CombinedSha256}); would install {Descriptor.DisplayName} v{InstallerVersion} (profile: {effectiveProfile}) into '{installDir}'.";
            return new InstallerOperationResult(true, installDir, batch.CombinedSha256, 0, dryMsg);
        }

        progress?.Report($"Checking for running {Descriptor.DisplayName} instances...");
        bool wasRunning = StopRunningInstanceInDirectory(installDir);
        Directory.CreateDirectory(installDir);

        InstallManifest? previousManifest = ownership.Status == InstallDirectoryOwnership.OwnedBySelf
            ? ownership.Manifest
            : null;
        bool isUpgrade = ownership.Status == InstallDirectoryOwnership.OwnedBySelf ||
                         (ownership.Manifest == null && HasExistingInstallationArtifacts(installDir));

        if (isUpgrade)
        {
            progress?.Report("Creating pre-upgrade state backup in backups\\...");
        }
        string? backupZipPath = isUpgrade ? await CreatePreUpgradeBackupAsync(installDir, progress, ct).ConfigureAwait(false) : null;

        var installedRelFiles = new List<string>();
        var installedTopDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await ExtractPayloadBatchAsync(
            batch, options.ExternalPayloadPath, installDir, installedRelFiles, installedTopDirs, progress, ct).ConfigureAwait(false);

        Directory.CreateDirectory(EnsureWithinDirectory(installDir, Path.Combine(installDir, "data")));
        Directory.CreateDirectory(EnsureWithinDirectory(installDir, Path.Combine(installDir, "logs")));
        Directory.CreateDirectory(EnsureWithinDirectory(installDir, Path.Combine(installDir, "backups")));

        bool hasAppSubfolder = installedTopDirs.Contains("app") || Directory.Exists(Path.Combine(installDir, "app"));
        if (IsOwnInstallManifest(previousManifest, Descriptor.ProductName) &&
            previousManifest?.InstalledRelativeFiles is { Count: > 0 } prevFiles)
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
        if (!TryExtractEmbeddedUninstallerResource(uninstallerPath, ct) &&
            !string.IsNullOrWhiteSpace(selfExe) &&
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
        TrySaveLastInstallBaseDir(
            _ctx.Registry,
            installDir,
            Descriptor.GetEffectiveUmbrellaSubpath(),
            _ctx.EffectiveSandbox,
            _ctx.ShouldSuppressHostMutations);

        var manifest = new InstallManifest
        {
            ProductName = Descriptor.ProductName,
            Version = InstallerVersion,
            InstalledAtUtc = DateTimeOffset.UtcNow,
            InstallDirectory = installDir,
            PayloadSha256 = batch.CombinedSha256,
            InstalledRelativeFiles = installedRelFiles,
            InstalledTopDirectories = installedTopDirs.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
            CreatedShortcuts = createdShortcuts,
            UninstallRegistryRecord = registryRecord,
            InstallProfile = effectiveProfile,
            ReEngineInstallDir = reEngineDir,
            SuiteInstallDir = suiteDir,
            UmbrellaSubpath = Descriptor.UmbrellaSubpath,
            MigrateLegacy = effectiveMigrateLegacy,
            InstallerDeliveryKind = Descriptor.InstallerDeliveryKind
        };

        string targetManifestPath = Path.Combine(metaContainerDir, InstallManifestFileName);
        await File.WriteAllTextAsync(
            targetManifestPath,
            JsonSerializer.Serialize(manifest, FoundationInstallerJsonContext.Default.InstallManifest),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            ct).ConfigureAwait(false);

        string actionVerb = isUpgrade ? "Upgraded" : "Installed";
        string backupSuffix = !string.IsNullOrWhiteSpace(backupZipPath) ? $" (Pre-upgrade backup: '{Path.GetFileName(backupZipPath)}')" : "";
        string doneMsg = $"{actionVerb} {Descriptor.DisplayName} v{InstallerVersion} ({installedRelFiles.Count:N0} files) to '{installDir}'{backupSuffix}.";
        _ctx.WriteLog(
            "INFO",
            $"{doneMsg} SHA-256={batch.CombinedSha256} Profile={effectiveProfile} Delivery={Descriptor.InstallerDeliveryKind} ReEngineDir={reEngineDir ?? "none"} SuiteDir={suiteDir ?? "none"} MigrateLegacy={effectiveMigrateLegacy}");
        progress?.Report(doneMsg);
        return new InstallerOperationResult(true, installDir, batch.CombinedSha256, installedRelFiles.Count, doneMsg, wasRunning, backupZipPath);
    }

    internal bool TryExtractEmbeddedUninstallerResource(string destinationPath, CancellationToken ct)
    {
        (Assembly? asm, string? resName, bool isGzip) = FindEmbeddedUninstallerResource();
        if (asm is null || string.IsNullOrWhiteSpace(resName))
        {
            return false;
        }

        string tempDest = destinationPath + ".tmp_" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            using Stream? rawStream = asm.GetManifestResourceStream(resName);
            if (rawStream is null)
            {
                return false;
            }

            string? parentDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            using (var outFs = new FileStream(tempDest, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (isGzip)
                {
                    using var gz = new GZipStream(rawStream, CompressionMode.Decompress);
                    gz.CopyTo(outFs);
                }
                else
                {
                    rawStream.CopyTo(outFs);
                }
            }

            FileLockRetryHelper.MoveWithOverwrite(tempDest, destinationPath, onRetryLog: msg => _ctx.WriteLog("WARN", msg), ct: ct);
            _ctx.WriteLog("INFO", $"Extracted embedded slim uninstaller resource '{resName}' -> '{destinationPath}'.");
            return true;
        }
        finally
        {
            if (File.Exists(tempDest))
            {
                try { File.Delete(tempDest); } catch { }
            }
        }
    }

    private (Assembly? Assembly, string? ResourceName, bool IsGzip) FindEmbeddedUninstallerResource()
    {
        Assembly? entryAsm = Assembly.GetEntryAssembly();
        Assembly?[] candidates = entryAsm is not null && !ReferenceEquals(entryAsm, _payloadAssembly)
            ? new[] { _payloadAssembly, entryAsm }
            : new[] { _payloadAssembly };

        foreach (Assembly? asm in candidates)
        {
            if (asm is null) continue;
            string[] names = asm.GetManifestResourceNames();
            string? gzName = names.FirstOrDefault(n => n.EndsWith(".uninstaller.exe.gz", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(gzName))
                return (asm, gzName, true);

            string? exeName = names.FirstOrDefault(n => n.EndsWith(".uninstaller.exe", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(exeName))
                return (asm, exeName, false);
        }

        return (null, null, false);
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
