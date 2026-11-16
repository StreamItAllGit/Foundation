// @module: Foundation.Installer.InstallerEngine.Core
// @purpose: Core installer engine state, installation detection, IPlatformRegistry discovery, and fail-closed default/explicit install path validation.
// @depends-on: System, System.Collections.Generic, System.IO, System.Reflection, System.Text.Json, Foundation.Core
// @exports: InstallerEngine

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Foundation.Core;

namespace Foundation.Installer;

public sealed partial class InstallerEngine
{
    public const string InstallManifestFileName = "install-manifest.json";
    private const long MaxArchiveExpandedBytes = 2L * 1024 * 1024 * 1024;

    private static readonly HashSet<string> ProtectedRuntimeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "settings.json",
        "data",
        "backups",
        "saves",
        "logs"
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly InstallerRuntimeContext _ctx;
    private readonly Assembly _payloadAssembly;

    public InstallerAppDescriptor Descriptor { get; }
    public InstallerRuntimeContext Context => _ctx;
    public string InstallerVersion => Descriptor.Version;
    public string UninstallerExeName => Descriptor.UninstallerExeName;

    public InstallerEngine(
        InstallerAppDescriptor descriptor,
        InstallerRuntimeContext ctx,
        Assembly? payloadAssembly = null)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        _payloadAssembly = payloadAssembly ?? Assembly.GetEntryAssembly() ?? typeof(InstallerEngine).Assembly;
    }

    public bool HasExistingInstallationArtifacts(string? candidateDir) =>
        HasExistingInstallationArtifacts(candidateDir, Descriptor.MainExecutableFileName);

    public static bool HasExistingInstallationArtifacts(string? candidateDir, string exeFileName)
    {
        if (string.IsNullOrWhiteSpace(candidateDir))
            return false;

        try
        {
            string trimmed = candidateDir.Trim();
            if (!Directory.Exists(trimmed))
                return false;

            return File.Exists(Path.Combine(trimmed, exeFileName)) ||
                   File.Exists(Path.Combine(trimmed, "app", exeFileName)) ||
                   File.Exists(Path.Combine(trimmed, "settings.json")) ||
                   File.Exists(Path.Combine(trimmed, "data", "settings.json")) ||
                   File.Exists(Path.Combine(trimmed, InstallManifestFileName)) ||
                   File.Exists(Path.Combine(trimmed, "app", InstallManifestFileName));
        }
        catch (Exception ex)
        {
            _ = ex; // Unreadable candidate paths are treated as non-existent installations.
            return false;
        }
    }

    public string ResolveDefaultInstallDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_ctx.ConfigOverride?.InstallDirectory))
            return Path.GetFullPath(_ctx.ConfigOverride.InstallDirectory);

        if (_ctx.IsStage1Isolated && !string.IsNullOrWhiteSpace(_ctx.DataDirectory))
            return Path.GetFullPath(Path.Combine(_ctx.DataDirectory, "installed-app"));

        string? envInstallDir = Environment.GetEnvironmentVariable($"{Descriptor.EnvPrefix}_INSTALL_DIR");
        if (!string.IsNullOrWhiteSpace(envInstallDir))
            return Path.GetFullPath(envInstallDir.Trim());

        if (!_ctx.ShouldSuppressHostMutations)
        {
            try
            {
                string? regLoc = _ctx.Registry
                    .GetValue(PlatformRegistryHive.CurrentUser, Descriptor.WindowsUninstallRegistrySubKey, "InstallLocation")
                    ?.AsString();
                if (!string.IsNullOrWhiteSpace(regLoc))
                {
                    string candidate = Path.GetFullPath(regLoc.Trim());
                    if ((!_ctx.IsSandboxActive || SandboxEnvironmentSeam.IsPathWithinSandbox(candidate, _ctx.Sandbox)) &&
                        Directory.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch (Exception ex)
            {
                _ = ex; // Registry access failure falls back cleanly to canonical install directory.
            }
        }

        return Path.GetFullPath(Descriptor.ResolveCanonicalDefaultInstallDir(_ctx.EffectiveSandbox));
    }

    public string ValidateAndNormalizeInstallDirectory(string? explicitInstallDir)
    {
        string resolved = Path.GetFullPath(
            !string.IsNullOrWhiteSpace(explicitInstallDir)
                ? explicitInstallDir.Trim()
                : ResolveDefaultInstallDirectory());

        string? root = Path.GetPathRoot(resolved);
        if (string.IsNullOrWhiteSpace(root) ||
            string.Equals(
                resolved.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Refusing to install directly into a filesystem drive root.", nameof(explicitInstallDir));
        }

        if (_ctx.ShouldSuppressLiveHostMutations &&
            FileLockRetryHelper.IsCanonicalProductionOrSystemPath(resolved, _ctx.EffectiveSandbox))
        {
            SandboxEnvironmentSeam.ThrowContainmentViolation(
                $"Fail-closed isolation violation: refusing to target canonical production or system directory '{resolved}'.");
        }

        if (_ctx.IsSandboxActive)
        {
            resolved = SandboxEnvironmentSeam.AssertSandboxPathIfActive(resolved, _ctx.Sandbox);
        }

        _ctx.EnsureDefaultLogDirectory(resolved);
        return resolved;
    }
}
