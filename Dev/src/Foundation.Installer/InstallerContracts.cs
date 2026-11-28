// @module: Foundation.Installer.InstallerContracts
// @purpose: Parameterized installer descriptor, config overrides, install options, manifest DTOs, and host run result contracts.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Text.Json.Serialization, Foundation.Core
// @exports: InstallerAppDescriptor, InstallerConfigOverride, InstallManifest, InstallerOperationResult, InstallerInstallOptions, InstallerHostRunResult

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using Foundation.Core;

namespace Foundation.Installer;

public static class InstallerDeliveryKinds
{
    public const string OnlineSetup = "onlineSetup";
    public const string OfflineSetup = "offlineSetup";
    public const string PortableZip = "portableZip";
}

public sealed record InstallerPayloadAsset(
    [property: JsonPropertyName("installProfile")] string InstallProfile,
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("expectedSizeBytes")] long ExpectedSizeBytes,
    [property: JsonPropertyName("expectedSha256")] string ExpectedSha256,
    [property: JsonPropertyName("relativeSubdir")] string? RelativeSubdir = null);

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
    public string LayoutMode { get; init; } = "single-line";
    public IReadOnlyList<string>? SupportedProfiles { get; init; }
    public string? LogoPath { get; init; }
    public string? IconPath { get; init; }
    public string? SurfaceBackdropHex { get; init; }
    public string? SurfaceCardHex { get; init; }
    public string? TextPrimaryHex { get; init; }
    public string? BorderHex { get; init; }
    public InstallerMarkupButtons? Buttons { get; init; }
    public IReadOnlyDictionary<string, InstallerMarkupTooltip>? Tooltips { get; init; }
    public IReadOnlyList<SecondaryPathPickerOptions>? SecondaryPickers { get; init; }
    public string InstallerDeliveryKind { get; init; } = InstallerDeliveryKinds.OfflineSetup;
    public string? PayloadDownloadUrl { get; init; }
    public IReadOnlyList<string>? PayloadFallbackUrls { get; init; }
    public string? BetaPin { get; init; }
    public IReadOnlyList<InstallerPayloadAsset>? PayloadAssets { get; init; }

    public bool IsOnlineSetup =>
        string.Equals(InstallerDeliveryKind, InstallerDeliveryKinds.OnlineSetup, StringComparison.OrdinalIgnoreCase);

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

    public string CanonicalDefaultInstallDir => ResolveCanonicalDefaultInstallDir();

    public string GetEffectiveUmbrellaSubpath()
    {
        if (!string.IsNullOrWhiteSpace(UmbrellaSubpath))
        {
            string trimmed = UmbrellaSubpath.Trim().Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string[] segments = trimmed.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 0)
            {
                bool hasProductOrVersionLeaf =
                    segments.Any(s => string.Equals(s, ProductName, StringComparison.OrdinalIgnoreCase)) ||
                    IsVersionLeafSegment(segments[^1]);
                return hasProductOrVersionLeaf
                    ? Path.Combine(segments)
                    : Path.Combine(segments.Concat(new[] { ProductName }).ToArray());
            }
        }

        string effectiveStudio = string.IsNullOrWhiteSpace(StudioName) ? "StreamItAll" : StudioName.Trim();
        return !string.IsNullOrWhiteSpace(SuiteName)
            ? Path.Combine(effectiveStudio, SuiteName.Trim(), ProductName)
            : Path.Combine(effectiveStudio, ProductName);
    }

    internal static bool IsVersionLeafSegment(string? segment) =>
        !string.IsNullOrWhiteSpace(segment) &&
        segment.Length >= 2 &&
        (segment[0] == 'v' || segment[0] == 'V') &&
        char.IsAsciiDigit(segment[1]);

    public string ResolveCanonicalDefaultInstallDir(SandboxResolution? sandbox = null)
    {
        string localAppData = KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, sandbox);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.Combine(
                KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.UserProfile, sandbox),
                "AppData",
                "Local");
        }

        string programsRoot = Path.Combine(localAppData, "Programs");
        return Path.Combine(programsRoot, GetEffectiveUmbrellaSubpath());
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

    [JsonPropertyName("installer_delivery_kind")]
    public string? InstallerDeliveryKind { get; set; }
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
    bool MigrateLegacy = false)
{
    public string? OfflineBundlePath { get; init; }
}

public sealed record InstallerHostRunResult(
    int ExitCode,
    string? LogDirectory = null,
    InstallerOperationResult? OperationResult = null);

public sealed record InstallerMarkupTheme(
    [property: JsonPropertyName("background")] string? Background = null,
    [property: JsonPropertyName("surface")] string? Surface = null,
    [property: JsonPropertyName("accent")] string? Accent = null,
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("border")] string? Border = null);

public sealed record InstallerMarkupButtons(
    [property: JsonPropertyName("installText")] string? InstallText = null,
    [property: JsonPropertyName("upgradeText")] string? UpgradeText = null,
    [property: JsonPropertyName("uninstallText")] string? UninstallText = null,
    [property: JsonPropertyName("cancelText")] string? CancelText = null);

public sealed record InstallerMarkupTooltip(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description)
{
    public string FormatTwoLine()
    {
        string t = Title?.Trim() ?? string.Empty;
        string d = Description?.Trim() ?? string.Empty;
        return string.IsNullOrEmpty(t) ? d : (string.IsNullOrEmpty(d) ? t : $"{t}{Environment.NewLine}{d}");
    }
}

public sealed record InstallerMarkupSecondaryPicker(
    [property: JsonPropertyName("flagName")] string FlagName,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("defaultPath")] string? DefaultPath = null,
    [property: JsonPropertyName("placeholder")] string? Placeholder = null,
    [property: JsonPropertyName("tooltip")] string? Tooltip = null);
