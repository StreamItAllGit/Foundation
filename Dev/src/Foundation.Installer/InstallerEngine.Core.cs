// @module: Foundation.Installer.InstallerEngine.Core
// @purpose: Parameterized installer descriptor, config overrides, manifest DTOs, fail-closed sandbox context, and default target path resolution.
// @depends-on: System, System.Collections.Generic, System.IO, System.Reflection, System.Text, System.Text.Json, Microsoft.Win32
// @exports: InstallerAppDescriptor, InstallerConfigOverride, InstallManifest, InstallerOperationResult, InstallerSandboxContext, InstallerEngine

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Foundation.Installer;

public sealed record InstallerAppDescriptor(
    string ProductName,
    string DisplayName,
    string Publisher,
    string Version,
    string MainExecutableRelativePath = "",
    string? Subtitle = null,
    string? EmbeddedPayloadResourceName = null,
    string? BrandAccentHex = null,
    bool AllowAutoStart = true,
    string StudioName = "StreamItAll",
    string? SuiteName = null,
    string? UmbrellaSubpath = null,
    string DefaultProfile = "lightweight")
{
    public string EffectiveMainExecutableRelativePath =>
        !string.IsNullOrWhiteSpace(MainExecutableRelativePath)
            ? MainExecutableRelativePath
            : Path.Combine("app", $"{ProductName}.exe");

    public string MainExecutableFileName =>
        Path.GetFileName(EffectiveMainExecutableRelativePath);

    public string MainProcessStem =>
        Path.GetFileNameWithoutExtension(MainExecutableFileName);

    public string UninstallerExeName => $"{ProductName}-Uninstall.exe";

    public string EffectiveEmbeddedPayloadResource =>
        !string.IsNullOrWhiteSpace(EmbeddedPayloadResourceName)
            ? EmbeddedPayloadResourceName!
            : $"{ProductName}.Installer.Embedded.payload.zip";

    public string WindowsUninstallRegistrySubKey =>
        $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{ProductName}";

    public string EnvPrefix =>
        ProductName.Trim().ToUpperInvariant().Replace('-', '_').Replace(' ', '_');

    public string EffectiveSubtitle =>
        !string.IsNullOrWhiteSpace(Subtitle)
            ? Subtitle!
            : $"Clean Release Root ({MainExecutableFileName} + app\\, scripts\\, data\\)";

    public string CanonicalDefaultInstallDir => ResolveCanonicalInstallDir();

    private string ResolveCanonicalInstallDir()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData",
                "Local");
        }

        string programsRoot = Path.Combine(localAppData, "Programs");
        if (!string.IsNullOrWhiteSpace(UmbrellaSubpath))
        {
            string trimmed = UmbrellaSubpath.Trim().Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string[] segments = trimmed.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 0)
            {
                string combined = Path.Combine(new[] { programsRoot }.Concat(segments).ToArray());
                return string.Equals(segments[^1], ProductName, StringComparison.OrdinalIgnoreCase)
                    ? combined
                    : Path.Combine(combined, ProductName);
            }
        }

        string effectiveStudio = string.IsNullOrWhiteSpace(StudioName) ? "StreamItAll" : StudioName.Trim();
        return !string.IsNullOrWhiteSpace(SuiteName)
            ? Path.Combine(programsRoot, effectiveStudio, SuiteName.Trim(), ProductName)
            : Path.Combine(programsRoot, effectiveStudio, ProductName);
    }
}

public sealed class InstallerConfigOverride
{
    [JsonPropertyName("install_dir")]
    public string? InstallDirectory { get; set; }

    [JsonPropertyName("create_shortcuts")]
    public bool? CreateShortcuts { get; set; }

    [JsonPropertyName("autostart")]
    public bool? AutoStart { get; set; }

    [JsonPropertyName("install_profile")]
    public string? InstallProfile { get; set; }

    [JsonPropertyName("reengine_install_dir")]
    public string? ReEngineInstallDir { get; set; }

    [JsonPropertyName("suite_install_dir")]
    public string? SuiteInstallDir { get; set; }

    [JsonPropertyName("migrate_legacy")]
    public bool? MigrateLegacy { get; set; }
}

public sealed class InstallManifest
{
    [JsonPropertyName("product_name")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("installed_at_utc")]
    public DateTimeOffset InstalledAtUtc { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("install_directory")]
    public string InstallDirectory { get; set; } = string.Empty;

    [JsonPropertyName("payload_sha256")]
    public string PayloadSha256 { get; set; } = string.Empty;

    [JsonPropertyName("installed_relative_files")]
    public List<string> InstalledRelativeFiles { get; set; } = new();

    [JsonPropertyName("installed_top_directories")]
    public List<string> InstalledTopDirectories { get; set; } = new();

    [JsonPropertyName("created_shortcuts")]
    public List<string> CreatedShortcuts { get; set; } = new();

    [JsonPropertyName("uninstall_registry_record")]
    public string? UninstallRegistryRecord { get; set; }

    [JsonPropertyName("install_profile")]
    public string? InstallProfile { get; set; }

    [JsonPropertyName("reengine_install_dir")]
    public string? ReEngineInstallDir { get; set; }

    [JsonPropertyName("suite_install_dir")]
    public string? SuiteInstallDir { get; set; }

    [JsonPropertyName("umbrella_subpath")]
    public string? UmbrellaSubpath { get; set; }
}

public sealed record InstallerOperationResult(
    bool Success,
    string InstallDirectory,
    string PayloadSha256,
    int FileCount,
    string Message,
    bool WasRunningBeforeInstall = false,
    string? BackupArchivePath = null);

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
    public bool VisibleTest { get; }
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
        Headless = headless || IsTruthyEnv($"{EnvPrefix}_HEADLESS");
        VisibleTest = visibleTest || IsTruthyEnv($"{EnvPrefix}_VISIBLE_TEST") || IsTruthyEnv("VISIBLE_TEST");

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
        catch { }
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

public sealed partial class InstallerEngine
{
    public const string InstallManifestFileName = "install-manifest.json";
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

    private readonly InstallerSandboxContext _ctx;
    private readonly Assembly _payloadAssembly;

    public InstallerAppDescriptor Descriptor { get; }
    public InstallerSandboxContext Context => _ctx;
    public string InstallerVersion => Descriptor.Version;
    public string UninstallerExeName => Descriptor.UninstallerExeName;

    public InstallerEngine(
        InstallerAppDescriptor descriptor,
        InstallerSandboxContext ctx,
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
        catch
        {
            return false;
        }
    }

    public string ResolveDefaultInstallDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_ctx.ConfigOverride?.InstallDirectory))
            return Path.GetFullPath(_ctx.ConfigOverride.InstallDirectory);

        if (!string.IsNullOrWhiteSpace(_ctx.DataDirectory))
            return Path.GetFullPath(Path.Combine(_ctx.DataDirectory, "installed-app"));

        string? envInstallDir = Environment.GetEnvironmentVariable($"{Descriptor.EnvPrefix}_INSTALL_DIR");
        if (!string.IsNullOrWhiteSpace(envInstallDir))
            return Path.GetFullPath(envInstallDir.Trim());

        if (!_ctx.IsContainedMode && OperatingSystem.IsWindows())
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(Descriptor.WindowsUninstallRegistrySubKey, writable: false);
                string? regLoc = key?.GetValue("InstallLocation") as string;
                if (!string.IsNullOrWhiteSpace(regLoc) && Directory.Exists(regLoc.Trim()))
                    return Path.GetFullPath(regLoc.Trim());
            }
            catch { }
        }

        return Path.GetFullPath(Descriptor.CanonicalDefaultInstallDir);
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

        if (_ctx.IsContainedMode)
        {
            if (string.IsNullOrWhiteSpace(_ctx.DataDirectory) && !string.IsNullOrWhiteSpace(explicitInstallDir))
            {
                _ctx.TryAdoptSafeTestDataDirectory(resolved);
            }

            if (string.IsNullOrWhiteSpace(_ctx.DataDirectory))
            {
                throw new InvalidOperationException(
                    $"Fail-closed sandbox violation: --test-mode requires --data-dir, {Descriptor.EnvPrefix}_DATA_DIR, DATA_DIR, APP_HOME, or an explicit --install-dir inside Sandbox\\ or Temp.");
            }

            string containmentBoundary = !string.IsNullOrWhiteSpace(_ctx.SandboxRoot)
                ? _ctx.SandboxRoot!
                : _ctx.DataDirectory!;
            EnsureWithinDirectory(containmentBoundary, resolved);
        }

        _ctx.EnsureDefaultLogDirectory(resolved);
        return resolved;
    }
}
