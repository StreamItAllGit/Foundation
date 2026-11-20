// @module: Foundation.UI.Branding
// @purpose: Configures per-app visual branding, sidebar layout, resizable terminal defaults, and declarative data table schemas.
// @depends-on: System, System.Collections.Generic
// @exports: TableColumnSchema, ShellBrandingOptions

using System;
using System.Collections.Generic;

namespace Foundation.UI;

/// <summary>
/// Declarative column mapping for UniversalDataTable, bound strictly to Standard 18 canonical keys and Title Case headers.
/// </summary>
public sealed record TableColumnSchema(
    string JsonKey,
    string TitleCaseHeader,
    bool IsNumericTabular = false,
    bool IsSortable = true,
    string? StatusBadgeMap = null
);

/// <summary>
/// Per-application customization contract for Foundation.UI's shared shell (Collapsible Sidebar, Docked Resizable Terminal,
/// SchemaSettingsRenderer, and UniversalDataTable).
/// </summary>
public sealed record ShellBrandingOptions
{
    /// <summary>Canonical application slug (Standard 18: appId).</summary>
    public required string AppId { get; init; }

    /// <summary>Application Title Case display name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Pinned Foundation SDK version (Standard 18: sdkVersion — Full SemVer MAJOR.MINOR.PATCH).</summary>
    public string SdkVersion { get; init; } = "0.5.3";

    /// <summary>Custom CSS brand accent color ("--brand-accent").</summary>
    public string BrandAccentHex { get; init; } = "#0EA5E9";

    /// <summary>Path or URI to the app's sidebar brand icon (.svg or .png).</summary>
    public string? BrandIconUri { get; init; }

    /// <summary>Whether the left vertical sidebar starts expanded (220px) or collapsed (56px).</summary>
    public bool SidebarInitiallyExpanded { get; init; } = true;

    /// <summary>Default docked terminal preset ("S" = 25%, "M" = 40%, "L" = 60%).</summary>
    public string DefaultTerminalPreset { get; init; } = "M";

    /// <summary>Whether to show the live drive free-space strip at the bottom of the left sidebar rail.</summary>
    public bool ShowDriveTelemetryStrip { get; init; } = true;
}
