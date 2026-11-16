// @module: Foundation.Core.IPlatformRegistry
// @purpose: Cross-platform registry abstraction, Standard 18 hive/kind contracts, no-op isolation implementation, and PlatformRegistryFactory.
// @depends-on: System, System.Collections.Generic, System.Globalization
// @exports: PlatformRegistryHive, PlatformRegistryValueKind, PlatformRegistryValue, IPlatformRegistry, NoOpPlatformRegistry, PlatformRegistryFactory

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Foundation.Core;

public enum PlatformRegistryHive
{
    CurrentUser,
    LocalMachine,
    ClassesRoot
}

public enum PlatformRegistryValueKind
{
    String,
    ExpandString,
    MultiString,
    DWord,
    QWord,
    Binary
}

public sealed record PlatformRegistryValue(PlatformRegistryValueKind Kind, object? Data)
{
    public string? AsString() => Data switch
    {
        null => null,
        string s => s,
        string[] arr => string.Join(Environment.NewLine, arr),
        byte[] bytes => Convert.ToHexString(bytes).ToLowerInvariant(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => Data.ToString()
    };

    public int? AsInt32() => Data switch
    {
        null => null,
        int i => i,
        uint u => unchecked((int)u),
        long l => unchecked((int)l),
        string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) => parsed,
        IConvertible c => Convert.ToInt32(c, CultureInfo.InvariantCulture),
        _ => null
    };

    public long? AsInt64() => Data switch
    {
        null => null,
        long l => l,
        int i => i,
        ulong ul => unchecked((long)ul),
        uint u => u,
        string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) => parsed,
        IConvertible c => Convert.ToInt64(c, CultureInfo.InvariantCulture),
        _ => null
    };

    public string[]? AsMultiString() => Data switch
    {
        null => null,
        string[] arr => arr,
        IEnumerable<string> seq => new List<string>(seq).ToArray(),
        string s => new[] { s },
        _ => null
    };

    public byte[]? AsBinary() => Data switch
    {
        null => null,
        byte[] bytes => bytes,
        string hex => Convert.FromHexString(hex.Trim()),
        _ => null
    };
}

public interface IPlatformRegistry
{
    bool SubKeyExists(PlatformRegistryHive hive, string subKeyPath);
    void CreateSubKey(PlatformRegistryHive hive, string subKeyPath);
    bool DeleteSubKeyTree(PlatformRegistryHive hive, string subKeyPath, bool throwOnMissingSubKey = false);
    PlatformRegistryValue? GetValue(PlatformRegistryHive hive, string subKeyPath, string valueName);
    void SetValue(PlatformRegistryHive hive, string subKeyPath, string valueName, object? value, PlatformRegistryValueKind valueKind = PlatformRegistryValueKind.String);
    void SetValues(PlatformRegistryHive hive, string subKeyPath, IReadOnlyDictionary<string, PlatformRegistryValue> values);
    bool DeleteValue(PlatformRegistryHive hive, string subKeyPath, string valueName, bool throwOnMissingValue = false);
    IReadOnlyList<string> GetSubKeyNames(PlatformRegistryHive hive, string subKeyPath);
    IReadOnlyList<string> GetValueNames(PlatformRegistryHive hive, string subKeyPath);
    IReadOnlyDictionary<string, PlatformRegistryValue> GetValues(PlatformRegistryHive hive, string subKeyPath);
}

internal static class PlatformRegistryNaming
{
    public static string ToHiveCode(PlatformRegistryHive hive) => hive switch
    {
        PlatformRegistryHive.CurrentUser => "HKCU",
        PlatformRegistryHive.LocalMachine => "HKLM",
        PlatformRegistryHive.ClassesRoot => "HKCR",
        _ => throw new ArgumentOutOfRangeException(nameof(hive), hive, "Unsupported registry hive.")
    };

    public static string ToKindCode(PlatformRegistryValueKind kind) => kind switch
    {
        PlatformRegistryValueKind.String => "REG_SZ",
        PlatformRegistryValueKind.ExpandString => "REG_EXPAND_SZ",
        PlatformRegistryValueKind.MultiString => "REG_MULTI_SZ",
        PlatformRegistryValueKind.DWord => "REG_DWORD",
        PlatformRegistryValueKind.QWord => "REG_QWORD",
        PlatformRegistryValueKind.Binary => "REG_BINARY",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported registry value kind.")
    };

    public static PlatformRegistryValueKind ParseKindCode(string? raw) => (raw ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "REG_SZ" => PlatformRegistryValueKind.String,
        "REG_EXPAND_SZ" => PlatformRegistryValueKind.ExpandString,
        "REG_MULTI_SZ" => PlatformRegistryValueKind.MultiString,
        "REG_DWORD" => PlatformRegistryValueKind.DWord,
        "REG_QWORD" => PlatformRegistryValueKind.QWord,
        "REG_BINARY" => PlatformRegistryValueKind.Binary,
        _ => PlatformRegistryValueKind.String
    };

    public static string NormalizeSubKeyPath(string subKeyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subKeyPath);
        string cleaned = subKeyPath.Replace('/', '\\').Trim().Trim('\\');
        foreach (string prefix in new[] { "HKCU\\", "HKEY_CURRENT_USER\\", "HKLM\\", "HKEY_LOCAL_MACHINE\\", "HKCR\\", "HKEY_CLASSES_ROOT\\" })
        {
            if (cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[prefix.Length..];
                break;
            }
        }
        return cleaned;
    }
}

public sealed class NoOpPlatformRegistry : IPlatformRegistry
{
    public static NoOpPlatformRegistry Instance { get; } = new();

    public bool SubKeyExists(PlatformRegistryHive hive, string subKeyPath) => false;
    public void CreateSubKey(PlatformRegistryHive hive, string subKeyPath) { }
    public bool DeleteSubKeyTree(PlatformRegistryHive hive, string subKeyPath, bool throwOnMissingSubKey = false) => false;
    public PlatformRegistryValue? GetValue(PlatformRegistryHive hive, string subKeyPath, string valueName) => null;
    public void SetValue(PlatformRegistryHive hive, string subKeyPath, string valueName, object? value, PlatformRegistryValueKind valueKind = PlatformRegistryValueKind.String) { }
    public void SetValues(PlatformRegistryHive hive, string subKeyPath, IReadOnlyDictionary<string, PlatformRegistryValue> values) { }
    public bool DeleteValue(PlatformRegistryHive hive, string subKeyPath, string valueName, bool throwOnMissingValue = false) => false;
    public IReadOnlyList<string> GetSubKeyNames(PlatformRegistryHive hive, string subKeyPath) => Array.Empty<string>();
    public IReadOnlyList<string> GetValueNames(PlatformRegistryHive hive, string subKeyPath) => Array.Empty<string>();
    public IReadOnlyDictionary<string, PlatformRegistryValue> GetValues(PlatformRegistryHive hive, string subKeyPath) =>
        new Dictionary<string, PlatformRegistryValue>(StringComparer.OrdinalIgnoreCase);
}

public static class PlatformRegistryFactory
{
    public static IPlatformRegistry CreateDefault(SandboxResolution? sandbox = null, bool suppressMutations = false)
    {
        if (suppressMutations)
            return NoOpPlatformRegistry.Instance;

        SandboxResolution res = sandbox ?? SandboxEnvironmentSeam.ResolveCurrent();
        if (res.IsSandboxActive)
        {
            if (string.IsNullOrWhiteSpace(res.RegistryHivePath))
                SandboxEnvironmentSeam.ThrowContainmentViolation("Active sandbox configuration is missing RegistryHivePath.");

            return new JsonHivePlatformRegistry(res.RegistryHivePath!, res.SandboxRoot, res.RegistryLockPath);
        }

        return OperatingSystem.IsWindows()
            ? new WindowsPlatformRegistry()
            : NoOpPlatformRegistry.Instance;
    }
}
