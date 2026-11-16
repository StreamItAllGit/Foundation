// @module: Foundation.Core.SandboxEnvironmentSeam
// @purpose: Resolves process-env-only 4-drive sandbox seams, dynamic subst backing directories, and fail-closed ExitCode=3 host containment guards.
// @depends-on: System, System.Collections, System.Collections.Generic, System.Diagnostics.CodeAnalysis, System.IO, System.Runtime.InteropServices, System.Text
// @exports: SandboxMountSpec, SandboxResolution, SandboxEnvironmentSeam

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Foundation.Core;

public sealed record SandboxMountSpec(string DriveRole, string DriveMount, string? DirectoryPath, bool IsEnabled);

public sealed record SandboxResolution(
    bool IsSandboxActive,
    string? SandboxRoot,
    string? SystemDriveRoot,
    string? StorageDriveRoot,
    string? RemovableDriveRoot,
    string? DiscDriveRoot,
    string? SandboxProfileDir,
    string? RegistryHivePath,
    string? RegistryBaselinePath,
    string? RegistryLockPath)
{
    public static SandboxResolution Inactive { get; } = new(false, null, null, null, null, null, null, null, null, null);
}

/// <summary>
/// Resolves 4-drive sandbox configuration strictly from process environment variables (never auto-reading .secrets\.env)
/// and enforces fail-closed host containment with ExitCode = 3.
/// </summary>
public static class SandboxEnvironmentSeam
{
    public const int ContainmentExitCode = 3;
    public const string EnvSandboxRoot = "FOUNDATION_SANDBOX_ROOT";
    public const string EnvIsolatedTest = "APP_ISOLATED_TEST";
    public const string EnvSandboxCDrive = "SANDBOX_C_DRIVE";
    public const string EnvSandboxDDrive = "SANDBOX_D_DRIVE";
    public const string EnvSandboxRemovableDrive = "SANDBOX_REMOVABLE_DRIVE";
    public const string EnvSandboxDiscDrive = "SANDBOX_DISC_DRIVE";
    public const string EnvSandboxProfileDir = "FOUNDATION_SANDBOX_PROFILE_DIR";

    private static readonly Environment.SpecialFolder[] SystemProtectedFolders =
    [
        Environment.SpecialFolder.Windows, Environment.SpecialFolder.System,
        Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData
    ];

    private static readonly Environment.SpecialFolder[] UserProtectedFolders =
    [
        Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData,
        Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments,
        Environment.SpecialFolder.Programs, Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.Startup
    ];

    [DllImport("kernel32.dll", EntryPoint = "QueryDosDeviceW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int QueryDosDeviceNative(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);

    public static SandboxResolution ResolveCurrent() => Resolve(null);

    public static SandboxResolution Resolve(IReadOnlyDictionary<string, string?>? envOverride)
    {
        if (IsTruthy(ReadEnv(envOverride, EnvIsolatedTest))) return SandboxResolution.Inactive;
        string? rawRoot = ReadEnv(envOverride, EnvSandboxRoot);
        if (string.IsNullOrWhiteSpace(rawRoot)) return SandboxResolution.Inactive;

        string sandboxRoot = NormalizePath(rawRoot);
        string systemDrive = NormalizeDriveOrPath(ReadEnv(envOverride, EnvSandboxCDrive)) ?? Path.Combine(sandboxRoot, "C");
        string storageDrive = NormalizeDriveOrPath(ReadEnv(envOverride, EnvSandboxDDrive)) ?? Path.Combine(sandboxRoot, "D");
        string? rawProfile = ReadEnv(envOverride, EnvSandboxProfileDir);
        string profileDir = !string.IsNullOrWhiteSpace(rawProfile) ? NormalizePath(rawProfile) : Path.Combine(systemDrive, "Users", "Sandbox");

        var resolution = new SandboxResolution(
            true, sandboxRoot, systemDrive, storageDrive,
            NormalizeDriveOrPath(ReadEnv(envOverride, EnvSandboxRemovableDrive)),
            NormalizeDriveOrPath(ReadEnv(envOverride, EnvSandboxDiscDrive)),
            profileDir,
            Path.Combine(sandboxRoot, "registry", "registry.hive.json"),
            Path.Combine(sandboxRoot, "registry", "registry.baseline.json"),
            Path.Combine(sandboxRoot, "locks", "registry.hive.lock"));

        ValidateResolution(resolution);
        return resolution;
    }

    public static void ValidateResolution(SandboxResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        if (!resolution.IsSandboxActive) return;
        if (string.IsNullOrWhiteSpace(resolution.SandboxRoot)) ThrowContainmentViolation("Active sandbox configuration is missing SandboxRoot.");

        var substDriveRoots = BuildVirtualSubstDriveSet(resolution);
        string?[] targets = [resolution.SandboxRoot, resolution.SystemDriveRoot, resolution.StorageDriveRoot, resolution.RemovableDriveRoot, resolution.DiscDriveRoot, resolution.SandboxProfileDir];
        foreach (string? candidate in targets)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            string norm = NormalizePath(candidate);
            if (IsHostProtectedCollision(norm, substDriveRoots))
                ThrowContainmentViolation($"Sandbox path '{norm}' collides with protected host OS, user profile, Programs, or Projects directory.");
            string? backing = TryResolveSubstBackingDirectory(norm);
            if (!string.IsNullOrWhiteSpace(backing) && IsHostProtectedCollision(backing, substDriveRoots))
                ThrowContainmentViolation($"Sandbox drive '{norm}' subst backing path '{backing}' collides with protected host directory.");
        }
    }

    public static string AssertSandboxPathIfActive(string candidatePath, SandboxResolution? sandbox = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        string full = NormalizePath(candidatePath);
        SandboxResolution res = sandbox ?? ResolveCurrent();
        if (res.IsSandboxActive && !IsPathWithinSandbox(full, res))
            ThrowContainmentViolation($"Fail-Closed Sandbox Containment Violation: Path '{full}' escapes active sandbox '{res.SandboxRoot}'.");
        return full;
    }

    public static bool IsPathWithinSandbox(string? candidatePath, SandboxResolution? sandbox = null)
    {
        if (string.IsNullOrWhiteSpace(candidatePath)) return false;
        SandboxResolution res = sandbox ?? ResolveCurrent();
        if (!res.IsSandboxActive) return false;
        string full;
        try { full = NormalizePath(candidatePath); } catch { return false; }
        if (IsHostProtectedCollision(full, BuildVirtualSubstDriveSet(res, includeDeclaredDrives: true))) return false;
        foreach (string root in EnumerateAllowedRoots(res))
            if (IsChildPath(full, root, true)) return true;
        return false;
    }

    public static bool IsContainmentViolation(Exception? ex)
    {
        for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
            if (cur is InvalidOperationException && cur.Data.Contains("ExitCode") &&
                int.TryParse(cur.Data["ExitCode"]?.ToString(), out int code) && code == ContainmentExitCode)
                return true;
        return false;
    }

    [DoesNotReturn]
    public static void ThrowContainmentViolation(string message, Exception? innerException = null)
    {
        var ex = new InvalidOperationException(message, innerException);
        ex.Data["ExitCode"] = ContainmentExitCode;
        throw ex;
    }

    public static string? TryResolveSubstBackingDirectory(string? driveOrPath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(driveOrPath)) return null;
        string trimmed = StripExtendedPrefix(driveOrPath.Trim().Trim('"'));
        if (trimmed.Length < 2 || !char.IsAsciiLetter(trimmed[0]) || trimmed[1] != ':') return null;
        var buffer = new StringBuilder(1024);
        if (QueryDosDeviceNative($"{char.ToUpperInvariant(trimmed[0])}:", buffer, buffer.Capacity) <= 0) return null;
        string target = buffer.ToString();
        if (!target.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase)) return null;
        string rest = target[4..];
        if (rest.StartsWith("Volume{", StringComparison.OrdinalIgnoreCase)) return null;
        return NormalizePath(rest.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + rest[4..] : rest);
    }

    public static Dictionary<string, string?> GetProcessEnvironment()
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            if (entry.Key is string key) map[key] = entry.Value?.ToString();
        return map;
    }

    internal static string NormalizePath(string path)
    {
        string trimmed = StripExtendedPrefix(path.Trim().Trim('"'));
        if (trimmed.Length is >= 1 and <= 3 && char.IsAsciiLetter(trimmed[0]) &&
            (trimmed.Length == 1 || (trimmed[1] == ':' && (trimmed.Length == 2 || trimmed[2] is '\\' or '/'))))
            return $"{char.ToUpperInvariant(trimmed[0])}:\\";
        string full = Path.GetFullPath(trimmed);
        return string.Equals(full, Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase)
            ? full : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string? NormalizeDriveOrPath(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : NormalizePath(raw);

    private static IEnumerable<string> EnumerateAllowedRoots(SandboxResolution res)
    {
        string?[] roots = [res.SandboxRoot, res.SystemDriveRoot, res.StorageDriveRoot, res.RemovableDriveRoot, res.DiscDriveRoot, res.SandboxProfileDir];
        foreach (string? raw in roots)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string norm = NormalizePath(raw);
            yield return norm;
            string? backing = TryResolveSubstBackingDirectory(norm);
            if (!string.IsNullOrWhiteSpace(backing)) yield return backing;
        }
    }

    private static HashSet<string> BuildVirtualSubstDriveSet(SandboxResolution res, bool includeDeclaredDrives = false)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? raw in new[] { res.SystemDriveRoot, res.StorageDriveRoot, res.RemovableDriveRoot, res.DiscDriveRoot })
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string norm = NormalizePath(raw);
            if (norm.Length != 3 || norm[1] != ':' || norm[2] != '\\') continue;
            bool isSubst = TryResolveSubstBackingDirectory(norm) is not null;
            bool isDeclaredSafeNonHostDrive = includeDeclaredDrives && !norm.StartsWith("C:", StringComparison.OrdinalIgnoreCase) &&
                !norm.StartsWith("D:", StringComparison.OrdinalIgnoreCase) && !HasHostRootMarkers(norm);
            if (isSubst || isDeclaredSafeNonHostDrive) set.Add(norm);
        }
        return set;
    }

    private static bool HasHostRootMarkers(string normDrive) =>
        Directory.Exists(Path.Combine(normDrive, "Windows")) || Directory.Exists(Path.Combine(normDrive, "Programs")) ||
        Directory.Exists(Path.Combine(normDrive, "Projects")) || Directory.Exists(Path.Combine(normDrive, "Users"));

    private static bool IsHostProtectedCollision(string normPath, IReadOnlySet<string> substDriveRoots)
    {
        string? tempRoot = TryNormalize(Path.GetTempPath());
        bool insideTemp = tempRoot is not null && IsChildPath(normPath, tempRoot, false);

        foreach (var sf in SystemProtectedFolders)
            if (TryNormalize(KnownFolderPaths.GetHostFolderPath(sf)) is { } r && (IsChildPath(r, normPath, true) || IsChildPath(normPath, r, true))) return true;

        if (TryNormalize(KnownFolderPaths.GetHostFolderPath(Environment.SpecialFolder.LocalApplicationData)) is { } localApp)
        {
            string localPrograms = NormalizePath(Path.Combine(localApp, "Programs"));
            if (IsChildPath(localPrograms, normPath, true) || IsChildPath(normPath, localPrograms, true)) return true;
        }

        foreach (var uf in UserProtectedFolders)
            if (TryNormalize(KnownFolderPaths.GetHostFolderPath(uf)) is { } uRoot && (IsChildPath(uRoot, normPath, true) || (!insideTemp && IsChildPath(normPath, uRoot, true)))) return true;

        if (Path.GetPathRoot(normPath) is { Length: > 0 } driveRoot)
        {
            string normDrive = NormalizePath(driveRoot);
            if (!substDriveRoots.Contains(normDrive))
            {
                if (IsChildPath(normPath, NormalizePath(Path.Combine(normDrive, "Programs")), true) ||
                    IsChildPath(normPath, NormalizePath(Path.Combine(normDrive, "Projects")), true)) return true;
                if (string.Equals(normPath, normDrive, StringComparison.OrdinalIgnoreCase) &&
                    (HasHostRootMarkers(normDrive) || normDrive.StartsWith("D:", StringComparison.OrdinalIgnoreCase))) return true;
            }
        }
        return false;
    }

    private static bool IsChildPath(string candidate, string root, bool allowEqual) =>
        string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
            ? allowEqual
            : candidate.StartsWith(root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string StripExtendedPrefix(string path) =>
        path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + path[8..]
            : (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase) ? path[4..] : path);

    private static string? TryNormalize(string? raw) { try { return string.IsNullOrWhiteSpace(raw) ? null : NormalizePath(raw); } catch { return null; } }

    private static string? ReadEnv(IReadOnlyDictionary<string, string?>? overrideMap, string key)
    {
        if (overrideMap is not null)
            return overrideMap.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value is { } v && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
        string? envVal = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(envVal) ? null : envVal.Trim();
    }

    private static bool IsTruthy(string? value) =>
        !string.IsNullOrWhiteSpace(value) && (value.Equals("1", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase));
}
