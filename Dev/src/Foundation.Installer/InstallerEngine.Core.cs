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

        string sanitized = SanitizeLastInstallBaseDir(rawBase, umbrellaSubpath);
        if (!TryValidateLastInstallBaseDir(sanitized, sandbox, out string validBase) ||
            IsPoisonedInstallBaseDirectory(validBase, umbrellaSubpath))
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
        if (!TryValidateLastInstallBaseDir(baseDir, sandbox, out string validBase) ||
            IsPoisonedInstallBaseDirectory(validBase, umbrellaSubpath))
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

    internal static string SanitizeLastInstallBaseDir(string? rawBase, string? umbrellaSubpath)
    {
        if (string.IsNullOrWhiteSpace(rawBase)) return string.Empty;
        string trimmed = rawBase.Trim();
        if (!Path.IsPathFullyQualified(trimmed)) return trimmed;
        string full;
        try { full = TrimNonRootDirectorySeparator(Path.GetFullPath(trimmed)); }
        catch (Exception ex) { _ = ex; return trimmed; }

        (string root, string[] pathSegs) = SplitRootAndSegments(full);
        int cutIdx = FindPoisonBoundaryIndex(pathSegs, SplitUmbrellaSegments(umbrellaSubpath), includeEngines: false);
        return cutIdx >= 0 ? CombineRootAndSegments(root, pathSegs.AsSpan(0, cutIdx)) : full;
    }

    internal static bool IsPoisonedInstallBaseDirectory(string candidateBase, string? umbrellaSubpath)
    {
        if (string.IsNullOrWhiteSpace(candidateBase)) return true;
        (_, string[] segs) = SplitRootAndSegments(candidateBase);
        return segs.Length == 0 || FindPoisonBoundaryIndex(segs, SplitUmbrellaSegments(umbrellaSubpath), includeEngines: true) >= 0;
    }

    public static string ExtractInstallBaseDirectory(string installDir, string umbrellaSubpath)
    {
        string full = TrimNonRootDirectorySeparator(Path.GetFullPath(installDir.Trim()));
        (string root, string[] pathSegs) = SplitRootAndSegments(full);
        string[] umbSegs = SplitUmbrellaSegments(umbrellaSubpath);

        int anchorIdx = FindPoisonBoundaryIndex(pathSegs, umbSegs, includeEngines: false);
        if (anchorIdx >= 0)
        {
            string anchored = CombineRootAndSegments(root, pathSegs.AsSpan(0, anchorIdx));
            return IsPoisonedInstallBaseDirectory(anchored, umbrellaSubpath) ? string.Empty : anchored;
        }

        string current = full;
        if (umbSegs.Length > 0 && !InstallerAppDescriptor.IsVersionLeafSegment(umbSegs[^1]) &&
            InstallerAppDescriptor.IsVersionLeafSegment(Path.GetFileName(current)))
        {
            if (Path.GetDirectoryName(current) is { Length: > 0 } noVer) current = TrimNonRootDirectorySeparator(noVer);
        }
        for (int i = umbSegs.Length - 1; i >= 0; i--)
        {
            if (!string.Equals(Path.GetFileName(current), umbSegs[i], StringComparison.OrdinalIgnoreCase)) break;
            if (Path.GetDirectoryName(current) is not { Length: > 0 } parent) break;
            current = TrimNonRootDirectorySeparator(parent);
        }

        string candidate = string.Equals(current, full, StringComparison.OrdinalIgnoreCase)
            ? (Path.GetDirectoryName(full) is { Length: > 0 } fb ? TrimNonRootDirectorySeparator(fb) : string.Empty)
            : current;
        return IsPoisonedInstallBaseDirectory(candidate, umbrellaSubpath) ? string.Empty : candidate;
    }

    private static int FindPoisonBoundaryIndex(string[] pathSegs, string[] umbSegs, bool includeEngines)
    {
        string? studioAnchor = umbSegs.Length > 0 ? umbSegs[0] : null;
        for (int i = 0; i < pathSegs.Length; i++)
        {
            string seg = pathSegs[i];
            if (string.Equals(seg, "StreamItAll", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(studioAnchor) && string.Equals(seg, studioAnchor, StringComparison.OrdinalIgnoreCase)) ||
                (includeEngines && string.Equals(seg, "engines", StringComparison.OrdinalIgnoreCase)))
                return i;
        }
        bool umbHasReEngine = Array.Exists(umbSegs, s => string.Equals(s, "reEngine", StringComparison.OrdinalIgnoreCase));
        for (int i = 0; i < pathSegs.Length; i++)
        {
            if (!string.Equals(pathSegs[i], "reEngine", StringComparison.OrdinalIgnoreCase)) continue;
            if (umbHasReEngine || (i + 1 < pathSegs.Length && (
                string.Equals(pathSegs[i + 1], "engines", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pathSegs[i + 1], "games", StringComparison.OrdinalIgnoreCase))))
                return i;
        }
        return -1;
    }

    private static string[] SplitUmbrellaSegments(string? umbrellaSubpath) =>
        string.IsNullOrWhiteSpace(umbrellaSubpath) ? Array.Empty<string>()
            : umbrellaSubpath.Trim().Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

    private static (string Root, string[] Segments) SplitRootAndSegments(string fullPath)
    {
        string norm = TrimNonRootDirectorySeparator(Path.GetFullPath(fullPath.Trim()));
        string root = Path.GetPathRoot(norm) ?? string.Empty;
        string rel = !string.IsNullOrEmpty(root) && norm.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? norm[root.Length..] : norm;
        return (root, rel.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string CombineRootAndSegments(string root, ReadOnlySpan<string> segments)
    {
        string combined = root;
        for (int i = 0; i < segments.Length; i++) combined = Path.Combine(combined, segments[i]);
        return TrimNonRootDirectorySeparator(combined);
    }

    private static string TrimNonRootDirectorySeparator(string path) =>
        string.Equals(path, Path.GetPathRoot(path), StringComparison.OrdinalIgnoreCase)
            ? path : path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static bool TryValidateLastInstallBaseDir(string? rawBase, SandboxResolution sandbox, out string validBase)
    {
        validBase = string.Empty;
        if (string.IsNullOrWhiteSpace(rawBase) || !Path.IsPathFullyQualified(rawBase.Trim())) return false;
        string full;
        try { full = TrimNonRootDirectorySeparator(Path.GetFullPath(rawBase.Trim())); }
        catch (Exception ex) { _ = ex; return false; }

        string? root = Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(root) || string.Equals(full, root, StringComparison.OrdinalIgnoreCase)) return false;

        if (sandbox.IsSandboxActive)
        {
            if (!SandboxEnvironmentSeam.IsPathWithinSandbox(full, sandbox) || IsSandboxRootPath(full, sandbox)) return false;
            string sandboxTemp = Path.Combine(KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, sandbox), "Temp");
            if (IsSameOrChildPath(full, sandboxTemp)) return false;
        }
        else if (IsSameOrChildPath(full, Path.GetTempPath())) return false;

        if (IsPoisonedInstallBaseDirectory(full, null) || HasReparsePointInExistingHierarchy(full, sandbox)) return false;
        validBase = full;
        return true;
    }

    private static bool IsSandboxRootPath(string fullPath, SandboxResolution sandbox)
    {
        foreach (string? c in new[] { sandbox.SandboxRoot, sandbox.SystemDriveRoot, sandbox.StorageDriveRoot, sandbox.RemovableDriveRoot })
            if (!string.IsNullOrWhiteSpace(c) && string.Equals(fullPath, TrimNonRootDirectorySeparator(Path.GetFullPath(c)), StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
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
