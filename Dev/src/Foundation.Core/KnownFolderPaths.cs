// @module: Foundation.Core.KnownFolderPaths
// @purpose: Redirects Environment.SpecialFolder lookups and host paths into active 4-drive sandbox paths while keeping production host paths untouched when inactive.
// @depends-on: System, System.Collections.Generic, System.IO
// @exports: KnownFolderPaths

using System;
using System.Collections.Generic;
using System.IO;

namespace Foundation.Core;

/// <summary>
/// Resolves Windows special folders against the active sandbox seam when FOUNDATION_SANDBOX_ROOT is active,
/// or delegates directly to Environment.GetFolderPath when running in production or Stage 1 isolation.
/// </summary>
public static class KnownFolderPaths
{
    private static readonly Environment.SpecialFolder[] PrefixRemapOrder =
    [
        Environment.SpecialFolder.Startup,
        Environment.SpecialFolder.Programs,
        Environment.SpecialFolder.StartMenu,
        Environment.SpecialFolder.ApplicationData,
        Environment.SpecialFolder.LocalApplicationData,
        Environment.SpecialFolder.DesktopDirectory,
        Environment.SpecialFolder.MyDocuments,
        Environment.SpecialFolder.UserProfile,
        Environment.SpecialFolder.CommonPrograms,
        Environment.SpecialFolder.CommonStartMenu,
        Environment.SpecialFolder.CommonApplicationData,
        Environment.SpecialFolder.ProgramFilesX86,
        Environment.SpecialFolder.ProgramFiles,
        Environment.SpecialFolder.SystemX86,
        Environment.SpecialFolder.System,
        Environment.SpecialFolder.Windows
    ];

    public static string GetFolderPath(Environment.SpecialFolder folder, SandboxResolution? sandbox = null)
    {
        SandboxResolution res = sandbox ?? SandboxEnvironmentSeam.ResolveCurrent();
        return res.IsSandboxActive ? ResolveSandboxFolder(folder, res) : Environment.GetFolderPath(folder);
    }

    public static string GetFolderPath(
        Environment.SpecialFolder folder,
        Environment.SpecialFolderOption option,
        SandboxResolution? sandbox = null)
    {
        SandboxResolution res = sandbox ?? SandboxEnvironmentSeam.ResolveCurrent();
        if (!res.IsSandboxActive) return Environment.GetFolderPath(folder, option);

        string resolved = ResolveSandboxFolder(folder, res);
        if (option == Environment.SpecialFolderOption.Create && !string.IsNullOrWhiteSpace(resolved))
            Directory.CreateDirectory(resolved);
        return resolved;
    }

    public static string ResolveLocalProgramsDirectory(SandboxResolution? sandbox = null) =>
        Path.Combine(GetFolderPath(Environment.SpecialFolder.LocalApplicationData, sandbox), "Programs");

    public static string MapHostPathToSandbox(string hostPath, SandboxResolution? sandbox = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostPath);
        SandboxResolution res = sandbox ?? SandboxEnvironmentSeam.ResolveCurrent();
        string normInput = SandboxEnvironmentSeam.NormalizePath(hostPath);
        if (!res.IsSandboxActive || SandboxEnvironmentSeam.IsPathWithinSandbox(normInput, res))
            return normInput;

        foreach (Environment.SpecialFolder sf in PrefixRemapOrder)
        {
            string rawHost = Environment.GetFolderPath(sf);
            if (string.IsNullOrWhiteSpace(rawHost)) continue;
            string hostPrefix = SandboxEnvironmentSeam.NormalizePath(rawHost);
            string targetPrefix = ResolveSandboxFolder(sf, res);
            if (TryRemapPrefix(normInput, hostPrefix, targetPrefix, out string mapped))
                return mapped;
        }

        string? inputDriveRoot = Path.GetPathRoot(normInput);
        if (!string.IsNullOrWhiteSpace(inputDriveRoot) && !string.IsNullOrWhiteSpace(res.StorageDriveRoot))
        {
            string normDrive = SandboxEnvironmentSeam.NormalizePath(inputDriveRoot);
            foreach (string folder in new[] { "Programs", "Projects" })
            {
                string drivePrefix = SandboxEnvironmentSeam.NormalizePath(Path.Combine(normDrive, folder));
                string targetPrefix = SandboxEnvironmentSeam.NormalizePath(Path.Combine(res.StorageDriveRoot, folder));
                if (TryRemapPrefix(normInput, drivePrefix, targetPrefix, out string mapped))
                    return mapped;
            }
        }

        SandboxEnvironmentSeam.ThrowContainmentViolation(
            $"Fail-Closed Sandbox Containment Violation: Host path '{normInput}' cannot be mapped safely inside sandbox '{res.SandboxRoot}'.");
        return normInput;
    }

    private static bool TryRemapPrefix(string normInput, string hostPrefix, string targetPrefix, out string mapped)
    {
        if (string.Equals(normInput, hostPrefix, StringComparison.OrdinalIgnoreCase))
        {
            mapped = targetPrefix;
            return true;
        }
        string withSep = hostPrefix.EndsWith(Path.DirectorySeparatorChar) ? hostPrefix : hostPrefix + Path.DirectorySeparatorChar;
        if (normInput.StartsWith(withSep, StringComparison.OrdinalIgnoreCase))
        {
            mapped = Path.Combine(targetPrefix, normInput[withSep.Length..]);
            return true;
        }
        mapped = normInput;
        return false;
    }

    private static string ResolveSandboxFolder(Environment.SpecialFolder folder, SandboxResolution res)
    {
        string sysDrive = res.SystemDriveRoot ?? Path.Combine(res.SandboxRoot!, "C");
        string profile = res.SandboxProfileDir ?? Path.Combine(sysDrive, "Users", "Sandbox");
        string roaming = Path.Combine(profile, "AppData", "Roaming");
        string startMenu = Path.Combine(roaming, "Microsoft", "Windows", "Start Menu");
        string startPrograms = Path.Combine(startMenu, "Programs");
        string programData = Path.Combine(sysDrive, "ProgramData");

        return folder switch
        {
            Environment.SpecialFolder.UserProfile => profile,
            Environment.SpecialFolder.LocalApplicationData => Path.Combine(profile, "AppData", "Local"),
            Environment.SpecialFolder.ApplicationData => roaming,
            Environment.SpecialFolder.DesktopDirectory or Environment.SpecialFolder.Desktop => Path.Combine(profile, "Desktop"),
            Environment.SpecialFolder.MyDocuments or Environment.SpecialFolder.Personal => Path.Combine(profile, "Documents"),
            Environment.SpecialFolder.Programs => startPrograms,
            Environment.SpecialFolder.StartMenu => startMenu,
            Environment.SpecialFolder.Startup => Path.Combine(startPrograms, "Startup"),
            Environment.SpecialFolder.ProgramFiles => Path.Combine(sysDrive, "Program Files"),
            Environment.SpecialFolder.ProgramFilesX86 => Path.Combine(sysDrive, "Program Files (x86)"),
            Environment.SpecialFolder.CommonApplicationData => programData,
            Environment.SpecialFolder.CommonStartMenu => Path.Combine(programData, "Microsoft", "Windows", "Start Menu"),
            Environment.SpecialFolder.CommonPrograms => Path.Combine(programData, "Microsoft", "Windows", "Start Menu", "Programs"),
            Environment.SpecialFolder.CommonDesktopDirectory => Path.Combine(sysDrive, "Users", "Public", "Desktop"),
            Environment.SpecialFolder.Windows => Path.Combine(sysDrive, "Windows"),
            Environment.SpecialFolder.System => Path.Combine(sysDrive, "Windows", "System32"),
            Environment.SpecialFolder.SystemX86 => Path.Combine(sysDrive, "Windows", "SysWOW64"),
            _ => Path.Combine(profile, folder.ToString())
        };
    }
}
