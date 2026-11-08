// @module: Foundation.UI.Desktop.StaNativeDialogBridgeBase
// @purpose: Hidden STA WinForms bridge for marshaling native Windows folder and file pickers from Kestrel HTTP handlers.
// @depends-on: System, System.Drawing, System.IO, System.Runtime.InteropServices, System.Threading.Tasks, System.Windows.Forms
// @exports: StaNativeDialogBridgeBase

using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Foundation.UI.Desktop;

public class StaNativeDialogBridgeBase : Form
{
    private readonly string _appDisplayName;
    private readonly Icon? _appIcon;
    private readonly Func<string?>? _fallbackDirectoryProvider;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    public StaNativeDialogBridgeBase(string appDisplayName = "Application", Icon? appIcon = null, Func<string?>? fallbackDirectoryProvider = null)
    {
        _appDisplayName = string.IsNullOrWhiteSpace(appDisplayName) ? "Application" : appDisplayName;
        _appIcon = appIcon;
        _fallbackDirectoryProvider = fallbackDirectoryProvider;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Minimized;
        Opacity = 0;
        Size = new Size(1, 1);
        _ = Handle;
    }

    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);

    public string? PickPath(bool pickFile, string? initialDirectory)
    {
        if (IsDisposed || !IsHandleCreated) return null;
        if (!InvokeRequired) return RunDialogOnSta(pickFile, initialDirectory);

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            BeginInvoke(new Action(() =>
            {
                try { tcs.TrySetResult(RunDialogOnSta(pickFile, initialDirectory)); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            }));

            if (tcs.Task.Wait(TimeSpan.FromMinutes(5)))
                return tcs.Task.GetAwaiter().GetResult();
        }
        catch { return null; }
        return null;
    }

    private string? RunDialogOnSta(bool pickFile, string? initialDirectory)
    {
        string? fallbackDir = null;
        try { fallbackDir = _fallbackDirectoryProvider?.Invoke(); } catch { }
        string? resolvedDir = ResolveNearestExistingDirectory(initialDirectory, fallbackDir);

        using var owner = new Form
        {
            Text = pickFile ? $"{_appDisplayName} — Select File" : $"{_appDisplayName} — Select Folder",
            TopMost = true,
            ShowInTaskbar = true,
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedToolWindow,
            WindowState = FormWindowState.Normal,
            Size = new Size(1, 1),
            Opacity = 0.01
        };
        if (_appIcon != null) owner.Icon = _appIcon;

        owner.Show();
        ForceForegroundWindow(owner.Handle);
        owner.BringToFront();
        owner.Activate();

        try
        {
            if (pickFile)
            {
                using var ofd = new OpenFileDialog
                {
                    Title = $"{_appDisplayName} — Select File",
                    CheckFileExists = true,
                    RestoreDirectory = true
                };
                if (!string.IsNullOrWhiteSpace(resolvedDir)) ofd.InitialDirectory = resolvedDir;
                if (!string.IsNullOrWhiteSpace(initialDirectory) && File.Exists(initialDirectory))
                    ofd.FileName = Path.GetFileName(initialDirectory);

                return ofd.ShowDialog(owner) == DialogResult.OK ? ofd.FileName : null;
            }
            else
            {
                using var fbd = new FolderBrowserDialog
                {
                    Description = $"{_appDisplayName} — Select Folder",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = true
                };
                if (!string.IsNullOrWhiteSpace(resolvedDir))
                {
                    fbd.InitialDirectory = resolvedDir;
                    fbd.SelectedPath = resolvedDir;
                }

                return fbd.ShowDialog(owner) == DialogResult.OK ? fbd.SelectedPath : null;
            }
        }
        finally
        {
            try { owner.Close(); } catch { }
        }
    }

    public static void ForceForegroundWindow(IntPtr targetHwnd)
    {
        uint fgThread = 0;
        uint currentThread = GetCurrentThreadId();
        bool attached = false;
        try
        {
            IntPtr fgHwnd = GetForegroundWindow();
            if (fgHwnd != IntPtr.Zero)
            {
                fgThread = GetWindowThreadProcessId(fgHwnd, out _);
                if (fgThread != 0 && fgThread != currentThread)
                    attached = AttachThreadInput(currentThread, fgThread, true);
            }
            BringWindowToTop(targetHwnd);
            SetForegroundWindow(targetHwnd);
        }
        catch { }
        finally
        {
            if (attached && fgThread != 0)
            {
                try { AttachThreadInput(currentThread, fgThread, false); } catch { }
            }
        }
    }

    public static string? ResolveNearestExistingDirectory(string? candidatePath, string? fallbackDirectory = null)
    {
        string? validFallback = !string.IsNullOrWhiteSpace(fallbackDirectory) && Directory.Exists(fallbackDirectory)
            ? Path.GetFullPath(fallbackDirectory)
            : null;

        if (string.IsNullOrWhiteSpace(candidatePath)) return validFallback;
        string current = candidatePath.Trim().Trim('"');
        try
        {
            if (File.Exists(current))
            {
                string? parent = Path.GetDirectoryName(current);
                if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                    return Path.GetFullPath(parent);
            }

            while (!string.IsNullOrWhiteSpace(current))
            {
                if (Directory.Exists(current)) return Path.GetFullPath(current);
                current = Path.GetDirectoryName(current) ?? "";
            }
        }
        catch { }
        return validFallback;
    }
}
