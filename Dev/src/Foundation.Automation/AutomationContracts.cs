// @module: Foundation.Automation.Contracts
// @purpose: WinForms-free opt-in loopback automation contracts, surface-target interface, virtual pointer/keyboard/snapshot DTOs, and Raylib/OpenGL RGBA snapshot callback.
// @depends-on: System, System.Collections.Generic, System.Text.Json.Serialization
// @exports: IRenderSurfaceSnapshotProvider, IAutomationSurfaceTarget, AutomationServerOptions, AutomationControlNode, AutomationPointerRequest, AutomationKeyboardRequest, AutomationSnapshotRequest, AutomationSnapshotResult, AutomationHealthPayload, AutomationResponse

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Foundation.Automation;

/// <summary>
/// Callback interface implemented by custom render surfaces (e.g., Raylib, OpenGL, Vulkan, SDL3)
/// so Foundation.Automation can snapshot framebuffers to PNG without taking an engine dependency.
/// </summary>
public interface IRenderSurfaceSnapshotProvider
{
    bool TryCaptureRgba(out int width, out int height, out byte[] rgbaBytes);
}

/// <summary>
/// WinForms-free UI/surface automation contract so SDL3, Avalonia, or cross-platform desktop hosts
/// can expose tree inspection, virtual input, and PNG capture without referencing WinForms.
/// </summary>
public interface IAutomationSurfaceTarget
{
    AutomationControlNode? InspectTree();
    bool ExecutePointer(AutomationPointerRequest request);
    bool ExecuteKeyboard(AutomationKeyboardRequest request);
    bool TryCapturePng(string? targetId, out byte[] pngBytes, out int width, out int height);
}

public sealed record AutomationServerOptions(
    int AutomationPort,
    string BearerToken,
    string AppId = "app",
    string? AllowedSnapshotRoot = null);

public sealed record AutomationControlNode(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("controlType")] string ControlType,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("isVisible")] bool IsVisible,
    [property: JsonPropertyName("isEnabled")] bool IsEnabled,
    [property: JsonPropertyName("children")] IReadOnlyList<AutomationControlNode> Children);

public sealed record AutomationPointerRequest(
    [property: JsonPropertyName("action")] string Action = "click",
    [property: JsonPropertyName("x")] int X = 0,
    [property: JsonPropertyName("y")] int Y = 0,
    [property: JsonPropertyName("toX")] int? ToX = null,
    [property: JsonPropertyName("toY")] int? ToY = null,
    [property: JsonPropertyName("button")] string Button = "left",
    [property: JsonPropertyName("deltaY")] int DeltaY = 0,
    [property: JsonPropertyName("targetId")] string? TargetId = null);

public sealed record AutomationKeyboardRequest(
    [property: JsonPropertyName("action")] string Action = "type",
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("key")] string? Key = null,
    [property: JsonPropertyName("modifiers")] IReadOnlyList<string>? Modifiers = null,
    [property: JsonPropertyName("targetId")] string? TargetId = null);

public sealed record AutomationSnapshotRequest(
    [property: JsonPropertyName("filePath")] string FilePath,
    [property: JsonPropertyName("snapshotFormat")] string SnapshotFormat = "png",
    [property: JsonPropertyName("targetId")] string? TargetId = null);

public sealed record AutomationSnapshotResult(
    [property: JsonPropertyName("filePath")] string FilePath,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("fileSizeBytes")] long FileSizeBytes,
    [property: JsonPropertyName("snapshotFormat")] string SnapshotFormat);

public sealed record AutomationHealthPayload(
    [property: JsonPropertyName("appId")] string AppId,
    [property: JsonPropertyName("automationPort")] int AutomationPort,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("isPassed")] bool IsPassed,
    [property: JsonPropertyName("generatedAtUtc")] string GeneratedAtUtc);

public sealed record AutomationResponse<T>(
    [property: JsonPropertyName("isPassed")] bool IsPassed,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage,
    [property: JsonPropertyName("data")] T? Data)
{
    public static AutomationResponse<T> Ok(T data) => new(true, "completed", null, data);
    public static AutomationResponse<T> Fail(string errorMessage) => new(false, "failed", errorMessage, default);
}
