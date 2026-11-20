// @module: Foundation.Automation.CliHelper
// @purpose: Parses --automation-port / --automation-token CLI flags and adapts WinForms Control hosts to IAutomationSurfaceTarget.
// @depends-on: System, System.Globalization, System.Security.Cryptography, System.Windows.Forms, Foundation.Core
// @exports: WinFormsAutomationSurfaceTarget, AutomationCliHelper

using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Windows.Forms;
using Foundation.Core;

namespace Foundation.Automation;

/// <summary>
/// Wraps a WinForms root <see cref="Control"/> and optional <see cref="IRenderSurfaceSnapshotProvider"/>
/// as a WinForms-agnostic <see cref="IAutomationSurfaceTarget"/>.
/// </summary>
public sealed class WinFormsAutomationSurfaceTarget : IAutomationSurfaceTarget
{
    private readonly Control? _rootControl;
    private readonly IRenderSurfaceSnapshotProvider? _renderProvider;

    public WinFormsAutomationSurfaceTarget(
        Control? rootControl,
        IRenderSurfaceSnapshotProvider? renderProvider = null)
    {
        _rootControl = rootControl;
        _renderProvider = renderProvider;
    }

    public AutomationControlNode? InspectTree() =>
        Win32VirtualInputDriver.InspectTree(_rootControl);

    public bool ExecutePointer(AutomationPointerRequest request) =>
        Win32VirtualInputDriver.ExecutePointer(_rootControl, request);

    public bool ExecuteKeyboard(AutomationKeyboardRequest request) =>
        Win32VirtualInputDriver.ExecuteKeyboard(_rootControl, request);

    public bool TryCapturePng(string? targetId, out byte[] pngBytes, out int width, out int height)
    {
        if (_renderProvider is not null && _renderProvider.TryCaptureRgba(out int w, out int h, out byte[] rgba))
        {
            pngBytes = RgbaPngWriter.Encode(w, h, rgba);
            width = w;
            height = h;
            return true;
        }

        if (_rootControl is null || _rootControl.IsDisposed)
        {
            pngBytes = Array.Empty<byte>();
            width = 0;
            height = 0;
            return false;
        }

        (pngBytes, width, height) = WindowSnapshotService.CaptureSnapshotPngBytes(_rootControl, _renderProvider, targetId);
        return pngBytes.Length > 0;
    }
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
        AutomationServerOptions? options = ParseServerOptions(appId, args, allowedSnapshotRoot);
        return options is null ? null : new AutomationLoopbackServer(options, rootControl, renderProvider);
    }

    public static AutomationLoopbackServer? TryCreateForSurface(
        string appId,
        string[]? args,
        IAutomationSurfaceTarget? surfaceTarget,
        IRenderSurfaceSnapshotProvider? renderProvider = null,
        string? allowedSnapshotRoot = null)
    {
        AutomationServerOptions? options = ParseServerOptions(appId, args, allowedSnapshotRoot);
        return options is null ? null : new AutomationLoopbackServer(options, surfaceTarget, renderProvider);
    }

    private static AutomationServerOptions? ParseServerOptions(string appId, string[]? args, string? allowedSnapshotRoot)
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

        return new AutomationServerOptions(
            AutomationPort: port.Value,
            BearerToken: token,
            AppId: string.IsNullOrWhiteSpace(appId) ? "app" : appId.Trim(),
            AllowedSnapshotRoot: allowedSnapshotRoot);
    }
}
