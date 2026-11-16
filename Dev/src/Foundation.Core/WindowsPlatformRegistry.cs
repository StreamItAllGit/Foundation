// @module: Foundation.Core.WindowsPlatformRegistry
// @purpose: Live Windows Registry backend implementing IPlatformRegistry with [SupportedOSPlatform("windows")] and OperatingSystem.IsWindows() guards.
// @depends-on: System, System.Collections.Generic, System.Globalization, System.Runtime.Versioning, Microsoft.Win32
// @exports: WindowsPlatformRegistry

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Foundation.Core;

/// <summary>
/// Reads and mutates the live Windows Registry (HKCU, HKLM, HKCR) using in-box Microsoft.Win32.Registry.
/// Never instantiated when sandbox mode is active or host mutations are suppressed.
/// </summary>
public sealed class WindowsPlatformRegistry : IPlatformRegistry
{
    public bool SubKeyExists(PlatformRegistryHive hive, string subKeyPath)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows only.");
        return SubKeyExistsWin(hive, PlatformRegistryNaming.NormalizeSubKeyPath(subKeyPath));
    }

    public void CreateSubKey(PlatformRegistryHive hive, string subKeyPath)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows only.");
        CreateSubKeyWin(hive, PlatformRegistryNaming.NormalizeSubKeyPath(subKeyPath));
    }

    public bool DeleteSubKeyTree(PlatformRegistryHive hive, string subKeyPath, bool throwOnMissingSubKey = false)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows only.");
        return DeleteSubKeyTreeWin(hive, PlatformRegistryNaming.NormalizeSubKeyPath(subKeyPath), throwOnMissingSubKey);
    }

    public PlatformRegistryValue? GetValue(PlatformRegistryHive hive, string subKeyPath, string valueName)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows only.");
        return GetValueWin(hive, PlatformRegistryNaming.NormalizeSubKeyPath(subKeyPath), valueName ?? string.Empty);
    }

    public void SetValue(PlatformRegistryHive hive, string subKeyPath, string valueName, object? value, PlatformRegistryValueKind valueKind = PlatformRegistryValueKind.String) =>
        SetValues(hive, subKeyPath, new Dictionary<string, PlatformRegistryValue> { [valueName ?? string.Empty] = new(valueKind, value) });

    public void SetValues(PlatformRegistryHive hive, string subKeyPath, IReadOnlyDictionary<string, PlatformRegistryValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows only.");
        SetValuesWin(hive, PlatformRegistryNaming.NormalizeSubKeyPath(subKeyPath), values);
    }

    public bool DeleteValue(PlatformRegistryHive hive, string subKeyPath, string valueName, bool throwOnMissingValue = false)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows only.");
        return DeleteValueWin(hive, PlatformRegistryNaming.NormalizeSubKeyPath(subKeyPath), valueName ?? string.Empty, throwOnMissingValue);
    }

    public IReadOnlyList<string> GetSubKeyNames(PlatformRegistryHive hive, string subKeyPath)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows only.");
        return GetSubKeyNamesWin(hive, PlatformRegistryNaming.NormalizeSubKeyPath(subKeyPath));
    }

    public IReadOnlyList<string> GetValueNames(PlatformRegistryHive hive, string subKeyPath)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows only.");
        return GetValueNamesWin(hive, PlatformRegistryNaming.NormalizeSubKeyPath(subKeyPath));
    }

    public IReadOnlyDictionary<string, PlatformRegistryValue> GetValues(PlatformRegistryHive hive, string subKeyPath)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows only.");
        return GetValuesWin(hive, PlatformRegistryNaming.NormalizeSubKeyPath(subKeyPath));
    }

    [SupportedOSPlatform("windows")]
    private static RegistryKey GetBaseKey(PlatformRegistryHive hive) => hive switch
    {
        PlatformRegistryHive.CurrentUser => Registry.CurrentUser,
        PlatformRegistryHive.LocalMachine => Registry.LocalMachine,
        PlatformRegistryHive.ClassesRoot => Registry.ClassesRoot,
        _ => throw new ArgumentOutOfRangeException(nameof(hive), hive, "Unsupported registry hive.")
    };

    [SupportedOSPlatform("windows")]
    private static bool SubKeyExistsWin(PlatformRegistryHive hive, string norm)
    {
        using RegistryKey? key = GetBaseKey(hive).OpenSubKey(norm, false);
        return key is not null;
    }

    [SupportedOSPlatform("windows")]
    private static void CreateSubKeyWin(PlatformRegistryHive hive, string norm)
    {
        using RegistryKey? key = GetBaseKey(hive).CreateSubKey(norm, true);
    }

    [SupportedOSPlatform("windows")]
    private static bool DeleteSubKeyTreeWin(PlatformRegistryHive hive, string norm, bool throwOnMissing)
    {
        RegistryKey baseKey = GetBaseKey(hive);
        if (!throwOnMissing)
        {
            using RegistryKey? probe = baseKey.OpenSubKey(norm, false);
            if (probe is null) return false;
        }
        baseKey.DeleteSubKeyTree(norm, throwOnMissing);
        return true;
    }

    [SupportedOSPlatform("windows")]
    private static PlatformRegistryValue? GetValueWin(PlatformRegistryHive hive, string norm, string name)
    {
        using RegistryKey? key = GetBaseKey(hive).OpenSubKey(norm, false);
        object? raw = key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return key is null || raw is null ? null : new PlatformRegistryValue(FromNativeKind(key.GetValueKind(name)), raw);
    }

    [SupportedOSPlatform("windows")]
    private static void SetValuesWin(PlatformRegistryHive hive, string norm, IReadOnlyDictionary<string, PlatformRegistryValue> values)
    {
        using RegistryKey key = GetBaseKey(hive).CreateSubKey(norm, true)
            ?? throw new InvalidOperationException($"Unable to open or create registry key '{norm}'.");
        foreach (var kv in values)
            key.SetValue(kv.Key, CoerceNativeValue(kv.Value.Data, kv.Value.Kind), ToNativeKind(kv.Value.Kind));
    }

    [SupportedOSPlatform("windows")]
    private static bool DeleteValueWin(PlatformRegistryHive hive, string norm, string name, bool throwOnMissing)
    {
        using RegistryKey? key = GetBaseKey(hive).OpenSubKey(norm, true);
        if (key is null)
        {
            if (throwOnMissing) throw new ArgumentException($"Registry subkey '{norm}' does not exist.");
            return false;
        }
        if (!throwOnMissing && key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is null) return false;
        key.DeleteValue(name, throwOnMissing);
        return true;
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> GetSubKeyNamesWin(PlatformRegistryHive hive, string norm)
    {
        using RegistryKey? key = GetBaseKey(hive).OpenSubKey(norm, false);
        return key is null ? Array.Empty<string>() : key.GetSubKeyNames();
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> GetValueNamesWin(PlatformRegistryHive hive, string norm)
    {
        using RegistryKey? key = GetBaseKey(hive).OpenSubKey(norm, false);
        return key is null ? Array.Empty<string>() : key.GetValueNames();
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyDictionary<string, PlatformRegistryValue> GetValuesWin(PlatformRegistryHive hive, string norm)
    {
        var result = new Dictionary<string, PlatformRegistryValue>(StringComparer.OrdinalIgnoreCase);
        using RegistryKey? key = GetBaseKey(hive).OpenSubKey(norm, false);
        if (key is null) return result;
        foreach (string name in key.GetValueNames())
        {
            object? raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (raw is not null) result[name] = new PlatformRegistryValue(FromNativeKind(key.GetValueKind(name)), raw);
        }
        return result;
    }

    [SupportedOSPlatform("windows")]
    private static RegistryValueKind ToNativeKind(PlatformRegistryValueKind kind) => kind switch
    {
        PlatformRegistryValueKind.ExpandString => RegistryValueKind.ExpandString,
        PlatformRegistryValueKind.MultiString => RegistryValueKind.MultiString,
        PlatformRegistryValueKind.DWord => RegistryValueKind.DWord,
        PlatformRegistryValueKind.QWord => RegistryValueKind.QWord,
        PlatformRegistryValueKind.Binary => RegistryValueKind.Binary,
        _ => RegistryValueKind.String
    };

    [SupportedOSPlatform("windows")]
    private static PlatformRegistryValueKind FromNativeKind(RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.ExpandString => PlatformRegistryValueKind.ExpandString,
        RegistryValueKind.MultiString => PlatformRegistryValueKind.MultiString,
        RegistryValueKind.DWord => PlatformRegistryValueKind.DWord,
        RegistryValueKind.QWord => PlatformRegistryValueKind.QWord,
        RegistryValueKind.Binary => PlatformRegistryValueKind.Binary,
        _ => PlatformRegistryValueKind.String
    };

    private static object CoerceNativeValue(object? value, PlatformRegistryValueKind kind) => kind switch
    {
        PlatformRegistryValueKind.DWord => Convert.ToInt32(value ?? 0, CultureInfo.InvariantCulture),
        PlatformRegistryValueKind.QWord => Convert.ToInt64(value ?? 0L, CultureInfo.InvariantCulture),
        PlatformRegistryValueKind.MultiString => new PlatformRegistryValue(kind, value).AsMultiString() ?? Array.Empty<string>(),
        PlatformRegistryValueKind.Binary => new PlatformRegistryValue(kind, value).AsBinary() ?? Array.Empty<byte>(),
        _ => value?.ToString() ?? string.Empty
    };
}
