// @module: Foundation.UI.Desktop.TrayIconBadgeHelper
// @purpose: System Tray NotifyIcon busy badge overlay generator, 63-char Win32 tooltip clamper, and default browser launcher.
// @depends-on: System, System.Diagnostics, System.Drawing
// @exports: TrayIconBadgeHelper

using System;
using System.Diagnostics;
using System.Drawing;

namespace Foundation.UI.Desktop;

public static class TrayIconBadgeHelper
{
    public static Icon? CreateBusyBadgeIcon(Icon baseIcon, Color? badgeColor = null)
    {
        try
        {
            Color fill = badgeColor ?? Color.FromArgb(255, 99, 102, 241);
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.DrawIcon(baseIcon, new Rectangle(0, 0, 32, 32));
                using var brush = new SolidBrush(fill);
                using var pen = new Pen(Color.White, 2f);
                g.FillEllipse(brush, 18, 18, 12, 12);
                g.DrawEllipse(pen, 18, 18, 12, 12);
            }
            IntPtr hIcon = bmp.GetHicon();
            return Icon.FromHandle(hIcon);
        }
        catch
        {
            return null;
        }
    }

    public static string ClampNotifyIconTooltip(string? rawTooltip)
    {
        if (string.IsNullOrEmpty(rawTooltip)) return "";
        return rawTooltip.Length > 63 ? rawTooltip[..63] : rawTooltip;
    }

    public static void OpenUrlInDefaultBrowser(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }
}
