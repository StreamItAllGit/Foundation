// @module: Foundation.Installer.InstallerRuntimeContext
// @purpose: Resolves installer CLI/env runtime path overrides (--data-dir, --config, --log-dir), 3-mode sandbox/isolated precedence, IPlatformRegistry, and diagnostic logging.
// @depends-on: System, System.IO, System.Text, System.Text.Json, Foundation.Core
// @exports: InstallerRuntimeContext

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Foundation.Core;

namespace Foundation.Installer;

public sealed class InstallerRuntimeContext
{
    public string EnvPrefix { get; }
    public string? DataDirectory { get; private set; }
    public string? ConfigFilePath { get; }
    public string? LogDirectory { get; private set; }
    public bool DryRun { get; }
    public bool IsIsolatedTestProcess { get; }
    public SandboxResolution Sandbox { get; }
    public bool IsSandboxActive => Sandbox.IsSandboxActive && !IsIsolatedTestProcess;
    internal SandboxResolution EffectiveSandbox => IsSandboxActive ? Sandbox : SandboxResolution.Inactive;
    public bool IsStage1Isolated => IsIsolatedTestProcess || (!IsSandboxActive && !string.IsNullOrWhiteSpace(DataDirectory));
    public bool ShouldSuppressHostMutations => IsStage1Isolated;
    public bool ShouldSuppressLiveHostMutations => IsStage1Isolated || IsSandboxActive;
    public IPlatformRegistry Registry { get; }
    public InstallerConfigOverride? ConfigOverride { get; }

    public InstallerRuntimeContext(
        string? cliDataDir = null,
        string? cliConfigPath = null,
        string? cliLogDir = null,
        bool dryRun = false,
        string envPrefix = "APP")
        : this(cliDataDir, cliConfigPath, cliLogDir, dryRun, envPrefix, sandboxOverride: null, registryOverride: null)
    {
    }

    public InstallerRuntimeContext(
        string? cliDataDir,
        string? cliConfigPath,
        string? cliLogDir,
        bool dryRun,
        string envPrefix,
        SandboxResolution? sandboxOverride,
        IPlatformRegistry? registryOverride = null)
    {
        EnvPrefix = string.IsNullOrWhiteSpace(envPrefix) ? "APP" : envPrefix.Trim().ToUpperInvariant();
        IsIsolatedTestProcess = IsTruthyEnv("APP_ISOLATED_TEST");
        Sandbox = sandboxOverride ?? SandboxEnvironmentSeam.ResolveCurrent();
        if (IsSandboxActive)
            SandboxEnvironmentSeam.ValidateResolution(Sandbox);

        string? rawData = FirstNonEmpty(
            cliDataDir,
            Environment.GetEnvironmentVariable($"{EnvPrefix}_DATA_DIR"),
            Environment.GetEnvironmentVariable("DATA_DIR"),
            Environment.GetEnvironmentVariable("APP_HOME"));

        DataDirectory = !string.IsNullOrWhiteSpace(rawData) ? Path.GetFullPath(rawData) : null;

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

        if (IsSandboxActive)
        {
            if (!string.IsNullOrWhiteSpace(DataDirectory))
                DataDirectory = SandboxEnvironmentSeam.AssertSandboxPathIfActive(DataDirectory, Sandbox);
            if (!string.IsNullOrWhiteSpace(ConfigFilePath))
                ConfigFilePath = SandboxEnvironmentSeam.AssertSandboxPathIfActive(ConfigFilePath, Sandbox);
            if (!string.IsNullOrWhiteSpace(LogDirectory))
                LogDirectory = SandboxEnvironmentSeam.AssertSandboxPathIfActive(LogDirectory, Sandbox);
        }

        DryRun = dryRun || IsTruthyEnv($"{EnvPrefix}_DRY_RUN");
        Registry = registryOverride ?? PlatformRegistryFactory.CreateDefault(EffectiveSandbox, ShouldSuppressHostMutations);

        if (!string.IsNullOrWhiteSpace(ConfigFilePath) && File.Exists(ConfigFilePath))
        {
            string json = File.ReadAllText(ConfigFilePath, Encoding.UTF8);
            ConfigOverride = JsonSerializer.Deserialize(json, FoundationInstallerJsonContext.Default.InstallerConfigOverride);
        }
    }

    public static bool IsSafeIsolatedInstallDirectory(string? candidateDir) =>
        FileLockRetryHelper.IsSafeIsolatedInstallDirectory(candidateDir);

    public static bool IsSafeIsolatedInstallDirectory(string? candidateDir, SandboxResolution? sandbox = null) =>
        FileLockRetryHelper.IsSafeIsolatedInstallDirectory(candidateDir, sandbox);

    public void EnsureDefaultLogDirectory(string installDir)
    {
        if (string.IsNullOrWhiteSpace(LogDirectory) && !string.IsNullOrWhiteSpace(installDir))
        {
            string resolvedLog = Path.Combine(Path.GetFullPath(installDir), "logs");
            LogDirectory = IsSandboxActive
                ? SandboxEnvironmentSeam.AssertSandboxPathIfActive(resolvedLog, Sandbox)
                : resolvedLog;
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
