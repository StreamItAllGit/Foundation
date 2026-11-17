// @module: Foundation.Installer.ManifestStore
// @purpose: Loads Standard 40 schemaVersion=2 declarative installer.manifest.json descriptors and maps them onto InstallerAppDescriptor / InstallerThemeOptions.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Reflection, System.Text, System.Text.Json, System.Text.Json.Serialization
// @exports: InstallerMarkupManifest, InstallerManifestStore

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foundation.Installer;

public sealed record InstallerMarkupManifest
{
    [JsonPropertyName("$schema")] public string SchemaUri { get; init; } = "https://schemas.local/manifest.v2.json";
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; } = 2;
    [JsonPropertyName("manifestType")] public string ManifestType { get; init; } = InstallerManifestStore.CanonicalManifestType;
    [JsonPropertyName("appId")] public string AppId { get; init; } = string.Empty;
    [JsonPropertyName("version")] public string Version { get; init; } = string.Empty;
    [JsonPropertyName("updatedAtUtc")] public string UpdatedAtUtc { get; init; } = string.Empty;
    [JsonPropertyName("displayName")] public string? DisplayName { get; init; }
    [JsonPropertyName("appTitle")] public string? AppTitle { get; init; }
    [JsonPropertyName("publisher")] public string? Publisher { get; init; }
    [JsonPropertyName("subtitle")] public string? Subtitle { get; init; }
    [JsonPropertyName("studioName")] public string? StudioName { get; init; }
    [JsonPropertyName("suiteName")] public string? SuiteName { get; init; }
    [JsonPropertyName("umbrellaSubpath")] public string? UmbrellaSubpath { get; init; }
    [JsonPropertyName("mainExecutableRelativePath")] public string? MainExecutableRelativePath { get; init; }
    [JsonPropertyName("logoPath")] public string? LogoPath { get; init; }
    [JsonPropertyName("iconPath")] public string? IconPath { get; init; }
    [JsonPropertyName("theme")] public InstallerMarkupTheme? Theme { get; init; }
    [JsonPropertyName("layoutMode")] public string LayoutMode { get; init; } = "single-line";
    [JsonPropertyName("supportedProfiles")] public List<string> SupportedProfiles { get; init; } = new() { "lightweight", "full" };
    [JsonPropertyName("defaultProfile")] public string DefaultProfile { get; init; } = "lightweight";
    [JsonPropertyName("buttons")] public InstallerMarkupButtons? Buttons { get; init; }
    [JsonPropertyName("tooltips")] public Dictionary<string, InstallerMarkupTooltip>? Tooltips { get; init; }
    [JsonPropertyName("secondaryPickers")] public List<InstallerMarkupSecondaryPicker>? SecondaryPickers { get; init; }
    [JsonPropertyName("allowAutoStart")] public bool? AllowAutoStart { get; init; }
    [JsonPropertyName("allowLegacyMigration")] public bool? AllowLegacyMigration { get; init; }

    [JsonIgnore]
    public string EffectiveDisplayName =>
        FirstNonEmpty(DisplayName, AppTitle, AppId) ?? "Application";

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}

public static class InstallerManifestStore
{
    public const string DefaultManifestFileName = "installer.manifest.json";
    public const string CanonicalManifestType = "installer-manifest";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly HashSet<string> AllowedProfiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "lightweight", "full", "custom"
    };

    public static InstallerMarkupManifest LoadFromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        InstallerMarkupManifest manifest = JsonSerializer.Deserialize<InstallerMarkupManifest>(json, JsonOpts)
            ?? throw new InvalidDataException("installer.manifest.json deserialized to null.");

        if (manifest.SchemaVersion != 2)
            throw new InvalidDataException($"installer.manifest.json schemaVersion must be 2 (received {manifest.SchemaVersion}).");

        if (!string.Equals(manifest.ManifestType, CanonicalManifestType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"installer.manifest.json manifestType must be '{CanonicalManifestType}' (received '{manifest.ManifestType}').");

        if (string.IsNullOrWhiteSpace(manifest.AppId))
            throw new InvalidDataException("installer.manifest.json is missing required 'appId'.");

        string layout = NormalizeLayoutMode(manifest.LayoutMode);
        List<string> profiles = NormalizeSupportedProfiles(manifest.SupportedProfiles);
        string defProfile = NormalizeDefaultProfile(manifest.DefaultProfile, profiles);

        return manifest with
        {
            LayoutMode = layout,
            SupportedProfiles = profiles,
            DefaultProfile = defProfile
        };
    }

    public static InstallerMarkupManifest LoadFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string full = Path.GetFullPath(filePath);
        return LoadFromJson(File.ReadAllText(full, Encoding.UTF8));
    }

    public static InstallerMarkupManifest? TryLoadFromDirectoryOrAssembly(
        string? directory,
        Assembly? assembly,
        string fileName = DefaultManifestFileName)
    {
        if (!string.IsNullOrWhiteSpace(directory))
        {
            string candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
                return LoadFromFile(candidate);
        }

        if (assembly is not null)
        {
            string? resName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(resName))
            {
                using Stream? stream = assembly.GetManifestResourceStream(resName);
                if (stream is not null)
                {
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    return LoadFromJson(reader.ReadToEnd());
                }
            }
        }

        return null;
    }

    public static InstallerAppDescriptor ApplyToDescriptor(
        InstallerAppDescriptor baseDescriptor,
        InstallerMarkupManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(baseDescriptor);
        ArgumentNullException.ThrowIfNull(manifest);

        List<SecondaryPathPickerOptions>? pickers = manifest.SecondaryPickers?
            .Where(p => !string.IsNullOrWhiteSpace(p.FlagName) && !string.IsNullOrWhiteSpace(p.Label))
            .Select(p => new SecondaryPathPickerOptions(p.FlagName.Trim(), p.Label.Trim(), p.DefaultPath, p.Placeholder, p.Tooltip))
            .ToList();

        return baseDescriptor with
        {
            DisplayName = FirstNonEmpty(manifest.DisplayName, manifest.AppTitle) ?? baseDescriptor.DisplayName,
            Publisher = FirstNonEmpty(manifest.Publisher) ?? baseDescriptor.Publisher,
            Version = FirstNonEmpty(manifest.Version) ?? baseDescriptor.Version,
            MainExecutableRelativePath = FirstNonEmpty(manifest.MainExecutableRelativePath) ?? baseDescriptor.MainExecutableRelativePath,
            Subtitle = FirstNonEmpty(manifest.Subtitle) ?? baseDescriptor.Subtitle,
            BrandAccentHex = FirstNonEmpty(manifest.Theme?.Accent) ?? baseDescriptor.BrandAccentHex,
            AllowAutoStart = manifest.AllowAutoStart ?? baseDescriptor.AllowAutoStart,
            StudioName = FirstNonEmpty(manifest.StudioName) ?? baseDescriptor.StudioName,
            SuiteName = FirstNonEmpty(manifest.SuiteName) ?? baseDescriptor.SuiteName,
            UmbrellaSubpath = FirstNonEmpty(manifest.UmbrellaSubpath) ?? baseDescriptor.UmbrellaSubpath,
            DefaultProfile = manifest.DefaultProfile,
            AllowLegacyMigration = manifest.AllowLegacyMigration ?? baseDescriptor.AllowLegacyMigration,
            LayoutMode = manifest.LayoutMode,
            SupportedProfiles = manifest.SupportedProfiles,
            LogoPath = FirstNonEmpty(manifest.LogoPath) ?? baseDescriptor.LogoPath,
            IconPath = FirstNonEmpty(manifest.IconPath) ?? baseDescriptor.IconPath,
            SurfaceBackdropHex = FirstNonEmpty(manifest.Theme?.Background) ?? baseDescriptor.SurfaceBackdropHex,
            SurfaceCardHex = FirstNonEmpty(manifest.Theme?.Surface) ?? baseDescriptor.SurfaceCardHex,
            TextPrimaryHex = FirstNonEmpty(manifest.Theme?.Text) ?? baseDescriptor.TextPrimaryHex,
            BorderHex = FirstNonEmpty(manifest.Theme?.Border) ?? baseDescriptor.BorderHex,
            Buttons = manifest.Buttons ?? baseDescriptor.Buttons,
            Tooltips = manifest.Tooltips ?? baseDescriptor.Tooltips,
            SecondaryPickers = pickers is { Count: > 0 } ? pickers : baseDescriptor.SecondaryPickers
        };
    }

    public static InstallerThemeOptions ToThemeOptions(
        InstallerMarkupManifest manifest,
        string? defaultInstallDir = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        List<SecondaryPathPickerOptions>? pickers = manifest.SecondaryPickers?
            .Where(p => !string.IsNullOrWhiteSpace(p.FlagName) && !string.IsNullOrWhiteSpace(p.Label))
            .Select(p => new SecondaryPathPickerOptions(p.FlagName.Trim(), p.Label.Trim(), p.DefaultPath, p.Placeholder, p.Tooltip))
            .ToList();

        var profiles = manifest.SupportedProfiles
            .Select(p => new InstallerProfileOption(
                p,
                string.Equals(p, "full", StringComparison.OrdinalIgnoreCase) ? InstallerProfileKind.Full : InstallerProfileKind.Lightweight,
                char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant(),
                $"{p} installation footprint",
                string.Equals(p, manifest.DefaultProfile, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return new InstallerThemeOptions
        {
            AppId = manifest.AppId.Trim(),
            DisplayName = manifest.EffectiveDisplayName,
            Version = FirstNonEmpty(manifest.Version) ?? "0.1.0",
            Subtitle = FirstNonEmpty(manifest.Subtitle) ?? "Standalone & suite-compatible installation wizard.",
            BrandAccentHex = FirstNonEmpty(manifest.Theme?.Accent) ?? "#0EA5E9",
            SurfaceBackdropHex = FirstNonEmpty(manifest.Theme?.Background) ?? "#0E0E10",
            SurfaceCardHex = FirstNonEmpty(manifest.Theme?.Surface) ?? "#18181B",
            TextPrimaryHex = FirstNonEmpty(manifest.Theme?.Text) ?? "#ECF0F8",
            BorderHex = FirstNonEmpty(manifest.Theme?.Border) ?? "#2A3344",
            WindowIconResource = FirstNonEmpty(manifest.IconPath),
            HeaderLogoResource = FirstNonEmpty(manifest.LogoPath),
            DefaultInstallDir = defaultInstallDir ?? string.Empty,
            ExecutableFileName = !string.IsNullOrWhiteSpace(manifest.MainExecutableRelativePath)
                ? Path.GetFileName(manifest.MainExecutableRelativePath)
                : null,
            AllowAutoStart = manifest.AllowAutoStart ?? true,
            StudioName = FirstNonEmpty(manifest.StudioName) ?? "StreamItAll",
            SuiteName = FirstNonEmpty(manifest.SuiteName),
            UmbrellaSubpath = FirstNonEmpty(manifest.UmbrellaSubpath),
            AvailableProfiles = profiles,
            DefaultProfileId = manifest.DefaultProfile,
            SupportedProfiles = manifest.SupportedProfiles,
            LayoutMode = manifest.LayoutMode,
            Buttons = manifest.Buttons,
            Tooltips = manifest.Tooltips,
            SecondaryPathPicker = pickers?.FirstOrDefault(),
            SecondaryPickers = pickers,
            AllowLegacyMigration = manifest.AllowLegacyMigration ?? false
        };
    }

    public static string NormalizeLayoutMode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "single-line";
        string trimmed = raw.Trim().ToLowerInvariant();
        return trimmed switch
        {
            "single-line" => "single-line",
            "multi-line" => "multi-line",
            _ => throw new InvalidDataException($"Unsupported installer layoutMode '{raw}'. Expected 'single-line' or 'multi-line'.")
        };
    }

    public static List<string> NormalizeSupportedProfiles(IEnumerable<string>? rawProfiles)
    {
        var list = (rawProfiles ?? new[] { "lightweight", "full" })
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim().ToLowerInvariant())
            .Where(p => AllowedProfiles.Contains(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return list.Count > 0 ? list : new List<string> { "lightweight", "full" };
    }

    private static string NormalizeDefaultProfile(string? raw, List<string> supported)
    {
        string candidate = string.IsNullOrWhiteSpace(raw) ? "lightweight" : raw.Trim().ToLowerInvariant();
        return supported.Any(p => string.Equals(p, candidate, StringComparison.OrdinalIgnoreCase))
            ? candidate
            : supported[0];
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
