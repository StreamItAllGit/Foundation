// @module: Foundation.UI.Desktop.VisibleTestWindowHelper
// @purpose: Standard 02 visible-by-default desktop test window helper: centered TopMost foreground activation, title badge formatting, and full Z-order bitmap capture.
// @depends-on: Foundation.Core, System, System.Drawing, System.Drawing.Imaging, System.IO, System.Runtime.InteropServices, System.Windows.Forms
// @exports: VisibleTestWindowHelper

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Foundation.Core;

namespace Foundation.UI.Desktop;

public static class VisibleTestWindowHelper
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    public static string FormatWindowTitle(string baseTitle, bool visibleTest)
    {
        string normalized = string.IsNullOrWhiteSpace(baseTitle) ? "Application" : baseTitle.Trim();
        if (!visibleTest || normalized.Contains("[VISIBLE TEST]", StringComparison.OrdinalIgnoreCase))
            return normalized;

        return $"{normalized} [VISIBLE TEST]";
    }

    public static bool IsVisibleTestActive(SandboxEnvironment? sandbox) =>
        sandbox?.EffectiveVisibleTest ?? false;

    public static void ApplyForegroundVisibleTestWindow(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        form.StartPosition = FormStartPosition.CenterScreen;
        form.TopMost = true;

        if (!form.IsHandleCreated)
            return;

        Screen? activeScreen = Screen.FromControl(form) ?? Screen.PrimaryScreen;
        if (activeScreen != null)
        {
            Rectangle area = activeScreen.WorkingArea;
            int x = area.Left + Math.Max(0, (area.Width - form.Width) / 2);
            int y = area.Top + Math.Max(0, (area.Height - form.Height) / 2);
            form.Location = new Point(x, y);
        }

        form.Activate();
        form.BringToFront();
        if (OperatingSystem.IsWindows())
        {
            _ = BringWindowToTop(form.Handle);
            _ = SetForegroundWindow(form.Handle);
        }
    }

    public static Bitmap CaptureCompositeFormBitmap(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        _ = form.Handle;
        form.PerformLayout();

        int width = Math.Max(1, form.Width);
        int height = Math.Max(1, form.Height);
        var bmp = new Bitmap(width, height);
        form.DrawToBitmap(bmp, new Rectangle(0, 0, width, height));

        Point clientScreenOrigin = form.PointToScreen(Point.Empty);
        int offsetX = clientScreenOrigin.X - form.Bounds.X;
        int offsetY = clientScreenOrigin.Y - form.Bounds.Y;
        int fallbackBorder = Math.Max(0, (width - form.ClientSize.Width) / 2);
        if (offsetX < 0 || offsetX > width - form.ClientSize.Width)
            offsetX = fallbackBorder;
        if (offsetY < 0 || offsetY > height - form.ClientSize.Height)
            offsetY = Math.Max(0, height - form.ClientSize.Height - fallbackBorder);

        for (int i = form.Controls.Count - 1; i >= 0; i--)
        {
            Control child = form.Controls[i];
            if (!child.Visible || child.Dock == DockStyle.Fill || child.Width <= 0 || child.Height <= 0)
                continue;

            var childRect = new Rectangle(
                offsetX + child.Left,
                offsetY + child.Top,
                child.Width,
                child.Height);
            child.DrawToBitmap(bmp, childRect);
        }

        return bmp;
    }

    public static string? SaveScreenshot(Form form, string? logDirectory, string fileName)
    {
        if (form is null || string.IsNullOrWhiteSpace(logDirectory))
            return null;

        string screenshotsDir = Path.Combine(Path.GetFullPath(logDirectory), "screenshots");
        Directory.CreateDirectory(screenshotsDir);
        string safeName = string.IsNullOrWhiteSpace(fileName) ? "window-visible-test.png" : Path.GetFileName(fileName);
        string targetPath = Path.Combine(screenshotsDir, safeName);
        using Bitmap bmp = CaptureCompositeFormBitmap(form);
        bmp.Save(targetPath, ImageFormat.Png);
        return targetPath;
    }
}
