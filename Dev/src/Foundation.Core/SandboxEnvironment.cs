// @module: Foundation.Core.Sandboxing
// @purpose: Resolves CLI/env sandbox overrides (--data-dir, --config, --log-dir, --test-mode, 4 UI modes) and enforces fail-closed path containment.
// @depends-on: System, System.Collections.Generic, System.IO
// @exports: UiPresentationMode, SandboxEnvironment

using System;
using System.Collections.Generic;
using System.IO;

namespace Foundation.Core;

/// <summary>
/// Canonical 4 UI presentation modes per Standard 17 §4 (Suite Tab, Standalone Window, Local Browser, Scoped Mobile PWA).
/// </summary>
public enum UiPresentationMode
{
    DesktopWindow,
    LocalBrowser,
    SuiteTab,
    HeadlessOrTrayOnly
}

/// <summary>
/// Resolves standard CLI flags and environment variables for fail-closed Sandbox and Production execution (Standard 02).
/// </summary>
public sealed class SandboxEnvironment
{
    public string AppId { get; }
    public string BaseDirectory { get; }
    public string DataDirectory { get; }
    public string ConfigFilePath { get; }
    public string LogDirectory { get; }
    public int? PortOverride { get; }
    public bool TestMode { get; }
    public bool DryRun { get; }
    public bool Headless { get; }
    public bool VisibleTest { get; }
    public bool TrayOnly { get; }
    public bool ManagedByMother { get; }
    public bool SuiteTab { get; }
    public UiPresentationMode PresentationMode { get; }
    public bool IsContainedSandbox { get; }

    public SandboxEnvironment(
        string appId,
        IReadOnlyList<string>? args = null,
        string? baseDirectoryOverride = null,
        string? defaultConfigFileName = "settings.json")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        AppId = appId.Trim().ToLowerInvariant();
        string envPrefix = AppId.ToUpperInvariant().Replace('-', '_');

        IReadOnlyList<string> cli = args ?? Array.Empty<string>();
        BaseDirectory = Path.GetFullPath(!string.IsNullOrWhiteSpace(baseDirectoryOverride)
            ? baseDirectoryOverride
            : AppContext.BaseDirectory);

        string? cliDataDir = ReadFlagValue(cli, "--data-dir");
        string? cliConfig = ReadFlagValue(cli, "--config");
        string? cliLogDir = ReadFlagValue(cli, "--log-dir");
        string? cliPort = ReadFlagValue(cli, "--port");
        string? cliUi = ReadFlagValue(cli, "--ui");

        TestMode = HasFlag(cli, "--test-mode") || IsTruthyEnv($"{envPrefix}_TEST_MODE") || IsTruthyEnv("TEST_MODE");
        DryRun = HasFlag(cli, "--dry-run") || IsTruthyEnv($"{envPrefix}_DRY_RUN");
        Headless = HasFlag(cli, "--headless") || IsTruthyEnv($"{envPrefix}_HEADLESS");
        VisibleTest = HasFlag(cli, "--visible-test") || IsTruthyEnv($"{envPrefix}_VISIBLE_TEST") || IsTruthyEnv("VISIBLE_TEST");
        TrayOnly = HasFlag(cli, "--tray-only");
        ManagedByMother = HasFlag(cli, "--managed-by-mother") || IsTruthyEnv("MANAGED_BY_MOTHER");
        SuiteTab = HasFlag(cli, "--suite-tab");
        bool forceBrowser = HasFlag(cli, "--browser") || string.Equals(cliUi, "browser", StringComparison.OrdinalIgnoreCase);
        bool forceWindow = HasFlag(cli, "--window") || string.Equals(cliUi, "desktop", StringComparison.OrdinalIgnoreCase);

        if (SuiteTab)
        {
            PresentationMode = UiPresentationMode.SuiteTab;
        }
        else if (Headless || TrayOnly || ManagedByMother)
        {
            PresentationMode = UiPresentationMode.HeadlessOrTrayOnly;
        }
        else if (VisibleTest)
        {
            PresentationMode = UiPresentationMode.DesktopWindow;
        }
        else if (forceBrowser)
        {
            PresentationMode = UiPresentationMode.LocalBrowser;
        }
        else if (forceWindow)
        {
            PresentationMode = UiPresentationMode.DesktopWindow;
        }
        else
        {
            PresentationMode = UiPresentationMode.DesktopWindow;
        }

        string? resolvedData = FirstNonEmpty(
            cliDataDir,
            Environment.GetEnvironmentVariable($"{envPrefix}_DATA_DIR"),
            Environment.GetEnvironmentVariable("DATA_DIR"),
            Environment.GetEnvironmentVariable("APP_HOME"));

        IsContainedSandbox = TestMode || !string.IsNullOrWhiteSpace(resolvedData);
        DataDirectory = Path.GetFullPath(!string.IsNullOrWhiteSpace(resolvedData)
            ? resolvedData
            : Path.Combine(BaseDirectory, "data"));

        string? resolvedConfig = FirstNonEmpty(
            cliConfig,
            Environment.GetEnvironmentVariable($"{envPrefix}_CONFIG"),
            Environment.GetEnvironmentVariable("CONFIG_PATH"));

        ConfigFilePath = Path.GetFullPath(!string.IsNullOrWhiteSpace(resolvedConfig)
            ? resolvedConfig
            : Path.Combine(DataDirectory, defaultConfigFileName ?? "settings.json"));

        string? resolvedLogs = FirstNonEmpty(
            cliLogDir,
            Environment.GetEnvironmentVariable($"{envPrefix}_LOG_DIR"),
            Environment.GetEnvironmentVariable("LOG_DIR"));

        LogDirectory = Path.GetFullPath(!string.IsNullOrWhiteSpace(resolvedLogs)
            ? resolvedLogs
            : (IsContainedSandbox ? Path.Combine(DataDirectory, "logs") : Path.Combine(BaseDirectory, "logs")));

        if (int.TryParse(FirstNonEmpty(cliPort, Environment.GetEnvironmentVariable($"{envPrefix}_PORT"), Environment.GetEnvironmentVariable("PORT")), out int parsedPort) &&
            parsedPort is > 0 and <= 65535)
        {
            PortOverride = parsedPort;
        }
    }

    /// <summary>
    /// Fail-closed containment guard ensuring a candidate path resolves strictly inside an allowed root directory.
    /// </summary>
    public static string AssertPathContained(string candidatePath, string allowedRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(allowedRoot);

        string fullCandidate = Path.GetFullPath(StripExtendedPrefix(candidatePath))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullRoot = Path.GetFullPath(StripExtendedPrefix(allowedRoot))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(fullCandidate, fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            return fullCandidate;
        }

        string rootWithSep = fullRoot + Path.DirectorySeparatorChar;
        if (!fullCandidate.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Fail-Closed Sandbox Violation: Path '{fullCandidate}' escapes allowed root '{fullRoot}'.");
        }

        return fullCandidate;
    }

    public string AssertSandboxPathContained(string candidatePath)
    {
        return AssertPathContained(candidatePath, DataDirectory);
    }

    /// <summary>
    /// Resolves the shared <c>Sandbox\</c> ancestor directory when <paramref name="dataDir"/> sits inside
    /// a canonical <c>Sandbox\{app,data,logs,installed}</c> tree, returning <see langword="null"/> for host
    /// or non-sandbox paths so callers fail closed.
    /// </summary>
    public static string? ResolveSiblingSandboxRoot(string? dataDir)
    {
        if (string.IsNullOrWhiteSpace(dataDir))
        {
            return null;
        }

        string fullData = Path.GetFullPath(StripExtendedPrefix(dataDir))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        DirectoryInfo? current = new DirectoryInfo(fullData);
        while (current is not null)
        {
            if (current.Parent is not null &&
                string.Equals(current.Name, "Sandbox", StringComparison.OrdinalIgnoreCase))
            {
                string root = current.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (IsHostEscapeRoot(root))
                {
                    return null;
                }
                return root;
            }
            current = current.Parent;
        }

        return null;
    }

    private static bool IsHostEscapeRoot(string path)
    {
        string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (normalized.IndexOf(@"\Programs\", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        string[] protectedRoots =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        foreach (string protectedRoot in protectedRoots)
        {
            if (string.IsNullOrWhiteSpace(protectedRoot)) continue;
            string fullProtected = Path.GetFullPath(protectedRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(normalized, fullProtected, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(fullProtected + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string NormalizeLongPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(StripExtendedPrefix(path));
        if (OperatingSystem.IsWindows() && full.Length >= 248 && !full.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return full.StartsWith(@"\\", StringComparison.Ordinal)
                ? @"\\?\UNC\" + full[2..]
                : @"\\?\" + full;
        }
        return full;
    }

    private static string StripExtendedPrefix(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[8..];
        }
        if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
        {
            return path[4..];
        }
        return path;
    }

    private static bool HasFlag(IReadOnlyList<string> args, string flag)
    {
        for (int i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string? ReadFlagValue(IReadOnlyList<string> args, string flag)
    {
        string prefix = flag + "=";
        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg[prefix.Length..].Trim().Trim('"');
            }
            if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return args[i + 1].Trim().Trim('"');
            }
        }
        return null;
    }

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (string? candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }
        return null;
    }

    private static bool IsTruthyEnv(string name)
    {
        string? val = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(val))
        {
            return false;
        }
        string trimmed = val.Trim();
        return trimmed.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
