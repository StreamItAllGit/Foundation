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

        if (renderProvider is not null && renderProvider.TryCaptureRgba(out int w, out int h, out byte[] rgba))
        {
            RgbaPngWriter.WritePng(fullPath, w, h, rgba);
            return new AutomationSnapshotResult(fullPath, w, h, new FileInfo(fullPath).Length, "png");
        }

        if (rootControl is null || rootControl.IsDisposed)
            throw new InvalidOperationException("No active control or render surface provider is available to snapshot.");

        (int width, int height) = Win32VirtualInputDriver.InvokeOnUiThread(rootControl, () =>
        {
            Control target = rootControl;
            if (!string.IsNullOrWhiteSpace(request.TargetId))
            {
                target = Win32VirtualInputDriver.FindControlById(rootControl, request.TargetId)
                    ?? throw new ArgumentException($"Target control '{request.TargetId}' was not found.", nameof(request));
            }

            if (!target.IsHandleCreated) target.CreateControl();
            target.PerformLayout();

            if (target is DisposableWebViewHostForm webHost)
            {
                using Bitmap webBmp = webHost.CapturePreviewBitmap();
                webBmp.Save(fullPath, ImageFormat.Png);
                return (webBmp.Width, webBmp.Height);
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

            bmp.Save(fullPath, ImageFormat.Png);
            return (bmpW, bmpH);
        });

        return new AutomationSnapshotResult(fullPath, width, height, new FileInfo(fullPath).Length, "png");
    }
}
