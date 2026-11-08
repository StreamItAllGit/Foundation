// @module: Foundation.Core.Contracts
// @purpose: Defines the unified contract for Hybrid Folder Slices and .dll Plugin Modules plus declarative settings schemas.
// @depends-on: System, System.Collections.Generic, System.Threading, System.Threading.Tasks
// @exports: ModuleType, SchemaSettingDescriptor, IFoundationModule

namespace Foundation.Core.Contracts;

/// <summary>
/// Distinguishes lightweight in-assembly folder slices from compiled external .dll plugin modules (Standard 18: moduleType).
/// </summary>
public enum ModuleType
{
    FolderSlice,
    DllModule
}

/// <summary>
/// Declarative setting definition allowing Foundation.UI's SchemaSettingsRenderer to build forms automatically without editing SettingsPage.
/// </summary>
public sealed record SchemaSettingDescriptor(
    string Key,
    string Label,
    string InputType,
    object? DefaultValue,
    string TooltipTitle,
    string TooltipBody,
    string? Hotkey = null,
    double? MinValue = null,
    double? MaxValue = null,
    IReadOnlyList<string>? AllowedOptions = null
);

/// <summary>
/// Unified plug-in contract implemented by both lightweight Folder Slices (Features\&lt;Slice&gt;\)
/// and compiled external .dll modules (app\modules\*.dll) for zero-surgery extensibility.
/// </summary>
public interface IFoundationModule
{
    /// <summary>Canonical lowercase-kebab-case module identifier (Standard 18: moduleId).</summary>
    string ModuleId { get; }

    /// <summary>Human-facing Title Case display name (Standard 18: displayName).</summary>
    string DisplayName { get; }

    /// <summary>Whether this module is an in-assembly folder slice or an external .dll plugin (Standard 18: moduleType).</summary>
    ModuleType ModuleType { get; }

    /// <summary>Navigation category and icon identifier for automatic sidebar rail population.</summary>
    string Category { get; }
    string IconName { get; }

    /// <summary>Declarative settings owned by this module, rendered automatically by SchemaSettingsRenderer.</summary>
    IReadOnlyList<SchemaSettingDescriptor> SettingsSchema { get; }

    /// <summary>Executes the module's headless CLI or background worker action with mandatory CancellationToken propagation.</summary>
    Task<int> ExecuteCliAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default);
}
