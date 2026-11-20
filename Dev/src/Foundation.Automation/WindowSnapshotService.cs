// @module: Foundation.Automation.WindowSnapshotService
// @purpose: Offscreen PNG capture for WinForms controls, DisposableWebViewHostForm, and IRenderSurfaceSnapshotProvider (Raylib/OpenGL RGBA framebuffers).
// @depends-on: System, System.Drawing, System.Drawing.Imaging, System.IO, System.Runtime.InteropServices, System.Windows.Forms, Foundation.Core, Foundation.UI.Desktop
// @exports: WindowSnapshotService

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Foundation.Core;
using Foundation.UI.Desktop;

namespace Foundation.Automation;

/// <summary>
/// Captures WinForms, WebView2, or custom RGBA render surfaces offscreen into PNG files
/// without stealing host foreground focus.
/// </summary>
public static class WindowSnapshotService
{
    private const uint PwRenderFullContent = 0x00000002;

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    public static AutomationSnapshotResult CaptureSnapshot(
        Control? rootControl,
        IRenderSurfaceSnapshotProvider? renderProvider,
        AutomationSnapshotRequest request,
        string? allowedSnapshotRoot = null,
        SandboxResolution? sandbox = null)
    {
        string fullPath = PrepareAndContainSnapshotPath(request, allowedSnapshotRoot, sandbox);
        if (renderProvider is not null && renderProvider.TryCaptureRgba(out int w, out int h, out byte[] rgba))
        {
            RgbaPngWriter.WritePng(fullPath, w, h, rgba);
            return new AutomationSnapshotResult(fullPath, w, h, new FileInfo(fullPath).Length, "png");
        }

        (byte[] pngBytes, int width, int height) = CaptureControlPngBytes(rootControl, request.TargetId);
        File.WriteAllBytes(fullPath, pngBytes);
        return new AutomationSnapshotResult(fullPath, width, height, pngBytes.LongLength, "png");
    }

    public static AutomationSnapshotResult CaptureSnapshot(
        IAutomationSurfaceTarget? surfaceTarget,
        IRenderSurfaceSnapshotProvider? renderProvider,
        AutomationSnapshotRequest request,
        string? allowedSnapshotRoot = null,
        SandboxResolution? sandbox = null)
    {
        string fullPath = PrepareAndContainSnapshotPath(request, allowedSnapshotRoot, sandbox);
        (byte[] pngBytes, int width, int height) = CaptureSnapshotPngBytes(surfaceTarget, renderProvider, request.TargetId);
        File.WriteAllBytes(fullPath, pngBytes);
        return new AutomationSnapshotResult(fullPath, width, height, pngBytes.LongLength, "png");
    }

    private static string PrepareAndContainSnapshotPath(
        AutomationSnapshotRequest request,
        string? allowedSnapshotRoot,
        SandboxResolution? sandbox)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FilePath);

        string format = string.IsNullOrWhiteSpace(request.SnapshotFormat)
            ? "png"
            : request.SnapshotFormat.Trim().ToLowerInvariant();
        if (format != "png")
            throw new ArgumentException($"Unsupported snapshotFormat '{request.SnapshotFormat}'. Only 'png' is supported.");

        string fullPath = Path.GetFullPath(request.FilePath.Trim());
        SandboxResolution res = sandbox ?? SandboxEnvironmentSeam.ResolveCurrent();
        if (res.IsSandboxActive)
            fullPath = SandboxEnvironmentSeam.AssertSandboxPathIfActive(fullPath, res);
        if (!string.IsNullOrWhiteSpace(allowedSnapshotRoot))
            fullPath = AppEnvironment.AssertPathContained(fullPath, allowedSnapshotRoot);

        string? parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);
        return fullPath;
    }

    public static (byte[] PngBytes, int Width, int Height) CaptureSnapshotPngBytes(
        Control? rootControl,
        IRenderSurfaceSnapshotProvider? renderProvider,
        string? targetId = null)
    {
        if (renderProvider is not null && renderProvider.TryCaptureRgba(out int w, out int h, out byte[] rgba))
            return (RgbaPngWriter.Encode(w, h, rgba), w, h);

        return CaptureControlPngBytes(rootControl, targetId);
    }

    public static (byte[] PngBytes, int Width, int Height) CaptureSnapshotPngBytes(
        IAutomationSurfaceTarget? surfaceTarget,
        IRenderSurfaceSnapshotProvider? renderProvider,
        string? targetId = null)
    {
        if (surfaceTarget is not null && surfaceTarget.TryCapturePng(targetId, out byte[] pngBytes, out int sw, out int sh))
            return (pngBytes, sw, sh);

        if (renderProvider is not null && renderProvider.TryCaptureRgba(out int w, out int h, out byte[] rgba))
            return (RgbaPngWriter.Encode(w, h, rgba), w, h);

        throw new InvalidOperationException("No active automation surface target or render surface provider is available to snapshot.");
    }

    private static (byte[] PngBytes, int Width, int Height) CaptureControlPngBytes(Control? rootControl, string? targetId)
    {
        if (rootControl is null || rootControl.IsDisposed)
            throw new InvalidOperationException("No active control or render surface provider is available to snapshot.");

        return Win32VirtualInputDriver.InvokeOnUiThread(rootControl, () =>
        {
            Control target = rootControl;
            if (!string.IsNullOrWhiteSpace(targetId))
            {
                target = Win32VirtualInputDriver.FindControlById(rootControl, targetId)
                    ?? throw new ArgumentException($"Target control '{targetId}' was not found.", nameof(targetId));
            }

            if (!target.IsHandleCreated) target.CreateControl();
            target.PerformLayout();

            using var ms = new MemoryStream();
            if (target is DisposableWebViewHostForm webHost)
            {
                using Bitmap webBmp = webHost.CapturePreviewBitmap();
                webBmp.Save(ms, ImageFormat.Png);
                return (ms.ToArray(), webBmp.Width, webBmp.Height);
            }

            int bmpW = Math.Max(1, target.Width);
            int bmpH = Math.Max(1, target.Height);
            using var bmp = new Bitmap(bmpW, bmpH, PixelFormat.Format32bppArgb);
            bool captured = false;
            if (target.Visible && target.IsHandleCreated)
            {
                using Graphics g = Graphics.FromImage(bmp);
                IntPtr hdc = g.GetHdc();
                try { captured = PrintWindow(target.Handle, hdc, PwRenderFullContent); }
                finally { g.ReleaseHdc(hdc); }
            }

            if (!captured)
            {
                target.DrawToBitmap(bmp, new Rectangle(0, 0, bmpW, bmpH));
            }

            bmp.Save(ms, ImageFormat.Png);
            return (ms.ToArray(), bmpW, bmpH);
        });
    }
}
