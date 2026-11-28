// @module: Foundation.Installer.UI.Wizard.Win32Host
// @purpose: Native Win32 window lifecycle, child HWND creation, tooltips, and DPI scaling for InstallerWizardForm.
// @depends-on: System, System.Collections.Concurrent, System.Collections.Generic, System.IO, System.Runtime.CompilerServices, System.Runtime.InteropServices, System.Threading
// @exports: InstallerWizardForm (partial: RunDialog, Close, OnNativeCreate)

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Foundation.Installer;

public sealed unsafe partial class InstallerWizardForm
{
    private const uint WmAppDispatch = Win32Native.WM_APP + 1;
    private const string WindowClassName = "FoundationWin32InstallerWizard";

    private static readonly ConcurrentDictionary<IntPtr, InstallerWizardForm> ActiveWindows = new();
    private static ushort _registeredAtom;
    private static readonly object ClassRegisterGate = new();
    [ThreadStatic]
    private static InstallerWizardForm? _creatingInstance;

    private IntPtr _hwnd;
    private IntPtr _hTooltip;
    private IntPtr _hIcon;
    private IntPtr _brushBg;
    private IntPtr _brushCard;
    private IntPtr _brushInput;
    private IntPtr _fontTitle;
    private IntPtr _fontSub;
    private IntPtr _fontBody;
    private IntPtr _fontBold;
    private IntPtr _fontSmall;
    private IntPtr _fontMono;
    private IntPtr _fontLog;
    private int _uiThreadId;
    private uint _dpi = 96;
    private readonly Dictionary<int, WizardControl> _controlsById = new();
    private readonly Dictionary<IntPtr, WizardControl> _controlsByHwnd = new();

    public void RunDialog()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Win32Native.TryEnablePerMonitorV2DpiAwareness();
        var icc = new Win32Native.INITCOMMONCONTROLSEX { dwSize = (uint)sizeof(Win32Native.INITCOMMONCONTROLSEX), dwICC = 0x00004020 };
        _ = Win32Native.InitCommonControlsEx(ref icc);

        EnsureWindowClassRegistered();
        _uiThreadId = Environment.CurrentManagedThreadId;

        const int style = Win32Native.WS_OVERLAPPED | Win32Native.WS_CAPTION | Win32Native.WS_SYSMENU | Win32Native.WS_MINIMIZEBOX;
        const int exStyle = Win32Native.WS_EX_CONTROLPARENT;

        _creatingInstance = this;
        try
        {
            _hwnd = Win32Native.CreateWindowExW(
                exStyle, WindowClassName, _windowText, style,
                100, 100, _clientSize.Width, _clientSize.Height,
                IntPtr.Zero, IntPtr.Zero, Win32Native.GetModuleHandleW(null), IntPtr.Zero);
        }
        finally
        {
            _creatingInstance = null;
        }

        if (_hwnd == IntPtr.Zero)
            return;

        uint detectedDpi = Win32Native.GetDpiForWindow(_hwnd);
        _dpi = detectedDpi >= 96 ? detectedDpi : 96;
        ResizeAndCenterWindow(_clientSize.Width, _clientSize.Height);
        InstallerWizardControls.EnableDarkTitleBar(_hwnd);
        TryApplyWindowIcon();

        _ = Win32Native.ShowWindow(_hwnd, Win32Native.SW_SHOW);
        _ = Win32Native.UpdateWindow(_hwnd);

        while (Win32Native.GetMessageW(out Win32Native.MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (!Win32Native.IsDialogMessageW(_hwnd, ref msg))
            {
                _ = Win32Native.TranslateMessage(ref msg);
                _ = Win32Native.DispatchMessageW(ref msg);
            }
        }
    }

    public void Close()
    {
        RunOnUiThread(() =>
        {
            if (_hwnd != IntPtr.Zero)
                _ = Win32Native.DestroyWindow(_hwnd);
        });
    }

    private void RunOnUiThread(Action action)
    {
        if (_disposed) return;
        if (_hwnd == IntPtr.Zero || Environment.CurrentManagedThreadId == _uiThreadId)
        {
            action();
            return;
        }

        _uiQueue.Enqueue(action);
        _ = Win32Native.PostMessageW(_hwnd, WmAppDispatch, UIntPtr.Zero, IntPtr.Zero);
    }

    private static void EnsureWindowClassRegistered()
    {
        if (_registeredAtom != 0) return;
        lock (ClassRegisterGate)
        {
            if (_registeredAtom != 0) return;
            fixed (char* pClassName = WindowClassName)
            {
                var wc = new Win32Native.WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(Win32Native.WNDCLASSEXW),
                    lpfnWndProc = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, UIntPtr, IntPtr, IntPtr>)&NativeWndProc,
                    hInstance = Win32Native.GetModuleHandleW(null),
                    hCursor = Win32Native.LoadCursorW(IntPtr.Zero, (IntPtr)Win32Native.IDC_ARROW),
                    hbrBackground = IntPtr.Zero,
                    lpszClassName = pClassName
                };
                _registeredAtom = Win32Native.RegisterClassExW(ref wc);
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static IntPtr NativeWndProc(IntPtr hwnd, uint msg, UIntPtr wParam, IntPtr lParam)
    {
        if (msg == Win32Native.WM_CREATE && _creatingInstance is not null)
        {
            ActiveWindows[hwnd] = _creatingInstance;
            _creatingInstance.OnNativeCreate(hwnd);
            return IntPtr.Zero;
        }

        if (ActiveWindows.TryGetValue(hwnd, out InstallerWizardForm? form))
            return form.HandleMessage(hwnd, msg, wParam, lParam);

        return Win32Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void OnNativeCreate(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _dpi = Math.Max(96u, Win32Native.GetDpiForWindow(hwnd));
        _brushBg = Win32Native.CreateSolidBrush(_bgPrimary.ToColorRef());
        _brushCard = Win32Native.CreateSolidBrush(_bgCard.ToColorRef());
        _brushInput = Win32Native.CreateSolidBrush(BgInput.ToColorRef());

        _fontTitle = CreateScaledFont("Segoe UI Semibold", 13.0f, bold: true);
        _fontSub = CreateScaledFont("Segoe UI", 8.75f, bold: false);
        _fontBody = CreateScaledFont("Segoe UI", 9.0f, bold: false);
        _fontBold = CreateScaledFont("Segoe UI Semibold", 9.25f, bold: true);
        _fontSmall = CreateScaledFont("Segoe UI Semibold", 8.5f, bold: true);
        _fontMono = CreateScaledFont("Consolas", 9.5f, bold: false);
        _fontLog = CreateScaledFont("Consolas", 8.5f, bold: false);

        _hTooltip = Win32Native.CreateWindowExW(
            0, "tooltips_class32", null, unchecked((int)0x80000003),
            0, 0, 0, 0, hwnd, IntPtr.Zero, Win32Native.GetModuleHandleW(null), IntPtr.Zero);
        if (_hTooltip != IntPtr.Zero)
        {
            _ = Win32Native.SendMessageW(_hTooltip, Win32Native.TTM_SETMAXTIPWIDTH, UIntPtr.Zero, (IntPtr)Scale(360));
            _ = Win32Native.SendMessageW(_hTooltip, Win32Native.TTM_SETDELAYTIME, (UIntPtr)Win32Native.TTDT_INITIAL, (IntPtr)300);
            _ = Win32Native.SendMessageW(_hTooltip, Win32Native.TTM_SETDELAYTIME, (UIntPtr)Win32Native.TTDT_AUTOPOP, (IntPtr)8000);
        }

        int nextId = 1001;
        CreateNativeChild(ref nextId, _lblTitle, "STATIC", Win32Native.WS_CHILD | Win32Native.SS_LEFT | Win32Native.SS_ENDELLIPSIS | Win32Native.SS_NOPREFIX, _fontTitle);
        CreateNativeChild(ref nextId, _lblSub, "STATIC", Win32Native.WS_CHILD | Win32Native.SS_LEFT | Win32Native.SS_NOPREFIX | (_lblSub.AutoEllipsis ? Win32Native.SS_ENDELLIPSIS : 0), _fontSub);
        CreateNativeChild(ref nextId, _lblModeBadge, "STATIC", Win32Native.WS_CHILD | Win32Native.SS_LEFT | Win32Native.SS_NOPREFIX, _fontSmall);
        CreateNativeChild(ref nextId, _lblDirHeader, "STATIC", Win32Native.WS_CHILD | Win32Native.SS_LEFT | Win32Native.SS_NOPREFIX, _fontBold);
        CreateNativeChild(ref nextId, _txtInstallDir, "EDIT", Win32Native.WS_CHILD | Win32Native.WS_TABSTOP | Win32Native.ES_LEFT | Win32Native.ES_AUTOHSCROLL, _fontMono, Win32Native.WS_EX_CLIENTEDGE);
        CreateNativeChild(ref nextId, _btnBrowse, "BUTTON", Win32Native.WS_CHILD | Win32Native.WS_TABSTOP | Win32Native.BS_OWNERDRAW, _fontBody);

        CreateNativeChild(ref nextId, _lblSecondaryDirHeader, "STATIC", Win32Native.WS_CHILD | Win32Native.SS_LEFT | Win32Native.SS_NOPREFIX, _fontBold);
        CreateNativeChild(ref nextId, _txtSecondaryDir, "EDIT", Win32Native.WS_CHILD | Win32Native.WS_TABSTOP | Win32Native.ES_LEFT | Win32Native.ES_AUTOHSCROLL, _fontMono, Win32Native.WS_EX_CLIENTEDGE);
        if (!string.IsNullOrWhiteSpace(_txtSecondaryDir.PlaceholderText))
        {
            fixed (char* pHint = _txtSecondaryDir.PlaceholderText)
                _ = Win32Native.SendMessageW(_txtSecondaryDir.NativeHwnd, Win32Native.EM_SETCUEBANNER, (UIntPtr)1, (IntPtr)pHint);
        }
        CreateNativeChild(ref nextId, _btnSecondaryBrowse, "BUTTON", Win32Native.WS_CHILD | Win32Native.WS_TABSTOP | Win32Native.BS_OWNERDRAW, _fontBody);

        CreateNativeChild(ref nextId, _lblOptionsHeader, "STATIC", Win32Native.WS_CHILD | Win32Native.SS_LEFT | Win32Native.SS_NOPREFIX, _fontSmall);
        CreateNativeChild(ref nextId, _lblProfileHeader, "STATIC", Win32Native.WS_CHILD | Win32Native.SS_LEFT | Win32Native.SS_NOPREFIX, _fontBold);
        CreateNativeRadio(ref nextId, _rbProfileLightweight, isGroupStart: true);
        CreateNativeRadio(ref nextId, _rbProfileFull, isGroupStart: false);
        CreateNativeRadio(ref nextId, _rbProfileCustom, isGroupStart: false);
        CreateNativeCheck(ref nextId, _chkShortcuts);
        CreateNativeCheck(ref nextId, _chkAutoStart);
        CreateNativeCheck(ref nextId, _chkLaunchAfter);
        CreateNativeCheck(ref nextId, _chkMigrateLegacy);
        CreateNativeCheck(ref nextId, _chkPurgeData);

        CreateNativeChild(ref nextId, _lblStatus, "STATIC", Win32Native.WS_CHILD | Win32Native.SS_LEFT | Win32Native.SS_NOPREFIX | (_lblStatus.AutoEllipsis ? Win32Native.SS_ENDELLIPSIS : 0), _fontBody);
        CreateNativeChild(ref nextId, _progressBar, "msctls_progress32", Win32Native.WS_CHILD, IntPtr.Zero);
        _ = Win32Native.SendMessageW(_progressBar.NativeHwnd, Win32Native.PBM_SETRANGE32, UIntPtr.Zero, (IntPtr)100);
        CreateNativeChild(ref nextId, _btnToggleDetails, "BUTTON", Win32Native.WS_CHILD | Win32Native.WS_TABSTOP | Win32Native.BS_OWNERDRAW, _fontSmall);
        CreateNativeChild(ref nextId, _txtLog, "EDIT", Win32Native.WS_CHILD | Win32Native.WS_VSCROLL | Win32Native.ES_MULTILINE | Win32Native.ES_READONLY | Win32Native.ES_AUTOVSCROLL, _fontLog, Win32Native.WS_EX_CLIENTEDGE);

        CreateNativeChild(ref nextId, _btnCancel, "BUTTON", Win32Native.WS_CHILD | Win32Native.WS_TABSTOP | Win32Native.BS_OWNERDRAW, _fontBody);
        CreateNativeChild(ref nextId, _btnPrimary, "BUTTON", Win32Native.WS_CHILD | Win32Native.WS_TABSTOP | Win32Native.BS_OWNERDRAW, _fontBold);

        SyncAllNativeControlBounds();
    }

    private void CreateNativeChild(ref int nextId, WizardControl ctrl, string className, int style, IntPtr font, int exStyle = 0)
    {
        int id = nextId++;
        (int x, int y) = ResolveAbsoluteDipPosition(ctrl);
        int finalStyle = style | (ctrl.Visible ? Win32Native.WS_VISIBLE : 0) | (!ctrl.Enabled ? Win32Native.WS_DISABLED : 0);
        IntPtr hChild = Win32Native.CreateWindowExW(
            exStyle, className, ctrl.Text, finalStyle,
            Scale(x), Scale(y), Scale(Math.Max(1, ctrl.Width)), Scale(Math.Max(1, ctrl.Height)),
            _hwnd, (IntPtr)id, Win32Native.GetModuleHandleW(null), IntPtr.Zero);
        if (hChild == IntPtr.Zero) return;

        if (font != IntPtr.Zero)
            _ = Win32Native.SendMessageW(hChild, Win32Native.WM_SETFONT, (UIntPtr)(ulong)font, (IntPtr)1);

        ctrl.NativeHwnd = hChild;
        ctrl.AttachSyncCallback(OnControlPropertyChanged);
        _controlsById[id] = ctrl;
        _controlsByHwnd[hChild] = ctrl;
        RegisterTooltipIfPresent(hChild, ctrl.TooltipText);
    }

    private void CreateNativeCheck(ref int nextId, WizardCheckBox chk)
    {
        CreateNativeChild(ref nextId, chk, "BUTTON", Win32Native.WS_CHILD | Win32Native.WS_TABSTOP | Win32Native.BS_AUTOCHECKBOX, chk.Bold ? _fontBold : _fontBody);
        if (chk.NativeHwnd != IntPtr.Zero)
        {
            _ = Win32Native.SetWindowTheme(chk.NativeHwnd, string.Empty, string.Empty);
            _ = Win32Native.SendMessageW(chk.NativeHwnd, Win32Native.BM_SETCHECK, (UIntPtr)(chk.Checked ? Win32Native.BST_CHECKED : Win32Native.BST_UNCHECKED), IntPtr.Zero);
        }
    }

    private void CreateNativeRadio(ref int nextId, WizardRadioButton rb, bool isGroupStart)
    {
        int style = Win32Native.WS_CHILD | Win32Native.WS_TABSTOP | Win32Native.BS_AUTORADIOBUTTON | (isGroupStart ? 0x00020000 : 0);
        CreateNativeChild(ref nextId, rb, "BUTTON", style, _fontBody);
        if (rb.NativeHwnd != IntPtr.Zero)
        {
            _ = Win32Native.SetWindowTheme(rb.NativeHwnd, string.Empty, string.Empty);
            _ = Win32Native.SendMessageW(rb.NativeHwnd, Win32Native.BM_SETCHECK, (UIntPtr)(rb.Checked ? Win32Native.BST_CHECKED : Win32Native.BST_UNCHECKED), IntPtr.Zero);
        }
    }

    private void RegisterTooltipIfPresent(IntPtr hChild, string? tooltip)
    {
        if (_hTooltip == IntPtr.Zero || string.IsNullOrWhiteSpace(tooltip)) return;
        fixed (char* pTip = tooltip)
        {
            var ti = new Win32Native.TOOLINFOW
            {
                cbSize = (uint)sizeof(Win32Native.TOOLINFOW),
                uFlags = Win32Native.TTF_SUBCLASS | Win32Native.TTF_IDISHWND,
                hwnd = _hwnd,
                uId = (UIntPtr)(ulong)hChild,
                lpszText = pTip
            };
            _ = Win32Native.SendMessageW(_hTooltip, Win32Native.TTM_ADDTOOLW, UIntPtr.Zero, (IntPtr)(&ti));
        }
    }
}
