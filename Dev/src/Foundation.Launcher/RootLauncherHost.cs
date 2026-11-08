// @module: Foundation.Launcher.RootLauncherHost
// @purpose: Clean-root launcher stub forwarding CLI subcommands with Win32 AttachConsole(-1) or spawning the GUI/Tray daemon asynchronously.
// @depends-on: System, System.Diagnostics, System.IO, System.Runtime.InteropServices
// @exports: RootLauncherHost

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Foundation.Launcher;

public static class RootLauncherHost
{
    private const int ATTACH_PARENT_PROCESS = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    public static string ResolveTargetExecutable(
        string rootDir,
        string appExecutableRelativePath,
        string? currentProcessPath = null)
    {
        string normalizedRoot = Path.GetFullPath(rootDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string trimmedRel = (appExecutableRelativePath ?? string.Empty)
            .Trim()
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        bool isBareFileName = trimmedRel.IndexOf(Path.DirectorySeparatorChar) < 0 &&
                              trimmedRel.IndexOf(Path.AltDirectorySeparatorChar) < 0;

        string inAppCandidate = Path.Combine(normalizedRoot, "app", Path.GetFileName(trimmedRel));
        string directCandidate = Path.Combine(normalizedRoot, trimmedRel);

        string chosen = isBareFileName ? inAppCandidate : directCandidate;
        if (!string.IsNullOrWhiteSpace(currentProcessPath))
        {
            string fullChosen = Path.GetFullPath(chosen);
            string fullCurrent = Path.GetFullPath(currentProcessPath);
            if (string.Equals(fullChosen, fullCurrent, StringComparison.OrdinalIgnoreCase))
            {
                chosen = inAppCandidate;
            }
        }

        return chosen;
    }

    public static int Run(string appExecutableRelativePath, string displayName, string[] args)
    {
        string rootDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? selfPath = Environment.ProcessPath;
        string appExe = ResolveTargetExecutable(rootDir, appExecutableRelativePath, selfPath);

        if (!string.IsNullOrWhiteSpace(selfPath) &&
            string.Equals(Path.GetFullPath(appExe), Path.GetFullPath(selfPath), StringComparison.OrdinalIgnoreCase))
        {
            if (OperatingSystem.IsWindows())
            {
                try { AttachConsole(ATTACH_PARENT_PROCESS); } catch { }
            }
            Console.Error.WriteLine($"Fail-closed self-spawn guard: refusing to launch '{appExe}' from itself.");
            return 1;
        }

        if (!File.Exists(appExe))
        {
            if (OperatingSystem.IsWindows())
            {
                try { AttachConsole(ATTACH_PARENT_PROCESS); } catch { }
            }
            Console.Error.WriteLine($"{displayName} application binary not found at '{appExe}'.");
            return 1;
        }

        bool isCliOrSyncWait = ShouldWaitForChild(args);
        if (isCliOrSyncWait && OperatingSystem.IsWindows())
        {
            try
            {
                if (!Console.IsOutputRedirected && !Console.IsErrorRedirected)
                {
                    AttachConsole(ATTACH_PARENT_PROCESS);
                }
            }
            catch { }
        }

        var psi = new ProcessStartInfo
        {
            FileName = appExe,
            WorkingDirectory = Path.GetDirectoryName(appExe) ?? rootDir,
            UseShellExecute = false
        };

        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using Process? proc = Process.Start(psi);
        if (proc == null)
        {
            return 1;
        }

        if (isCliOrSyncWait)
        {
            proc.WaitForExit();
            return proc.ExitCode;
        }

        return 0;
    }

    public static bool ShouldWaitForChild(string[] args)
    {
        if (args == null || args.Length == 0)
            return false;

        foreach (string raw in args)
        {
            string a = raw.Trim();
            if (a.Equals("--version", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-v", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("/?", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("help", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--headless", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        string first = args[0].Trim();
        return !first.StartsWith("-", StringComparison.Ordinal);
    }
}
