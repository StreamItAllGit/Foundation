// @module: Foundation.Launcher.RootLauncherHost
// @purpose: Clean-root launcher stub forwarding CLI subcommands with Win32 AttachConsole(-1) or spawning the GUI/Tray daemon asynchronously.
// @depends-on: System, System.Diagnostics, System.IO, System.Runtime.InteropServices
// @exports: RootLauncherHost

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Foundation.Launcher;

public static class RootLauncherHost
{
    private const int ATTACH_PARENT_PROCESS = -1;

    private static readonly HashSet<string> SyncWaitFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "--version", "-v", "--help", "-h", "/?", "help",
        "--dry-run", "--wait", "--open",
        "--check-updates", "--register-assoc",
        "--diagnose", "--doctor", "--verify", "--verify-module"
    };

    private static readonly HashSet<string> ValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--port", "--ui", "--data-dir", "--config", "--log-dir", "--feed", "--open",
        "--frames", "--profile", "--install-dir", "--reengine-dir",
        "--suite-dir", "--rom", "--manifest", "--output", "--out",
        "--file", "--path",
        "--programs-dir", "--save-dir", "--preload-media", "--backend",
        "--automation-port", "--automation-token", "--verify-module"
    };

    private static readonly HashSet<string> PathValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--open", "--data-dir", "--config", "--log-dir", "--install-dir", "--reengine-dir",
        "--suite-dir", "--rom", "--manifest",
        "--output", "--out", "--file", "--path",
        "--programs-dir", "--save-dir", "--preload-media", "--verify-module"
    };

    private static readonly HashSet<string> OptionalValueFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "--suite-tab", "--window", "--browser"
    };

    public static IReadOnlySet<string> DefaultSyncWaitFlags => SyncWaitFlags;
    public static IReadOnlySet<string> DefaultValueOptions => ValueOptions;
    public static IReadOnlySet<string> DefaultPathValueOptions => PathValueOptions;

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

    public static int Run(string appExecutableRelativePath, string displayName, string[] args) =>
        Run(appExecutableRelativePath, displayName, args, options: null);

    public static int Run(string appExecutableRelativePath, string displayName, string[] args, RootLauncherOptions? options)
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

        bool isCliOrSyncWait = ShouldWaitForChild(args, options);
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

        string callerCwd = Environment.CurrentDirectory;
        string[] normalizedArgs = NormalizeRelativePathArgs(args, callerCwd, options);

        var psi = new ProcessStartInfo
        {
            FileName = appExe,
            WorkingDirectory = Path.GetDirectoryName(appExe) ?? rootDir,
            UseShellExecute = false
        };

        foreach (string arg in normalizedArgs)
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
            bool interrupted = false;
            ConsoleCancelEventHandler handler = (_, e) =>
            {
                e.Cancel = true;
                interrupted = true;
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill(entireProcessTree: true);
                    }
                }
                catch { }
            };

            Console.CancelKeyPress += handler;
            try
            {
                proc.WaitForExit();
                return interrupted ? 130 : proc.ExitCode;
            }
            finally
            {
                Console.CancelKeyPress -= handler;
            }
        }

        return 0;
    }

    public static string[] NormalizeRelativePathArgs(string[]? args, string callerCwd) =>
        NormalizeRelativePathArgs(args, callerCwd, options: null);

    public static string[] NormalizeRelativePathArgs(string[]? args, string callerCwd, RootLauncherOptions? options)
    {
        if (args == null || args.Length == 0)
            return Array.Empty<string>();

        string[] result = new string[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            string raw = args[i];
            if (string.IsNullOrWhiteSpace(raw))
            {
                result[i] = raw;
                continue;
            }

            int eq = raw.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0 && raw.StartsWith("-", StringComparison.Ordinal))
            {
                string opt = raw[..eq];
                string val = raw[(eq + 1)..];
                result[i] = IsPathValueOption(opt, options)
                    ? $"{opt}={RebasePathIfRelative(val, callerCwd)}"
                    : raw;
                continue;
            }

            if (raw.StartsWith("-", StringComparison.Ordinal))
            {
                result[i] = raw;
                if (IsOptionalValueFlag(raw) &&
                    i + 1 < args.Length &&
                    !args[i + 1].TrimStart().StartsWith("-", StringComparison.Ordinal))
                {
                    i++;
                    result[i] = args[i];
                }
                else if (IsValueOption(raw, options) &&
                         i + 1 < args.Length &&
                         !args[i + 1].TrimStart().StartsWith("--", StringComparison.Ordinal))
                {
                    i++;
                    string nextVal = args[i];
                    result[i] = IsPathValueOption(raw, options)
                        ? RebasePathIfRelative(nextVal, callerCwd)
                        : nextVal;
                }
                continue;
            }

            result[i] = IsLikelyRelativeFileArg(raw, callerCwd)
                ? RebasePathIfRelative(raw, callerCwd)
                : raw;
        }

        return result;
    }

    public static bool ShouldWaitForChild(string[]? args) =>
        ShouldWaitForChild(args, options: null);

    public static bool ShouldWaitForChild(string[]? args, RootLauncherOptions? options)
    {
        if (args == null || args.Length == 0)
            return false;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i].Trim();
            if (string.IsNullOrEmpty(a)) continue;

            int eq = a.IndexOf('=', StringComparison.Ordinal);
            string flagName = eq > 0 && a.StartsWith("-", StringComparison.Ordinal) ? a[..eq] : a;

            if (IsSyncWaitFlag(flagName, options))
            {
                return true;
            }

            if (eq < 0 && IsOptionalValueFlag(flagName))
            {
                if (i + 1 < args.Length && !args[i + 1].TrimStart().StartsWith("-", StringComparison.Ordinal))
                    i++;
                continue;
            }

            if (eq < 0 && IsValueOption(flagName, options))
            {
                if (i + 1 < args.Length && !args[i + 1].TrimStart().StartsWith("--", StringComparison.Ordinal))
                    i++;
                continue;
            }

            if (!a.StartsWith("-", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSyncWaitFlag(string flagName, RootLauncherOptions? options) =>
        SyncWaitFlags.Contains(flagName) || ContainsIgnoreCase(options?.ExtraSyncWaitFlags, flagName);

    private static bool IsValueOption(string flagName, RootLauncherOptions? options) =>
        ValueOptions.Contains(flagName) ||
        ContainsIgnoreCase(options?.ExtraValueOptions, flagName) ||
        ContainsIgnoreCase(options?.ExtraPathValueOptions, flagName);

    private static bool IsPathValueOption(string flagName, RootLauncherOptions? options) =>
        PathValueOptions.Contains(flagName) || ContainsIgnoreCase(options?.ExtraPathValueOptions, flagName);

    private static bool IsOptionalValueFlag(string flagName) => OptionalValueFlags.Contains(flagName);

    private static bool ContainsIgnoreCase(IReadOnlyCollection<string>? set, string value)
    {
        if (set == null || set.Count == 0) return false;
        foreach (string item in set)
            if (string.Equals(item, value, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string RebasePathIfRelative(string value, string callerCwd)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && !uri.IsFile)
        {
            return value;
        }
        if (Path.IsPathRooted(value)) return value;
        return Path.GetFullPath(value, callerCwd);
    }

    private static bool IsLikelyRelativeFileArg(string arg, string callerCwd)
    {
        if (Uri.TryCreate(arg, UriKind.Absolute, out Uri? uri) && !uri.IsFile) return false;
        if (Path.IsPathRooted(arg)) return false;
        if (arg.Contains(Path.DirectorySeparatorChar) || arg.Contains(Path.AltDirectorySeparatorChar) || arg.StartsWith('.'))
        {
            return true;
        }
        string candidate = Path.GetFullPath(arg, callerCwd);
        return File.Exists(candidate) || Directory.Exists(candidate);
    }
}
