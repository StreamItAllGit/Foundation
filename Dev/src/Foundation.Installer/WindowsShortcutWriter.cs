// @module: Foundation.Installer.Shortcuts
// @purpose: Writes pure-C# MS-SHLLINK (.lnk) shortcuts and HKCU Uninstall/Run registry entries with strict test-mode isolation.
// @depends-on: System, System.Buffers.Binary, System.IO, System.Text, Microsoft.Win32
// @exports: WindowsShortcutWriter

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Foundation.Installer;

/// <summary>
/// Pure managed C# writer for Windows Shell Link (.lnk) files and HKCU Uninstall/Startup registry keys.
/// Strictly skips live host registry mutations when running inside Sandbox / --test-mode.
/// </summary>
public static class WindowsShortcutWriter
{
    private static readonly Guid ShellLinkClsid = new("00021401-0000-0000-C000-000000000046");

    private const uint HasName = 0x00000004;
    private const uint HasWorkingDir = 0x00000010;
    private const uint HasArguments = 0x00000020;
    private const uint HasIconLocation = 0x00000040;
    private const uint IsUnicode = 0x00000080;
    private const uint ForceNoLinkInfo = 0x00000100;
    private const uint HasExpString = 0x00000200;
    private const uint FileAttributeArchive = 0x00000020;
    private const uint SwShowNormal = 0x00000001;
    private const uint EnvVarBlockSize = 0x00000314;
    private const uint EnvVarBlockSignature = 0xA0000001;

    public static void WriteShortcut(
        string outputLnkPath,
        string targetExecutablePath,
        string arguments = "",
        string? workingDirectory = null,
        string? description = null,
        string? iconFilePath = null,
        int iconIndex = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputLnkPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetExecutablePath);

        string fullTarget = Path.GetFullPath(targetExecutablePath);
        string resolvedWorkDir = !string.IsNullOrWhiteSpace(workingDirectory)
            ? Path.GetFullPath(workingDirectory)
            : (Path.GetDirectoryName(fullTarget) ?? AppContext.BaseDirectory);

        byte[] bytes = BuildShortcutBytes(
            fullTarget,
            arguments ?? string.Empty,
            resolvedWorkDir,
            description ?? $"Launch {Path.GetFileNameWithoutExtension(fullTarget)}",
            iconFilePath,
            iconIndex);

        string? parentDir = Path.GetDirectoryName(Path.GetFullPath(outputLnkPath));
        if (!string.IsNullOrEmpty(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        string tempFile = $"{outputLnkPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(tempFile, bytes);
            File.Move(tempFile, outputLnkPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { /* ignore */ }
            }
        }
    }

    public static byte[] BuildShortcutBytes(
        string targetExecutablePath,
        string arguments,
        string workingDirectory,
        string description,
        string? iconFilePath = null,
        int iconIndex = 0)
    {
        using var ms = new MemoryStream(1024);
        using var bw = new BinaryWriter(ms, Encoding.Unicode, leaveOpen: true);

        uint flags = HasName | HasWorkingDir | HasArguments | IsUnicode | ForceNoLinkInfo | HasExpString;
        bool hasIcon = !string.IsNullOrWhiteSpace(iconFilePath);
        if (hasIcon)
        {
            flags |= HasIconLocation;
        }

        long nowFileTime = DateTime.UtcNow.ToFileTimeUtc();

        bw.Write((uint)0x0000004C);
        bw.Write(ShellLinkClsid.ToByteArray());
        bw.Write(flags);
        bw.Write(FileAttributeArchive);
        bw.Write(nowFileTime);
        bw.Write(nowFileTime);
        bw.Write(nowFileTime);
        bw.Write((uint)0);
        bw.Write(iconIndex);
        bw.Write(SwShowNormal);
        bw.Write((ushort)0);
        bw.Write((ushort)0);
        bw.Write((uint)0);
        bw.Write((uint)0);

        WriteUnicodeStringData(bw, description);
        WriteUnicodeStringData(bw, workingDirectory);
        WriteUnicodeStringData(bw, arguments);
        if (hasIcon)
        {
            WriteUnicodeStringData(bw, iconFilePath!);
        }

        bw.Write(EnvVarBlockSize);
        bw.Write(EnvVarBlockSignature);

        byte[] ansiBuf = new byte[260];
        byte[] asciiBytes = Encoding.ASCII.GetBytes(targetExecutablePath);
        Array.Copy(asciiBytes, ansiBuf, Math.Min(asciiBytes.Length, 259));
        bw.Write(ansiBuf);

        byte[] unicodeBuf = new byte[520];
        byte[] utf16Bytes = Encoding.Unicode.GetBytes(targetExecutablePath);
        int uniCopyLen = Math.Min(utf16Bytes.Length, 518);
        uniCopyLen -= uniCopyLen % 2;
        Array.Copy(utf16Bytes, unicodeBuf, uniCopyLen);
        bw.Write(unicodeBuf);

        bw.Write((uint)0x00000000);
        bw.Flush();
        return ms.ToArray();
    }

    public static bool IsValidShellLinkHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < 76)
        {
            return false;
        }

        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        if (headerSize != 0x0000004C)
        {
            return false;
        }

        return new Guid(data.Slice(4, 16)) == ShellLinkClsid;
    }

    public static void RegisterCurrentUserUninstallEntry(
        InstallerThemeOptions theme,
        string installDir,
        string uninstallerExePath,
        bool isContainedSandbox)
    {
        if (isContainedSandbox || !OperatingSystem.IsWindows())
        {
            return;
        }

        string subKeyPath = $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{theme.DisplayName}";
        using RegistryKey? key = Registry.CurrentUser.CreateSubKey(subKeyPath, writable: true);
        if (key is null)
        {
            return;
        }

        string exeName = theme.ExecutableFileName ?? $"{theme.DisplayName}.exe";
        key.SetValue("DisplayName", theme.DisplayName);
        key.SetValue("DisplayVersion", theme.Version);
        key.SetValue("Publisher", "Personal Suite Ecosystem");
        key.SetValue("InstallLocation", installDir);
        key.SetValue("DisplayIcon", Path.Combine(installDir, exeName));
        key.SetValue("UninstallString", $"\"{uninstallerExePath}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 0, RegistryValueKind.DWord);
    }

    public static void RemoveCurrentUserUninstallEntry(string displayName, bool isContainedSandbox)
    {
        if (isContainedSandbox || !OperatingSystem.IsWindows())
        {
            return;
        }

        string subKeyPath = $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{displayName}";
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
        }
        catch
        {
            // Ignore missing registry entry
        }
    }

    private static void WriteUnicodeStringData(BinaryWriter bw, string value)
    {
        string safe = value ?? string.Empty;
        if (safe.Length > ushort.MaxValue)
        {
            safe = safe[..ushort.MaxValue];
        }
        bw.Write((ushort)safe.Length);
        bw.Write(Encoding.Unicode.GetBytes(safe));
    }
}
