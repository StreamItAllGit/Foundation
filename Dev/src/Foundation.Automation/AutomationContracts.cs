// @module: Foundation.Automation.Contracts
// @purpose: Opt-in loopback automation contracts, virtual pointer/keyboard/snapshot DTOs, Raylib/OpenGL RGBA snapshot callback, and CLI bootstrapper.
// @depends-on: System, System.Collections.Generic, System.Globalization, System.Security.Cryptography, System.Text.Json.Serialization, System.Windows.Forms
// @exports: IRenderSurfaceSnapshotProvider, AutomationServerOptions, AutomationControlNode, AutomationPointerRequest, AutomationKeyboardRequest, AutomationSnapshotRequest, AutomationSnapshotResult, AutomationHealthPayload, AutomationResponse, AutomationCliHelper

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace Foundation.Automation;

/// <summary>
/// Callback interface implemented by custom render surfaces (e.g., Raylib, OpenGL, Vulkan)
/// so Foundation.Automation can snapshot framebuffers to PNG without taking an engine dependency.
/// </summary>
public interface IRenderSurfaceSnapshotProvider
{
    bool TryCaptureRgba(out int width, out int height, out byte[] rgbaBytes);
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

public static class AutomationCliHelper
{
    public static AutomationLoopbackServer? TryCreateFromArgs(
        string appId,
        string[]? args,
        Control? rootControl,
        IRenderSurfaceSnapshotProvider? renderProvider = null,
        string? allowedSnapshotRoot = null)
    {
        args ??= Array.Empty<string>();
        int? port = null;
        string? token = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i]?.Trim() ?? string.Empty;
            if (arg.Equals("--automation-port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length &&
                int.TryParse(args[i + 1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                port = parsed;
                i++;
            }
            else if (arg.Equals("--automation-token", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                token = args[i + 1].Trim();
                i++;
            }
        }

        if (!port.HasValue || port.Value < 0 || port.Value > 65535)
            return null;

        if (string.IsNullOrWhiteSpace(token))
        {
            token = Environment.GetEnvironmentVariable("FOUNDATION_AUTOMATION_TOKEN")?.Trim();
        }
        if (string.IsNullOrWhiteSpace(token))
        {
            token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        }

        var options = new AutomationServerOptions(
            AutomationPort: port.Value,
            BearerToken: token,
            AppId: string.IsNullOrWhiteSpace(appId) ? "app" : appId.Trim(),
            AllowedSnapshotRoot: allowedSnapshotRoot);
        return new AutomationLoopbackServer(options, rootControl, renderProvider);
    }
}
