// @module: Foundation.UI.Desktop.DisposableWebViewHostForm
// @purpose: Base dark-themed WinForms desktop shell window with Win11 DWM immersive dark titlebar and child browser disposal.
// @depends-on: System, System.Drawing, System.Runtime.InteropServices, System.Windows.Forms
// @exports: DisposableWebViewHostForm

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

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

        Text = windowTitle;
        Width = defaultWidth;
        Height = defaultHeight;
        MinimumSize = new Size(960, 620);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(9, 14, 23);
        ForeColor = Color.FromArgb(241, 245, 249);
        if (appIcon != null)
            Icon = appIcon;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        EnableImmersiveDarkTitleBar(Handle);
    }

    public static void EnableImmersiveDarkTitleBar(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            int useDark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
        }
        catch { }
    }
}
