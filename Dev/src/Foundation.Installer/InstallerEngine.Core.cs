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
    public const string SharedStreamItAllRegistrySubKey = @"Software\StreamItAll";
    public const string LastInstallBaseDirValueName = "LastInstallBaseDir";
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
    internal Assembly PayloadAssembly => _payloadAssembly;
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

                if (TryResolveSuggestedInstallDirFromHistory(
                    _ctx.Registry,
                    Descriptor.GetEffectiveUmbrellaSubpath(),
                    _ctx.EffectiveSandbox,
                    out string suggestedDir))
                {
                    return suggestedDir;
                }
            }
            catch (Exception ex)
            {
                _ = ex; // Registry access failure falls back cleanly to canonical install directory.
            }
        }

        return Path.GetFullPath(Descriptor.ResolveCanonicalDefaultInstallDir(_ctx.EffectiveSandbox));
    }

    public static bool TryResolveSuggestedInstallDirFromHistory(
        IPlatformRegistry registry,
        string umbrellaSubpath,
        SandboxResolution sandbox,
        out string suggestedInstallDir)
    {
        suggestedInstallDir = string.Empty;
        ArgumentNullException.ThrowIfNull(registry);
        if (string.IsNullOrWhiteSpace(umbrellaSubpath)) return false;

        string? rawBase = registry.GetValue(
            PlatformRegistryHive.CurrentUser,
            SharedStreamItAllRegistrySubKey,
            LastInstallBaseDirValueName)?.AsString();

        if (!TryValidateLastInstallBaseDir(rawBase, sandbox, out string validBase))
            return false;

        string cleanUmbrella = umbrellaSubpath.Trim().Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        suggestedInstallDir = Path.GetFullPath(Path.Combine(validBase, cleanUmbrella));
        return true;
    }

    public static bool TrySaveLastInstallBaseDir(
        IPlatformRegistry registry,
        string installDir,
        string umbrellaSubpath,
        SandboxResolution sandbox,
        bool suppressHostMutations)
    {
        if (suppressHostMutations || registry is NoOpPlatformRegistry || string.IsNullOrWhiteSpace(installDir))
            return false;

        string baseDir = ExtractInstallBaseDirectory(installDir, umbrellaSubpath);
        if (!TryValidateLastInstallBaseDir(baseDir, sandbox, out string validBase))
            return false;

        try
        {
            registry.SetValue(
                PlatformRegistryHive.CurrentUser,
                SharedStreamItAllRegistrySubKey,
                LastInstallBaseDirValueName,
                validBase,
                PlatformRegistryValueKind.String);
            return true;
        }
        catch (Exception ex)
        {
            _ = ex; // Best-effort LastInstallBaseDir persistence must not block completed setup.
            return false;
        }
    }

    internal static string ExtractInstallBaseDirectory(string installDir, string umbrellaSubpath)
    {
        string full = Path.GetFullPath(installDir.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string[] segments = (umbrellaSubpath ?? string.Empty)
            .Trim()
            .Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

        string current = full;
        for (int i = segments.Length - 1; i >= 0; i--)
        {
            if (!string.Equals(Path.GetFileName(current), segments[i], StringComparison.OrdinalIgnoreCase))
                break;

            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent))
                break;

            current = TrimNonRootDirectorySeparator(parent);
        }

        if (string.Equals(current, full, StringComparison.OrdinalIgnoreCase))
        {
            string? fallbackParent = Path.GetDirectoryName(full);
            return string.IsNullOrEmpty(fallbackParent) ? string.Empty : TrimNonRootDirectorySeparator(fallbackParent);
        }

        return current;
    }

    private static string TrimNonRootDirectorySeparator(string path)
    {
        string? root = Path.GetPathRoot(path);
        return string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
            ? path
            : path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static bool TryValidateLastInstallBaseDir(string? rawBase, SandboxResolution sandbox, out string validBase)
    {
        validBase = string.Empty;
        if (string.IsNullOrWhiteSpace(rawBase)) return false;

        string trimmed = rawBase.Trim();
        if (!Path.IsPathFullyQualified(trimmed)) return false;

        string full;
        try { full = Path.GetFullPath(trimmed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch (Exception ex) { _ = ex; return false; }

        string? root = Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(root) || string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            return false;

        if (sandbox.IsSandboxActive)
        {
            if (!SandboxEnvironmentSeam.IsPathWithinSandbox(full, sandbox)) return false;
            string sandboxTemp = Path.Combine(
                KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, sandbox),
                "Temp");
            if (IsSameOrChildPath(full, sandboxTemp)) return false;
        }
        else if (IsSameOrChildPath(full, Path.GetTempPath()))
        {
            return false;
        }

        if (HasReparsePointInExistingHierarchy(full, sandbox))
            return false;

        validBase = full;
        return true;
    }

    private static bool HasReparsePointInExistingHierarchy(string fullPath, SandboxResolution sandbox)
    {
        string? root = Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? stopBoundary = sandbox.IsSandboxActive && !string.IsNullOrWhiteSpace(sandbox.SandboxRoot)
            ? Path.GetFullPath(sandbox.SandboxRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : root;

        for (string? cur = fullPath; !string.IsNullOrEmpty(cur); cur = Path.GetDirectoryName(cur))
        {
            string norm = cur.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(norm, root, StringComparison.OrdinalIgnoreCase))
                break;

            if (Directory.Exists(norm))
            {
                try
                {
                    if ((File.GetAttributes(norm) & FileAttributes.ReparsePoint) != 0)
                        return true;
                }
                catch (Exception ex) { _ = ex; return true; }
            }

            if (stopBoundary is not null && string.Equals(norm, stopBoundary, StringComparison.OrdinalIgnoreCase))
                break;
        }
        return false;
    }

    private static bool IsSameOrChildPath(string candidate, string parent)
    {
        if (string.IsNullOrWhiteSpace(parent)) return false;
        string normParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(candidate, normParent, StringComparison.OrdinalIgnoreCase) ||
               candidate.StartsWith(normParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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
