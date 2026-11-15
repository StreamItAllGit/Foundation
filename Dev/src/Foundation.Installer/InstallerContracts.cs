// @module: Foundation.Installer.InstallerContracts
// @purpose: Parameterized installer descriptor, config overrides, install options, manifest DTOs, and host run result contracts.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Text.Json.Serialization
// @exports: InstallerAppDescriptor, InstallerConfigOverride, InstallManifest, InstallerOperationResult, InstallerInstallOptions, InstallerHostRunResult

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

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
    string DefaultProfile = "lightweight",
    bool AllowLegacyMigration = false)
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

    [JsonPropertyName("migrate_legacy")]
    public bool MigrateLegacy { get; set; }
}

public sealed record InstallerOperationResult(
    bool Success,
    string InstallDirectory,
    string PayloadSha256,
    int FileCount,
    string Message,
    bool WasRunningBeforeInstall = false,
    string? BackupArchivePath = null);

public sealed record InstallerInstallOptions(
    string? ExplicitInstallDir = null,
    bool CreateShortcuts = true,
    bool RegisterAutoStart = false,
    string? ExternalPayloadPath = null,
    string? InstallProfile = null,
    string? SecondaryInstallDir = null,
    string? SecondaryFlagName = null,
    bool MigrateLegacy = false);

public sealed record InstallerHostRunResult(
    int ExitCode,
    string? LogDirectory = null,
    InstallerOperationResult? OperationResult = null);
