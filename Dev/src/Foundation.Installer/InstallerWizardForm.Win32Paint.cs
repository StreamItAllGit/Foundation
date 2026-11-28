// @module: Foundation.Installer.UI.Wizard.Win32Paint
// @purpose: Win32 message dispatch, GDI background/control coloring, owner-drawn buttons, DPI coordinate scaling, and handle cleanup for InstallerWizardForm.
// @depends-on: System, System.Collections.Generic, System.IO, System.Runtime.InteropServices
// @exports: InstallerWizardForm (partial: HandleMessage, PaintSurfaceBackground, DrawOwnerButton, ResizeAndCenterWindow)

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Foundation.Installer;

public sealed partial class InstallerWizardForm
{
    private void SetNativeWindowTitle(string value)
    {
        _windowText = value ?? string.Empty;
        if (_hwnd != IntPtr.Zero)
            _ = Win32Native.SetWindowTextW(_hwnd, _windowText);
    }

    private void SetNativeClientSize(WizardSize next)
    {
        _clientSize = next;
        if (_hwnd != IntPtr.Zero)
            ResizeWindowPreservingPosition(next.Width, next.Height);
    }

    private void SetProgressMarquee(bool enabled)
    {
        _progressBar.IsMarquee = enabled;
        if (_progressBar.NativeHwnd != IntPtr.Zero)
        {
            int curStyle = (int)GetWindowLongPtrW(_progressBar.NativeHwnd, Win32Native.GWL_STYLE);
            int nextStyle = enabled ? (curStyle | Win32Native.PBS_MARQUEE) : (curStyle & ~Win32Native.PBS_MARQUEE);
            _ = SetWindowLongPtrW(_progressBar.NativeHwnd, Win32Native.GWL_STYLE, (IntPtr)nextStyle);
            _ = Win32Native.SendMessageW(_progressBar.NativeHwnd, Win32Native.PBM_SETMARQUEE, (UIntPtr)(enabled ? 1 : 0), (IntPtr)30);
        }
    }

    private void SetProgressValue(int value)
    {
        _progressBar.Value = value;
        if (_progressBar.NativeHwnd != IntPtr.Zero)
            _ = Win32Native.SendMessageW(_progressBar.NativeHwnd, Win32Native.PBM_SETPOS, (UIntPtr)_progressBar.Value, IntPtr.Zero);
    }

    private void SyncAllNativeControlBounds()
    {
        if (_hwnd == IntPtr.Zero) return;
        foreach (KeyValuePair<IntPtr, WizardControl> kv in _controlsByHwnd)
            MoveNativeControl(kv.Value);
        _ = Win32Native.InvalidateRect(_hwnd, IntPtr.Zero, true);
    }

    private int Scale(int dip) => (int)Math.Round(dip * _dpi / 96.0);

    private (int X, int Y) ResolveAbsoluteDipPosition(WizardControl ctrl)
    {
        int x = ctrl.Left;
        int y = ctrl.Top;
        if (ctrl.Parent is not null)
        {
            x += ctrl.Parent.Left;
            y += ctrl.Parent.Top;
        }
        return (x, y);
    }

    private void MoveNativeControl(WizardControl ctrl)
    {
        if (ctrl.NativeHwnd == IntPtr.Zero) return;
        (int x, int y) = ResolveAbsoluteDipPosition(ctrl);
        _ = Win32Native.MoveWindow(ctrl.NativeHwnd, Scale(x), Scale(y), Scale(Math.Max(1, ctrl.Width)), Scale(Math.Max(1, ctrl.Height)), true);
        _ = Win32Native.ShowWindow(ctrl.NativeHwnd, ctrl.Visible ? Win32Native.SW_SHOW : Win32Native.SW_HIDE);
        _ = Win32Native.EnableWindow(ctrl.NativeHwnd, ctrl.Enabled);
        _ = Win32Native.InvalidateRect(ctrl.NativeHwnd, IntPtr.Zero, true);
    }

    private void OnControlPropertyChanged(WizardControl ctrl, string propertyName)
    {
        if (ctrl.NativeHwnd == IntPtr.Zero) return;
        switch (propertyName)
        {
            case nameof(WizardControl.Text):
                _ = Win32Native.SetWindowTextW(ctrl.NativeHwnd, ctrl.Text);
                _ = Win32Native.InvalidateRect(ctrl.NativeHwnd, IntPtr.Zero, true);
                break;
            case nameof(WizardControl.Visible):
                _ = Win32Native.ShowWindow(ctrl.NativeHwnd, ctrl.Visible ? Win32Native.SW_SHOW : Win32Native.SW_HIDE);
                break;
            case nameof(WizardControl.Enabled):
                _ = Win32Native.EnableWindow(ctrl.NativeHwnd, ctrl.Enabled);
                _ = Win32Native.InvalidateRect(ctrl.NativeHwnd, IntPtr.Zero, true);
                break;
            case nameof(WizardControl.Location):
            case nameof(WizardControl.Size):
                MoveNativeControl(ctrl);
                break;
            case nameof(WizardControl.ForeColor):
            case nameof(WizardControl.BackColor):
            case nameof(WizardControl.BorderColor):
                _ = Win32Native.InvalidateRect(ctrl.NativeHwnd, IntPtr.Zero, true);
                break;
        }
    }

    private IntPtr HandleMessage(IntPtr hwnd, uint msg, UIntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WmAppDispatch:
                while (_uiQueue.TryDequeue(out Action? act)) act();
                return IntPtr.Zero;

            case Win32Native.WM_ERASEBKGND:
                PaintSurfaceBackground((IntPtr)(ulong)wParam, hwnd);
                return (IntPtr)1;

            case Win32Native.WM_CTLCOLORSTATIC:
            case Win32Native.WM_CTLCOLORBTN:
            {
                IntPtr hdc = (IntPtr)(ulong)wParam;
                if (_controlsByHwnd.TryGetValue(lParam, out WizardControl? ctrl))
                {
                    _ = Win32Native.SetTextColor(hdc, ctrl.ForeColor.ToColorRef());
                    bool onCard = ctrl.Parent is not null;
                    _ = Win32Native.SetBkColor(hdc, (ctrl is WizardTextBox ? BgInput : (onCard ? _bgCard : _bgPrimary)).ToColorRef());
                    _ = Win32Native.SetBkMode(hdc, Win32Native.OPAQUE_BK);
                    return ctrl is WizardTextBox ? _brushInput : (onCard ? _brushCard : _brushBg);
                }
                return _brushBg;
            }

            case Win32Native.WM_CTLCOLOREDIT:
            {
                IntPtr hdc = (IntPtr)(ulong)wParam;
                _ = Win32Native.SetTextColor(hdc, _textPrimary.ToColorRef());
                _ = Win32Native.SetBkColor(hdc, BgInput.ToColorRef());
                return _brushInput;
            }

            case Win32Native.WM_DRAWITEM:
                DrawOwnerButton(lParam);
                return (IntPtr)1;

            case Win32Native.WM_COMMAND:
                HandleWmCommand(wParam);
                return IntPtr.Zero;

            case Win32Native.WM_CLOSE:
                OnCancelClicked(this, EventArgs.Empty);
                return IntPtr.Zero;

            case Win32Native.WM_DESTROY:
                ActiveWindows.TryRemove(hwnd, out _);
                DisposeNativeHandles();
                Win32Native.PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return Win32Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void PaintSurfaceBackground(IntPtr hdc, IntPtr hwnd)
    {
        if (!Win32Native.GetClientRect(hwnd, out Win32Native.RECT client)) return;
        _ = Win32Native.FillRect(hdc, ref client, _brushBg);

        var hdr = new Win32Native.RECT { Left = 0, Top = 0, Right = client.Right, Bottom = Scale(_headerPanel.Height) };
        _ = Win32Native.FillRect(hdc, ref hdr, _brushCard);

        var card = new Win32Native.RECT
        {
            Left = Scale(_optionsPanel.Left),
            Top = Scale(_optionsPanel.Top),
            Right = Scale(_optionsPanel.Right),
            Bottom = Scale(_optionsPanel.Bottom)
        };
        _ = Win32Native.FillRect(hdc, ref card, _brushCard);
        IntPtr borderBrush = Win32Native.CreateSolidBrush(_borderSubtle.ToColorRef());
        try { _ = Win32Native.FrameRect(hdc, ref card, borderBrush); }
        finally { _ = Win32Native.DeleteObject(borderBrush); }

        var ftr = new Win32Native.RECT { Left = 0, Top = Scale(_footerPanel.Top), Right = client.Right, Bottom = client.Bottom };
        _ = Win32Native.FillRect(hdc, ref ftr, _brushCard);
    }

    private void DrawOwnerButton(IntPtr lParam)
    {
        var dis = Marshal.PtrToStructure<Win32Native.DRAWITEMSTRUCT>(lParam);
        if (!_controlsByHwnd.TryGetValue(dis.hwndItem, out WizardControl? ctrl) || ctrl is not WizardButton btn)
            return;

        bool disabled = (dis.itemState & Win32Native.ODS_DISABLED) != 0;
        bool pressed = (dis.itemState & Win32Native.ODS_SELECTED) != 0;
        WizardColor back = btn.BackColor == WizardColor.Transparent ? _bgPrimary : btn.BackColor;
        if (pressed) back = WizardColor.FromRgb(Math.Max(0, back.R - 18), Math.Max(0, back.G - 18), Math.Max(0, back.B - 18));

        IntPtr hbr = Win32Native.CreateSolidBrush(back.ToColorRef());
        try { _ = Win32Native.FillRect(dis.hDC, ref dis.rcItem, hbr); }
        finally { _ = Win32Native.DeleteObject(hbr); }

        if (btn.BorderColor.HasValue && btn.BorderSize > 0)
        {
            IntPtr hBorder = Win32Native.CreateSolidBrush(btn.BorderColor.Value.ToColorRef());
            try { _ = Win32Native.FrameRect(dis.hDC, ref dis.rcItem, hBorder); }
            finally { _ = Win32Native.DeleteObject(hBorder); }
        }

        _ = Win32Native.SetBkMode(dis.hDC, Win32Native.TRANSPARENT_BK);
        _ = Win32Native.SetTextColor(dis.hDC, (disabled ? TextMuted : btn.ForeColor).ToColorRef());
        uint fmt = (uint)((btn.TextAlignLeft ? Win32Native.DT_LEFT : Win32Native.DT_CENTER) | Win32Native.DT_VCENTER | Win32Native.DT_SINGLELINE);
        var textRect = dis.rcItem;
        if (btn.TextAlignLeft) textRect.Left += 2;
        _ = Win32Native.DrawTextW(dis.hDC, btn.Text, btn.Text.Length, ref textRect, fmt);
    }

    private void HandleWmCommand(UIntPtr wParam)
    {
        int raw = unchecked((int)(ulong)wParam);
        int id = raw & 0xFFFF;
        int code = (raw >> 16) & 0xFFFF;
        if (!_controlsById.TryGetValue(id, out WizardControl? ctrl)) return;

        if (ctrl is WizardTextBox tb && code == Win32Native.EN_CHANGE && tb.NativeHwnd != IntPtr.Zero)
        {
            tb.SetTextFromNative(Win32Native.ReadWindowText(tb.NativeHwnd));
        }
        else if (ctrl is WizardButton btn && code == Win32Native.BN_CLICKED)
        {
            btn.RaiseClick();
        }
        else if (ctrl is WizardCheckBox chk && code == Win32Native.BN_CLICKED)
        {
            chk.Checked = (int)Win32Native.SendMessageW(chk.NativeHwnd, Win32Native.BM_GETCHECK, UIntPtr.Zero, IntPtr.Zero) == Win32Native.BST_CHECKED;
        }
        else if (ctrl is WizardRadioButton rb && code == Win32Native.BN_CLICKED)
        {
            if ((int)Win32Native.SendMessageW(rb.NativeHwnd, Win32Native.BM_GETCHECK, UIntPtr.Zero, IntPtr.Zero) == Win32Native.BST_CHECKED)
                rb.Checked = true;
        }
    }

    private IntPtr CreateScaledFont(string face, float sizePt, bool bold)
    {
        int pxHeight = -Math.Max(11, (int)Math.Round(sizePt * _dpi / 72.0));
        return Win32Native.CreateFontW(pxHeight, 0, 0, 0, bold ? 700 : 400, 0, 0, 0, 1, 0, 0, 5, 0, face);
    }

    private void ResizeAndCenterWindow(int clientW, int clientH)
    {
        var rect = new Win32Native.RECT { Left = 0, Top = 0, Right = Scale(clientW), Bottom = Scale(clientH) };
        _ = Win32Native.AdjustWindowRectEx(ref rect, Win32Native.WS_OVERLAPPED | Win32Native.WS_CAPTION | Win32Native.WS_SYSMENU | Win32Native.WS_MINIMIZEBOX, false, Win32Native.WS_EX_CONTROLPARENT);
        int w = rect.Right - rect.Left;
        int h = rect.Bottom - rect.Top;
        int x = Math.Max(40, (Win32Native.GetSystemMetrics(Win32Native.SM_CXSCREEN) - w) / 2);
        int y = Math.Max(40, (Win32Native.GetSystemMetrics(Win32Native.SM_CYSCREEN) - h) / 2);
        _ = Win32Native.MoveWindow(_hwnd, x, y, w, h, true);
    }

    private void ResizeWindowPreservingPosition(int clientW, int clientH)
    {
        var rect = new Win32Native.RECT { Left = 0, Top = 0, Right = Scale(clientW), Bottom = Scale(clientH) };
        _ = Win32Native.AdjustWindowRectEx(ref rect, Win32Native.WS_OVERLAPPED | Win32Native.WS_CAPTION | Win32Native.WS_SYSMENU | Win32Native.WS_MINIMIZEBOX, false, Win32Native.WS_EX_CONTROLPARENT);
        _ = SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, rect.Right - rect.Left, rect.Bottom - rect.Top, 0x0002 | 0x0004 | 0x0010);
    }

    private void TryApplyWindowIcon()
    {
        string? iconSrc = !string.IsNullOrWhiteSpace(_iconPath) && File.Exists(_iconPath) ? _iconPath : Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(iconSrc) || !File.Exists(iconSrc)) return;
        _hIcon = Win32Native.ExtractIconW(Win32Native.GetModuleHandleW(null), iconSrc, 0);
        if (_hIcon != IntPtr.Zero)
        {
            _ = Win32Native.SendMessageW(_hwnd, Win32Native.WM_SETICON, (UIntPtr)Win32Native.ICON_SMALL, _hIcon);
            _ = Win32Native.SendMessageW(_hwnd, Win32Native.WM_SETICON, (UIntPtr)Win32Native.ICON_BIG, _hIcon);
        }
    }

    private void DisposeNativeHandles()
    {
        DeleteGdi(ref _brushBg); DeleteGdi(ref _brushCard); DeleteGdi(ref _brushInput);
        DeleteGdi(ref _fontTitle); DeleteGdi(ref _fontSub); DeleteGdi(ref _fontBody); DeleteGdi(ref _fontBold);
        DeleteGdi(ref _fontSmall); DeleteGdi(ref _fontMono); DeleteGdi(ref _fontLog);
        if (_hIcon != IntPtr.Zero) { _ = Win32Native.DestroyIcon(_hIcon); _hIcon = IntPtr.Zero; }
        _hwnd = IntPtr.Zero;
    }

    private static void DeleteGdi(ref IntPtr hObj)
    {
        if (hObj != IntPtr.Zero) { _ = Win32Native.DeleteObject(hObj); hObj = IntPtr.Zero; }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
