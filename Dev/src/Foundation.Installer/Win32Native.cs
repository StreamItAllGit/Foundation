// @module: Foundation.Installer.UI.Win32Native
// @purpose: NativeAOT-safe Win32, GDI32, COMCTL32, UxTheme, Shell32, and DWM P/Invoke bindings replacing WinForms in Foundation.Installer.
// @depends-on: System, System.Runtime.InteropServices
// @exports: Win32Native

using System;
using System.Runtime.InteropServices;

namespace Foundation.Installer;

internal static unsafe partial class Win32Native
{
    public const int WS_OVERLAPPED = 0x00000000;
    public const int WS_CAPTION = 0x00C00000;
    public const int WS_SYSMENU = 0x00080000;
    public const int WS_MINIMIZEBOX = 0x00020000;
    public const int WS_VISIBLE = 0x10000000;
    public const int WS_CHILD = 0x40000000;
    public const int WS_TABSTOP = 0x00010000;
    public const int WS_DISABLED = 0x08000000;
    public const int WS_VSCROLL = 0x00200000;
    public const int WS_CLIPSIBLINGS = 0x04000000;
    public const int WS_EX_CLIENTEDGE = 0x00000200;
    public const int WS_EX_CONTROLPARENT = 0x00010000;

    public const int ES_LEFT = 0x0000;
    public const int ES_AUTOHSCROLL = 0x0080;
    public const int ES_MULTILINE = 0x0004;
    public const int ES_READONLY = 0x0800;
    public const int ES_AUTOVSCROLL = 0x0040;

    public const int SS_LEFT = 0x0000;
    public const int SS_ENDELLIPSIS = 0x00004000;
    public const int SS_NOPREFIX = 0x00000080;

    public const int BS_PUSHBUTTON = 0x00000000;
    public const int BS_AUTOCHECKBOX = 0x00000003;
    public const int BS_AUTORADIOBUTTON = 0x00000009;
    public const int BS_OWNERDRAW = 0x0000000B;

    public const int PBS_MARQUEE = 0x08;
    public const uint WM_USER = 0x0400;
    public const uint WM_APP = 0x8000;
    public const uint WM_CREATE = 0x0001;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_SETFONT = 0x0030;
    public const uint WM_SETICON = 0x0080;
    public const uint WM_COMMAND = 0x0111;
    public const uint WM_CTLCOLOREDIT = 0x0133;
    public const uint WM_CTLCOLORBTN = 0x0135;
    public const uint WM_CTLCOLORSTATIC = 0x0138;
    public const uint WM_DRAWITEM = 0x002B;
    public const uint WM_PAINT = 0x000F;

    public const int BN_CLICKED = 0;
    public const int EN_CHANGE = 0x0300;
    public const uint BM_GETCHECK = 0x00F0;
    public const uint BM_SETCHECK = 0x00F1;
    public const int BST_UNCHECKED = 0;
    public const int BST_CHECKED = 1;

    public const uint EM_SETCUEBANNER = 0x1501;
    public const uint EM_SETSEL = 0x00B1;
    public const uint EM_REPLACESEL = 0x00C2;
    public const uint PBM_SETRANGE32 = WM_USER + 6;
    public const uint PBM_SETPOS = WM_USER + 2;
    public const uint PBM_SETMARQUEE = WM_USER + 10;

    public const uint TTM_ADDTOOLW = WM_USER + 50;
    public const uint TTM_SETMAXTIPWIDTH = WM_USER + 24;
    public const uint TTM_SETDELAYTIME = WM_USER + 3;
    public const int TTF_IDISHWND = 0x0001;
    public const int TTF_SUBCLASS = 0x0010;
    public const int TTDT_AUTOPOP = 2;
    public const int TTDT_INITIAL = 3;

    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;
    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;
    public const int TRANSPARENT_BK = 1;
    public const int OPAQUE_BK = 2;
    public const int DT_LEFT = 0x0000;
    public const int DT_CENTER = 0x0001;
    public const int DT_VCENTER = 0x0004;
    public const int DT_SINGLELINE = 0x0020;
    public const uint ODS_SELECTED = 0x0001;
    public const uint ODS_DISABLED = 0x0004;
    public const uint ODS_FOCUS = 0x0010;

    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int MB_OK = 0x00000000;
    public const int MB_ICONWARNING = 0x00000030;
    public const int MB_ICONERROR = 0x00000010;
    public const int ICON_SMALL = 0;
    public const int ICON_BIG = 1;
    public const uint IDC_ARROW = 32512;
    public const int GWL_STYLE = -16;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int cx; public int cy; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DRAWITEMSTRUCT
    {
        public uint CtlType;
        public uint CtlID;
        public uint itemID;
        public uint itemAction;
        public uint itemState;
        public IntPtr hwndItem;
        public IntPtr hDC;
        public RECT rcItem;
        public UIntPtr itemData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INITCOMMONCONTROLSEX
    {
        public uint dwSize;
        public uint dwICC;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct TOOLINFOW
    {
        public uint cbSize;
        public uint uFlags;
        public IntPtr hwnd;
        public UIntPtr uId;
        public RECT rect;
        public IntPtr hinst;
        public char* lpszText;
        public IntPtr lParam;
        public IntPtr lpReserved;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("comctl32.dll", SetLastError = true)]
    public static extern bool InitCommonControlsEx(ref INITCOMMONCONTROLSEX iccex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowExW(
        int dwExStyle,
        string lpClassName,
        string? lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool UpdateWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

    [DllImport("user32.dll")]
    public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll")]
    public static extern bool AdjustWindowRectEx(ref RECT lpRect, int dwStyle, bool bMenu, int dwExStyle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SetWindowTextW(IntPtr hWnd, string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(IntPtr hWnd, char* lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool PostMessageW(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern IntPtr LoadCursorW(IntPtr hInstance, IntPtr lpCursorName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    [DllImport("user32.dll")]
    public static extern bool IsDialogMessageW(IntPtr hDlg, ref MSG lpMsg);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr ExtractIconW(IntPtr hInst, string lpszExeFileName, uint nIconIndex);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    public static extern int SetWindowTheme(IntPtr hwnd, string? pszSubAppName, string? pszSubIdList);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreatePen(int iStyle, int cWidth, uint color);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    public static extern uint SetTextColor(IntPtr hdc, uint color);

    [DllImport("gdi32.dll")]
    public static extern uint SetBkColor(IntPtr hdc, uint color);

    [DllImport("gdi32.dll")]
    public static extern int SetBkMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateFontW(
        int cHeight, int cWidth, int cEscapement, int cOrientation, int cWeight,
        uint bItalic, uint bUnderline, uint bStrikeOut, uint iCharSet,
        uint iOutPrecision, uint iClipPrecision, uint iQuality, uint iPitchAndFamily,
        string pszFaceName);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetTextExtentPoint32W(IntPtr hdc, string lpString, int c, out SIZE psizl);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    public static extern int FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

    [DllImport("user32.dll")]
    public static extern int FrameRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int DrawTextW(IntPtr hdc, string lpchText, int cchText, ref RECT lprc, uint format);

    public static void TryEnablePerMonitorV2DpiAwareness()
    {
        if (!OperatingSystem.IsWindows()) return;
        try { SetProcessDpiAwarenessContext((IntPtr)(-4)); }
        catch { /* Ignored when caller already initialized DPI awareness. */ }
    }

    public static string ReadWindowText(IntPtr hwnd)
    {
        int len = GetWindowTextLengthW(hwnd);
        if (len <= 0) return string.Empty;
        char[] buf = new char[len + 1];
        fixed (char* p = buf)
        {
            int copied = GetWindowTextW(hwnd, p, buf.Length);
            return copied > 0 ? new string(buf, 0, copied) : string.Empty;
        }
    }
}
