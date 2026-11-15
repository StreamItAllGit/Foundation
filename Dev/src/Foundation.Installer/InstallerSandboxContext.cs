// @module: Foundation.Installer.InstallerSandboxContext
// @purpose: Fail-closed Standard 02 installer sandbox, CLI/env resolution, visible-by-default test flags, and diagnostic file logging.
// @depends-on: System, System.IO, System.Text, System.Text.Json
// @exports: InstallerSandboxContext

using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Foundation.Installer;

public sealed class InstallerSandboxContext
{
    public string EnvPrefix { get; }
    public string? DataDirectory { get; private set; }
    public string? SandboxRoot { get; private set; }
    public string? ConfigFilePath { get; }
    public string? LogDirectory { get; private set; }
    public bool TestMode { get; }
    public bool DryRun { get; }
    public bool Headless { get; }
    public bool ExplicitVisibleTest { get; }
    public bool VisibleTest { get; }
    public bool EffectiveVisibleTest => VisibleTest && !Headless;
    public InstallerConfigOverride? ConfigOverride { get; }

    public bool IsContainedMode => TestMode || !string.IsNullOrWhiteSpace(DataDirectory);

    public InstallerSandboxContext(
        string? cliDataDir = null,
        string? cliConfigPath = null,
        string? cliLogDir = null,
        bool testMode = false,
        bool dryRun = false,
        bool headless = false,
        string envPrefix = "APP",
        string? fallbackTestInstallDir = null,
        bool visibleTest = false,
        string? cliSandboxRoot = null)
    {
        EnvPrefix = string.IsNullOrWhiteSpace(envPrefix) ? "APP" : envPrefix.Trim().ToUpperInvariant();

        string? rawData = FirstNonEmpty(
            cliDataDir,
            Environment.GetEnvironmentVariable($"{EnvPrefix}_DATA_DIR"),
            Environment.GetEnvironmentVariable("DATA_DIR"),
            Environment.GetEnvironmentVariable("APP_HOME"));

        DataDirectory = !string.IsNullOrWhiteSpace(rawData) ? Path.GetFullPath(rawData) : null;

        string? rawSandboxRoot = FirstNonEmpty(
            cliSandboxRoot,
            Environment.GetEnvironmentVariable($"{EnvPrefix}_SANDBOX_ROOT"),
            Environment.GetEnvironmentVariable("SANDBOX_ROOT"));

        SandboxRoot = !string.IsNullOrWhiteSpace(rawSandboxRoot)
            ? Path.GetFullPath(rawSandboxRoot)
            : ResolveSiblingSandboxRoot(DataDirectory);

        string? rawConfig = FirstNonEmpty(
            cliConfigPath,
            Environment.GetEnvironmentVariable($"{EnvPrefix}_CONFIG"),
            Environment.GetEnvironmentVariable("CONFIG_PATH"));

        ConfigFilePath = !string.IsNullOrWhiteSpace(rawConfig) ? Path.GetFullPath(rawConfig) : null;

        string? rawLog = FirstNonEmpty(
            cliLogDir,
            Environment.GetEnvironmentVariable($"{EnvPrefix}_LOG_DIR"),
            Environment.GetEnvironmentVariable("LOG_DIR"));

        LogDirectory = !string.IsNullOrWhiteSpace(rawLog)
            ? Path.GetFullPath(rawLog)
            : (!string.IsNullOrWhiteSpace(DataDirectory) ? Path.Combine(DataDirectory, "logs") : null);

        TestMode = testMode || IsTruthyEnv($"{EnvPrefix}_TEST_MODE") || IsTruthyEnv("TEST_MODE");
        DryRun = dryRun || IsTruthyEnv($"{EnvPrefix}_DRY_RUN");
        Headless = headless || IsTruthyEnv($"{EnvPrefix}_HEADLESS") || IsTruthyEnv("HEADLESS");
        ExplicitVisibleTest = visibleTest || IsTruthyEnv($"{EnvPrefix}_VISIBLE_TEST") || IsTruthyEnv("VISIBLE_TEST");
        VisibleTest = ExplicitVisibleTest || (TestMode && !Headless);

        if (TestMode && string.IsNullOrWhiteSpace(DataDirectory) && IsSafeTestInstallDirectory(fallbackTestInstallDir))
        {
            TryAdoptSafeTestDataDirectory(fallbackTestInstallDir!);
        }

        if (!string.IsNullOrWhiteSpace(ConfigFilePath) && File.Exists(ConfigFilePath))
        {
            string json = File.ReadAllText(ConfigFilePath, Encoding.UTF8);
            ConfigOverride = JsonSerializer.Deserialize<InstallerConfigOverride>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
    }

    public static bool IsSafeTestInstallDirectory(string? candidateDir) =>
        FileLockRetryHelper.IsSafeTestInstallDirectory(candidateDir);

    public bool TryAdoptSafeTestDataDirectory(string candidateInstallDir)
    {
        if (!TestMode || !string.IsNullOrWhiteSpace(DataDirectory) || !IsSafeTestInstallDirectory(candidateInstallDir))
            return false;

        DataDirectory = Path.GetFullPath(candidateInstallDir);
        if (string.IsNullOrWhiteSpace(SandboxRoot))
            SandboxRoot = ResolveSiblingSandboxRoot(DataDirectory);
        if (string.IsNullOrWhiteSpace(LogDirectory))
            LogDirectory = Path.Combine(DataDirectory, "logs");
        return true;
    }

    private static string? ResolveSiblingSandboxRoot(string? dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
            return null;

        string fullData = Path.GetFullPath(dataDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string folderName = Path.GetFileName(fullData);
        if (string.Equals(folderName, "data", StringComparison.OrdinalIgnoreCase))
        {
            string? parent = Path.GetDirectoryName(fullData);
            if (!string.IsNullOrWhiteSpace(parent) && IsSafeTestInstallDirectory(parent))
            {
                return parent;
            }
        }

        return fullData;
    }

    public void EnsureDefaultLogDirectory(string installDir)
    {
        if (string.IsNullOrWhiteSpace(LogDirectory) && !string.IsNullOrWhiteSpace(installDir))
        {
            LogDirectory = Path.Combine(Path.GetFullPath(installDir), "logs");
        }
    }

    public void WriteLog(string level, string message)
    {
        if (DryRun || string.IsNullOrWhiteSpace(LogDirectory))
            return;

        try
        {
            Directory.CreateDirectory(LogDirectory);
            string logFile = Path.Combine(LogDirectory, "installer.log");
            string line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
            File.AppendAllText(logFile, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex)
        {
            _ = ex; // Best-effort diagnostic logging must never crash installer execution.
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (string? v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }
        return null;
    }

    private static bool IsTruthyEnv(string name)
    {
        string? v = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(v)) return false;
        v = v.Trim();
        return v.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
