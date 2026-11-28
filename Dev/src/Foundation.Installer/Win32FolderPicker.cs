// @module: Foundation.Installer.UI.Win32FolderPicker
// @purpose: NativeAOT-safe COM IFileOpenDialog folder picker using direct vtable calls with zero WinForms dependencies.
// @depends-on: System, System.IO, System.Runtime.InteropServices
// @exports: Win32FolderPicker

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Foundation.Installer;

internal static unsafe class Win32FolderPicker
{
    private const uint CLSCTX_INPROC_SERVER = 0x1;
    private const uint FOS_PICKFOLDERS = 0x00000020;
    private const uint FOS_FORCEFILESYSTEM = 0x00000040;
    private const uint FOS_PATHMUSTEXIST = 0x00000800;
    private const uint SIGDN_FILESYSPATH = 0x80058000;
    private const uint COINIT_APARTMENTTHREADED = 0x2;

    private static readonly Guid CLSID_FileOpenDialog = new("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7");
    private static readonly Guid IID_IFileOpenDialog = new("d57c7288-d4ad-4768-be02-9d969532d960");
    private static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        in Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        in Guid riid,
        out IntPtr ppv);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        in Guid riid,
        out IntPtr ppv);

    public static string? PickFolder(IntPtr ownerHwnd, string title, string? initialPath)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        _ = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
        int hr = CoCreateInstance(in CLSID_FileOpenDialog, IntPtr.Zero, CLSCTX_INPROC_SERVER, in IID_IFileOpenDialog, out IntPtr dlg);
        if (hr < 0 || dlg == IntPtr.Zero)
            return null;

        IntPtr initialItem = IntPtr.Zero;
        IntPtr resultItem = IntPtr.Zero;
        try
        {
            void** vtbl = *(void***)dlg;
            // IFileDialog::GetOptions (slot 10), SetOptions (slot 9), SetFolder (slot 12), SetTitle (slot 17), Show (slot 3), GetResult (slot 20)
            var getOptions = (delegate* unmanaged[Stdcall]<IntPtr, out uint, int>)vtbl[10];
            var setOptions = (delegate* unmanaged[Stdcall]<IntPtr, uint, int>)vtbl[9];
            if (getOptions(dlg, out uint opts) >= 0)
            {
                _ = setOptions(dlg, opts | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
            }

            if (!string.IsNullOrWhiteSpace(title))
            {
                var setTitle = (delegate* unmanaged[Stdcall]<IntPtr, char*, int>)vtbl[17];
                fixed (char* pTitle = title)
                {
                    _ = setTitle(dlg, pTitle);
                }
            }

            if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
            {
                if (SHCreateItemFromParsingName(initialPath, IntPtr.Zero, in IID_IShellItem, out initialItem) >= 0 && initialItem != IntPtr.Zero)
                {
                    var setFolder = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)vtbl[12];
                    _ = setFolder(dlg, initialItem);
                }
            }

            var show = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)vtbl[3];
            if (show(dlg, ownerHwnd) != 0)
                return null;

            var getResult = (delegate* unmanaged[Stdcall]<IntPtr, out IntPtr, int>)vtbl[20];
            if (getResult(dlg, out resultItem) < 0 || resultItem == IntPtr.Zero)
                return null;

            void** itemVtbl = *(void***)resultItem;
            // IShellItem::GetDisplayName (slot 5)
            var getDisplayName = (delegate* unmanaged[Stdcall]<IntPtr, uint, out IntPtr, int>)itemVtbl[5];
            if (getDisplayName(resultItem, SIGDN_FILESYSPATH, out IntPtr pszName) >= 0 && pszName != IntPtr.Zero)
            {
                try
                {
                    return Marshal.PtrToStringUni(pszName);
                }
                finally
                {
                    CoTaskMemFree(pszName);
                }
            }

            return null;
        }
        finally
        {
            if (initialItem != IntPtr.Zero) Marshal.Release(initialItem);
            if (resultItem != IntPtr.Zero) Marshal.Release(resultItem);
            Marshal.Release(dlg);
        }
    }
}
