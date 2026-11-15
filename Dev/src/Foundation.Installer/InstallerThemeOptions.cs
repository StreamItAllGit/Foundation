// @module: Foundation.Installer.Theme
// @purpose: Provides per-application visual, icon, color, profile, and step customization for the 650x434 Dark WinForms Setup Wizard.
// @depends-on: System, System.Collections.Generic
// @exports: InstallerProfileKind, InstallerProfileOption, SecondaryPathPickerOptions, InstallerCustomToggle, InstallerThemeOptions

using System;
using System.Collections.Generic;

namespace Foundation.Installer;

/// <summary>
/// Supported installer bundle footprint profiles (Standard 18: installProfile).
/// </summary>
public enum InstallerProfileKind
{
    Lightweight,
    Full
}

/// <summary>
/// Selectable installation profile option presented in the setup wizard (e.g., Lightweight vs Full).
/// </summary>
public sealed record InstallerProfileOption(
    string Id,
    InstallerProfileKind Kind,
    string Label,
    string Description,
    bool IsDefault = false
);

/// <summary>
/// Optional secondary directory picker configuration for companion roots (e.g., --reengine-dir or --suite-dir).
/// </summary>
public sealed record SecondaryPathPickerOptions(
    string FlagName,
    string Label,
    string? DefaultPath = null,
    string? Placeholder = null,
    string? Tooltip = null
);

/// <summary>
/// Optional per-app checkbox toggle displayed on the setup wizard surface (e.g., "Register with Mother Suite", "Create Desktop Shortcut").
/// </summary>
public sealed record InstallerCustomToggle(
    string Id,
    string Label,
    string TooltipSummary,
    bool DefaultChecked = true
);

/// <summary>
/// Full visual and behavioral customization options for Foundation.Installer.dll so every app or game
/// can customize colors, icons, banners, and steps in under 30 lines of code without duplicating the wizard.
/// </summary>
public sealed record InstallerThemeOptions
{
    /// <summary>Canonical application identifier (Standard 18: appId, e.g., "housekeeping" or "shadowlibrary").</summary>
    public required string AppId { get; init; }

    /// <summary>Human-facing Title Case application name (e.g., "Housekeeping" or "ShadowLibrary").</summary>
    public required string DisplayName { get; init; }

    /// <summary>Active SemVer build version (Standard 18: version, e.g., "1.0.0-rc.14").</summary>
    public required string Version { get; init; }

    /// <summary>Sentence-case subtitle displayed beneath the wizard header.</summary>
    public string Subtitle { get; init; } = "Standalone & suite-compatible installation wizard.";

    /// <summary>Primary brand accent hex color (e.g., "#F59E0B" Amber for Housekeeping, "#6366F1" Indigo for ShadowLibrary).</summary>
    public string BrandAccentHex { get; init; } = "#0EA5E9";

    /// <summary>Primary window backdrop surface hex (defaults to Standard 09 surface-0 "#0E0E10").</summary>
    public string SurfaceBackdropHex { get; init; } = "#0E0E10";

    /// <summary>Raised card surface hex (defaults to Standard 09 surface-2 "#18181B").</summary>
    public string SurfaceCardHex { get; init; } = "#18181B";

    /// <summary>Optional path or embedded resource name for the custom .ico window/taskbar icon.</summary>
    public string? WindowIconResource { get; init; }

    /// <summary>Optional path or embedded resource name for the header emblem/logo image.</summary>
    public string? HeaderLogoResource { get; init; }

    /// <summary>Default canonical production target path.</summary>
    public required string DefaultInstallDir { get; init; }

    /// <summary>Primary launcher executable filename at the installed root (defaults to "&lt;DisplayName&gt;.exe").</summary>
    public string? ExecutableFileName { get; init; }

    /// <summary>Uninstaller executable filename at the installed root (defaults to "&lt;DisplayName&gt;-Uninstall.exe").</summary>
    public string? UninstallerFileName { get; init; }

    /// <summary>Whether to bundle and bootstrap the Shared Suite Daemon during install.</summary>
    public bool BootstrapSharedSuiteDaemon { get; init; } = true;

    /// <summary>Whether the application supports launching automatically at Windows sign-in.</summary>
    public bool AllowAutoStart { get; init; } = true;

    /// <summary>Customizable optional toggles presented to the user before installation starts.</summary>
    public IReadOnlyList<InstallerCustomToggle> CustomToggles { get; init; } = Array.Empty<InstallerCustomToggle>();

    /// <summary>Studio umbrella folder under %LOCALAPPDATA%\Programs\ (defaults to "StreamItAll").</summary>
    public string StudioName { get; init; } = "StreamItAll";

    /// <summary>Optional suite folder nested under StudioName (e.g., "Mother" or "reEngine").</summary>
    public string? SuiteName { get; init; }

    /// <summary>Optional explicit umbrella subpath relative to %LOCALAPPDATA%\Programs\ (Standard 18: umbrellaSubpath).</summary>
    public string? UmbrellaSubpath { get; init; }

    /// <summary>Available installation profiles presented to the user (e.g., Lightweight vs Full).</summary>
    public IReadOnlyList<InstallerProfileOption> AvailableProfiles { get; init; } = Array.Empty<InstallerProfileOption>();

    /// <summary>Default installation profile identifier (Standard 18: installProfile, defaults to "lightweight").</summary>
    public string DefaultProfileId { get; init; } = "lightweight";

    /// <summary>Optional secondary directory picker (e.g., --reengine-dir or --suite-dir).</summary>
    public SecondaryPathPickerOptions? SecondaryPathPicker { get; init; }

    /// <summary>Whether the wizard offers opt-in migration from legacy flat <PROGRAMS_ROOT>\ installations.</summary>
    public bool AllowLegacyMigration { get; init; } = false;
}
