// @module: Foundation.Core.KnownFolderPaths
// @purpose: Redirects Environment.SpecialFolder lookups and host paths into active 4-drive sandbox paths while keeping production host paths untouched when inactive.
// @depends-on: System, System.Collections.Generic, System.IO, System.Runtime.InteropServices
// @exports: KnownFolderPaths

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Foundation.Core;

/// <summary>
/// Resolves Windows special folders against the active sandbox seam when FOUNDATION_SANDBOX_ROOT is active,
/// or delegates directly to Environment.GetFolderPath when running in production or Stage 1 isolation.
/// </summary>
public static class KnownFolderPaths
{
    private const uint KfFlagDefaultPath = 0x00000400;
    private const uint KfFlagDontVerify = 0x00004000;

    private static readonly Guid FolderProfileId = new("5E6C858F-0E22-4760-9AFE-EA3317B67173");
    private static readonly Guid FolderLocalAppDataId = new("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");
    private static readonly Guid FolderRoamingAppDataId = new("3EB685DB-65F9-4CF6-A03A-E3EF65729F3D");
    private static readonly Guid FolderDesktopId = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
    private static readonly Guid FolderDocumentsId = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7");
    private static readonly Guid FolderProgramFilesId = new("905E63B6-C1BF-494E-B29C-65B732D3D21A");
    private static readonly Guid FolderProgramFilesX86Id = new("7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E");
    private static readonly Guid FolderCommonAppDataId = new("62AB5D82-FDC1-4DC3-A9DD-070D1D495D97");
    private static readonly Guid FolderCommonProgramsId = new("0139D44E-6AFE-49F2-8690-3DAFCAE6FFB8");
    private static readonly Guid FolderCommonStartMenuId = new("A4115719-D62E-491D-AA7C-E74B8BE3B067");
    private static readonly Guid FolderCommonDesktopId = new("C4AA340D-F20F-4863-AFEF-F87EF2E6BA25");
    private static readonly Guid FolderWindowsId = new("F38BF404-1D43-42F2-9305-67DE0B28FC23");
    private static readonly Guid FolderSystemId = new("1AC14E77-02E7-4E5D-B744-2EB1AE5198B7");
    private static readonly Guid FolderSystemX86Id = new("D65231B0-B2F1-4857-A4CE-A8E7C6EA7D27");

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(in Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

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

    /// <summary>
    /// Resolves the true host Windows SpecialFolder path unaffected by child-process sandbox env redirection
    /// (USERPROFILE, LOCALAPPDATA, APPDATA), falling back to Environment.GetFolderPath on non-Windows hosts.
    /// </summary>
    public static string GetHostFolderPath(Environment.SpecialFolder folder)
    {
        if (!OperatingSystem.IsWindows())
            return Environment.GetFolderPath(folder);

        string? direct = TryGetWinKnownFolder(folder);
        if (!string.IsNullOrWhiteSpace(direct))
            return direct;

        string raw = Environment.GetFolderPath(folder);
        if (folder is Environment.SpecialFolder.Programs or Environment.SpecialFolder.StartMenu or Environment.SpecialFolder.Startup)
        {
            string? hostAppData = TryGetWinKnownFolder(Environment.SpecialFolder.ApplicationData);
            string? envAppData = Environment.GetEnvironmentVariable("APPDATA");
            if (!string.IsNullOrWhiteSpace(hostAppData))
            {
                string normHostApp = SandboxEnvironmentSeam.NormalizePath(hostAppData);
                if (!string.IsNullOrWhiteSpace(envAppData))
                {
                    string normEnvApp = SandboxEnvironmentSeam.NormalizePath(envAppData);
                    if (!string.Equals(normHostApp, normEnvApp, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(raw) &&
                            TryRemapPrefix(SandboxEnvironmentSeam.NormalizePath(raw), normEnvApp, normHostApp, out string unmapped))
                        {
                            return unmapped;
                        }
                        string startMenu = Path.Combine(normHostApp, "Microsoft", "Windows", "Start Menu");
                        return folder switch
                        {
                            Environment.SpecialFolder.StartMenu => startMenu,
                            Environment.SpecialFolder.Programs => Path.Combine(startMenu, "Programs"),
                            _ => Path.Combine(startMenu, "Programs", "Startup")
                        };
                    }
                }
            }
        }

        if (folder is Environment.SpecialFolder.Desktop or Environment.SpecialFolder.DesktopDirectory
            or Environment.SpecialFolder.MyDocuments or Environment.SpecialFolder.Personal)
        {
            string? hostProfile = TryGetWinKnownFolder(Environment.SpecialFolder.UserProfile);
            string? envProfile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrWhiteSpace(hostProfile))
            {
                string normHostProfile = SandboxEnvironmentSeam.TryToLongPath(SandboxEnvironmentSeam.NormalizePath(hostProfile));
                string fallbackFolder = folder is Environment.SpecialFolder.Desktop or Environment.SpecialFolder.DesktopDirectory
                    ? "Desktop"
                    : "Documents";
                if (!string.IsNullOrWhiteSpace(envProfile))
                {
                    string normEnvProfile = SandboxEnvironmentSeam.TryToLongPath(SandboxEnvironmentSeam.NormalizePath(envProfile));
                    if (!string.Equals(normHostProfile, normEnvProfile, StringComparison.OrdinalIgnoreCase))
                    {
                        string normRaw = string.IsNullOrWhiteSpace(raw) ? string.Empty : SandboxEnvironmentSeam.TryToLongPath(SandboxEnvironmentSeam.NormalizePath(raw));
                        if (!string.IsNullOrWhiteSpace(normRaw) &&
                            TryRemapPrefix(normRaw, normEnvProfile, normHostProfile, out string unmapped))
                        {
                            return unmapped;
                        }
                        return Path.Combine(normHostProfile, fallbackFolder);
                    }
                }
            }
        }

        return raw;
    }

    public static string MapHostPathToSandbox(string hostPath, SandboxResolution? sandbox = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostPath);
        SandboxResolution res = sandbox ?? SandboxEnvironmentSeam.ResolveCurrent();
        string normInput = SandboxEnvironmentSeam.NormalizePath(hostPath);
        if (!res.IsSandboxActive || SandboxEnvironmentSeam.IsPathWithinSandbox(normInput, res))
            return normInput;

        foreach (Environment.SpecialFolder sf in PrefixRemapOrder)
        {
            string rawHost = GetHostFolderPath(sf);
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

    private static string? TryGetWinKnownFolder(Environment.SpecialFolder folder)
    {
        (Guid guid, uint flags)? entry = folder switch
        {
            Environment.SpecialFolder.UserProfile => (FolderProfileId, KfFlagDefaultPath | KfFlagDontVerify),
            Environment.SpecialFolder.LocalApplicationData => (FolderLocalAppDataId, KfFlagDefaultPath | KfFlagDontVerify),
            Environment.SpecialFolder.ApplicationData => (FolderRoamingAppDataId, KfFlagDefaultPath | KfFlagDontVerify),
            Environment.SpecialFolder.DesktopDirectory or Environment.SpecialFolder.Desktop => (FolderDesktopId, KfFlagDontVerify),
            Environment.SpecialFolder.MyDocuments or Environment.SpecialFolder.Personal => (FolderDocumentsId, KfFlagDontVerify),
            Environment.SpecialFolder.ProgramFiles => (FolderProgramFilesId, KfFlagDefaultPath | KfFlagDontVerify),
            Environment.SpecialFolder.ProgramFilesX86 => (FolderProgramFilesX86Id, KfFlagDefaultPath | KfFlagDontVerify),
            Environment.SpecialFolder.CommonApplicationData => (FolderCommonAppDataId, KfFlagDefaultPath | KfFlagDontVerify),
            Environment.SpecialFolder.CommonPrograms => (FolderCommonProgramsId, KfFlagDefaultPath | KfFlagDontVerify),
            Environment.SpecialFolder.CommonStartMenu => (FolderCommonStartMenuId, KfFlagDefaultPath | KfFlagDontVerify),
            Environment.SpecialFolder.CommonDesktopDirectory => (FolderCommonDesktopId, KfFlagDefaultPath | KfFlagDontVerify),
            Environment.SpecialFolder.Windows => (FolderWindowsId, KfFlagDefaultPath | KfFlagDontVerify),
            Environment.SpecialFolder.System => (FolderSystemId, KfFlagDefaultPath | KfFlagDontVerify),
            Environment.SpecialFolder.SystemX86 => (FolderSystemX86Id, KfFlagDefaultPath | KfFlagDontVerify),
            _ => null
        };
        if (entry is null) return null;
        (Guid guid, uint flags) = entry.Value;

        IntPtr ptr = IntPtr.Zero;
        try
        {
            int hr = SHGetKnownFolderPath(in guid, flags, IntPtr.Zero, out ptr);
            return hr == 0 && ptr != IntPtr.Zero ? Marshal.PtrToStringUni(ptr) : null;
        }
        finally
        {
            if (ptr != IntPtr.Zero) Marshal.FreeCoTaskMem(ptr);
        }
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
