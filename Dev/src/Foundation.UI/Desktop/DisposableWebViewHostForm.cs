// @module: Foundation.UI.Desktop.DisposableWebViewHostForm
// @purpose: Base dark-themed WinForms desktop shell window with Win11 DWM immersive dark titlebar and composite bitmap capture.
// @depends-on: Foundation.Core, System, System.Drawing, System.Runtime.InteropServices, System.Windows.Forms
// @exports: DisposableWebViewHostForm

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Foundation.Core;

namespace Foundation.UI.Desktop;

public class DisposableWebViewHostForm : Form
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public string TargetUrl { get; }
    public string UserDataFolder { get; }

    public DisposableWebViewHostForm(
        string targetUrl,
        string userDataFolder,
        string windowTitle = "Application",
        Icon? appIcon = null,
        int defaultWidth = 1440,
        int defaultHeight = 920)
    {
        TargetUrl = targetUrl;
        UserDataFolder = userDataFolder;

        Text = string.IsNullOrWhiteSpace(windowTitle) ? "Application" : windowTitle.Trim();
        Width = defaultWidth;
        Height = defaultHeight;
        MinimumSize = new Size(960, 620);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(9, 14, 23);
        ForeColor = Color.FromArgb(241, 245, 249);
        if (appIcon != null)
        {
            Icon = appIcon;
        }
    }

    public DisposableWebViewHostForm(
        string targetUrl,
        string userDataFolder,
        AppEnvironment? environment,
        string windowTitle = "Application",
        Icon? appIcon = null,
        int defaultWidth = 1440,
        int defaultHeight = 920)
        : this(targetUrl, userDataFolder, windowTitle, appIcon, defaultWidth, defaultHeight)
    {
        _ = environment;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        EnableImmersiveDarkTitleBar(Handle);
    }

    public Bitmap CapturePreviewBitmap()
    {
        if (!IsHandleCreated)
        {
            CreateControl();
        }
        PerformLayout();
        int width = Math.Max(1, ClientSize.Width);
        int height = Math.Max(1, ClientSize.Height);
        var bmp = new Bitmap(width, height);
        DrawToBitmap(bmp, new Rectangle(0, 0, width, height));
        return bmp;
    }

    public static void EnableImmersiveDarkTitleBar(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            int useDark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
        }
        catch (Exception ex)
        {
            _ = ex; // Systems without DWM dark titlebar support proceed with default window frame.
        }
    }
}
