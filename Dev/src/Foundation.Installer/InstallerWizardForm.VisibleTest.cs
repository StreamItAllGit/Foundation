// @module: Foundation.Installer.UI.Wizard.VisibleTest
// @purpose: Lifecycle overrides, DWM dark titlebar, foreground visible-test execution, screenshot capture, and Z-order composite bitmap rendering.
// @depends-on: System, System.Drawing, System.Drawing.Imaging, System.IO, System.Runtime.InteropServices, System.Threading.Tasks, System.Windows.Forms
// @exports: InstallerWizardForm (partial: CaptureWizardPreviewBitmap, CaptureVisibleTestScreenshot, Dispose, OnShown)

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Foundation.Installer;

public sealed partial class InstallerWizardForm
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private bool ShouldSuppressModalDialogs =>
        _autoRunVisibleTest || _showcaseAutoCloseMs > 0 || (_ctx?.IsContainedMode ?? false) || (_ctx?.Headless ?? false);

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) Invoke(action);
        else action();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TryApplyDarkModeTitleBar();
        UpdateLayoutBounds();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        UpdateLayoutBounds();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdateLayoutBounds();

        if (_autoRunVisibleTest || _showcaseAutoCloseMs > 0)
        {
            TopMost = true;
            CenterToScreen();
            Activate();
            BringToFront();
            if (IsHandleCreated) _ = SetForegroundWindow(Handle);
        }

        if (_autoRunVisibleTest && !_visibleTestStarted)
        {
            _visibleTestStarted = true;
            BeginInvoke(new Action(() => _ = ExecuteVisibleTestRunAsync()));
            return;
        }

        if (_showcaseAutoCloseMs > 0 && _showcaseTimer is null)
        {
            _showcaseTimer = new System.Windows.Forms.Timer { Interval = Math.Max(200, _showcaseAutoCloseMs) };
            _showcaseTimer.Tick += (_, _) =>
            {
                _showcaseTimer?.Stop();
                ExitCode = 0;
                Close();
            };
            _showcaseTimer.Start();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateLayoutBounds();
    }

    public Bitmap CaptureWizardPreviewBitmap()
    {
        if (!IsHandleCreated) CreateHandle();
        UpdateLayoutBounds();
        PerformLayout();

        int width = Math.Max(1, Width);
        int height = Math.Max(1, Height);
        var bmp = new Bitmap(width, height);
        DrawToBitmap(bmp, new Rectangle(0, 0, width, height));

        Point clientScreenOrigin = PointToScreen(Point.Empty);
        int offsetX = clientScreenOrigin.X - Bounds.X;
        int offsetY = clientScreenOrigin.Y - Bounds.Y;
        int fallbackBorder = Math.Max(0, (width - ClientSize.Width) / 2);
        if (offsetX < 0 || offsetX > width - ClientSize.Width) offsetX = fallbackBorder;
        if (offsetY < 0 || offsetY > height - ClientSize.Height) offsetY = Math.Max(0, height - ClientSize.Height - fallbackBorder);

        for (int i = Controls.Count - 1; i >= 0; i--)
        {
            Control child = Controls[i];
            if (!child.Visible || child.Dock == DockStyle.Fill || child.Width <= 0 || child.Height <= 0) continue;
            child.DrawToBitmap(bmp, new Rectangle(offsetX + child.Left, offsetY + child.Top, child.Width, child.Height));
        }
        return bmp;
    }

    public string? CaptureVisibleTestScreenshot(string? fileName = "installer-visible-test.png")
    {
        if (string.IsNullOrWhiteSpace(_ctx?.LogDirectory)) return null;
        try
        {
            string screenshotsDir = Path.Combine(_ctx.LogDirectory, "screenshots");
            Directory.CreateDirectory(screenshotsDir);
            string targetFile = Path.Combine(screenshotsDir, string.IsNullOrWhiteSpace(fileName) ? "installer-visible-test.png" : fileName);
            using Bitmap bmp = CaptureWizardPreviewBitmap();
            bmp.Save(targetFile, ImageFormat.Png);
            LastVisibleTestScreenshotPath = targetFile;
            _ctx.WriteLog("INFO", $"Saved visible test installer screenshot: {targetFile}");
            return targetFile;
        }
        catch (Exception ex)
        {
            _ctx.WriteLog("WARN", $"Failed to save visible test screenshot: {ex.Message}");
            return null;
        }
    }

    private async Task ExecuteVisibleTestRunAsync()
    {
        try
        {
            RunOnUiThread(() => { SetDetailsExpanded(true); Refresh(); });
            string targetDir = _txtInstallDir.Text.Trim();
            bool hasActionableWork = _isUninstallMode
                ? ((_fullEngine?.HasExistingInstallationArtifacts(targetDir) ?? false) || (_explicitInstallTargetProvided && Directory.Exists(targetDir)))
                : (_fullEngine?.HasPayloadSource(_externalPayloadPath) ?? _customInstallAction != null);

            if (hasActionableWork)
            {
                await OnPrimaryActionClickedAsync();
            }
            else
            {
                RunOnUiThread(() =>
                {
                    const string verifiedMsg = "Visible test layout verification passed (no embedded payload present).";
                    _progressBar.Style = ProgressBarStyle.Blocks;
                    _progressBar.Value = 100;
                    _lblStatus.Text = verifiedMsg;
                    _lblStatus.ForeColor = AccentEmerald;
                    AppendLogLine(verifiedMsg);
                    _ctx?.WriteLog("INFO", verifiedMsg);
                    ExitCode = 0;
                });
            }

            RunOnUiThread(() =>
            {
                Refresh();
                CaptureVisibleTestScreenshot();
            });
        }
        finally
        {
            RunOnUiThread(Close);
        }
    }

    private void TryApplyDarkModeTitleBar()
    {
        if (!OperatingSystem.IsWindows() || !IsHandleCreated) return;
        try
        {
            int useDark = 1;
            int hr = DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int));
            if (hr != 0) DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkModeBefore20H1, ref useDark, sizeof(int));
        }
        catch (Exception ex)
        {
            _ = ex; // Older Windows builds without DWM dark titlebar attribute continue with default frame.
        }
    }
}
