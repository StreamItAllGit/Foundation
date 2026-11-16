// @module: Foundation.Core.JsonHivePlatformRegistry
// @purpose: Cross-process atomic JSON pseudo-registry implementing Standard 18 schema, named Mutex + PidSafeLockfile, and .tmp->Flush->.bak->Move persistence.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.Globalization, System.IO, System.Linq, System.Security.Cryptography, System.Text, System.Text.Json, System.Text.Json.Serialization, System.Threading
// @exports: JsonHivePlatformRegistry

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Foundation.Core;

using HiveTree = Dictionary<string, Dictionary<string, Dictionary<string, PlatformRegistryValue>>>;

/// <summary>
/// Persists HKCU, HKLM, and HKCR subkeys and typed values inside &lt;SandboxRoot&gt;\registry\registry.hive.json
/// using a named Mutex, PidSafeLockfile, and flush-safe .tmp -> .bak -> File.Move atomic replacement.
/// </summary>
public sealed class JsonHivePlatformRegistry : IPlatformRegistry
{
    public const string SchemaUri = "https://schemas.local/registry-hive.v1.json";
    public const int CurrentSchemaVersion = 1;
    private static readonly string[] HiveCodes = ["HKCU", "HKLM", "HKCR"];
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public string HiveFilePath { get; }
    public string BackupFilePath { get; }
    public string SandboxRoot { get; }
    public string LockFilePath { get; }
    public string MutexName { get; }
    private readonly TimeSpan _lockTimeout;

    private sealed class HiveDocumentDto
    {
        [JsonPropertyName("$schema")] public string Schema { get; set; } = SchemaUri;
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        [JsonPropertyName("sandboxRoot")] public string SandboxRoot { get; set; } = string.Empty;
        [JsonPropertyName("updatedAtUtc")] public string UpdatedAtUtc { get; set; } = string.Empty;
        [JsonPropertyName("registryHives")] public Dictionary<string, Dictionary<string, Dictionary<string, HiveEntryDto>>> RegistryHives { get; set; } = new();
    }

    private sealed class HiveEntryDto
    {
        [JsonPropertyName("registryValueKind")] public string RegistryValueKind { get; set; } = "REG_SZ";
        [JsonPropertyName("registryValueData")] public JsonElement RegistryValueData { get; set; }
    }

    public JsonHivePlatformRegistry(string hiveFilePath, string? sandboxRoot = null, string? lockFilePath = null, TimeSpan? lockTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hiveFilePath);
        HiveFilePath = Path.GetFullPath(hiveFilePath);
        BackupFilePath = HiveFilePath + ".bak";
        string? regDir = Path.GetDirectoryName(HiveFilePath);
        string? inferredRoot = !string.IsNullOrEmpty(regDir) ? Path.GetDirectoryName(regDir) : null;
        SandboxRoot = !string.IsNullOrWhiteSpace(sandboxRoot) ? SandboxEnvironmentSeam.NormalizePath(sandboxRoot) : (inferredRoot ?? regDir ?? AppContext.BaseDirectory);
        LockFilePath = !string.IsNullOrWhiteSpace(lockFilePath) ? Path.GetFullPath(lockFilePath) : Path.Combine(SandboxRoot, "locks", "registry.hive.lock");
        MutexName = $"Local\\FoundationSandboxReg_{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(HiveFilePath.ToLowerInvariant())))[..8].ToLowerInvariant()}";
        _lockTimeout = lockTimeout ?? TimeSpan.FromSeconds(10);
    }

    public void EnsureInitialized() => ExecuteLocked(true, _ => true);

    public bool SubKeyExists(PlatformRegistryHive hive, string subKeyPath) => ExecuteLocked(false, hives =>
    {
        string norm = PlatformRegistryNaming.NormalizeSubKeyPath(subKeyPath), code = PlatformRegistryNaming.ToHiveCode(hive);
        return hives[code].ContainsKey(norm) || hives[code].Keys.Any(k => k.StartsWith(norm + "\\", StringComparison.OrdinalIgnoreCase));
    });

    public void CreateSubKey(PlatformRegistryHive hive, string subKeyPath) => ExecuteLocked(true, hives =>
    {
        string norm = PlatformRegistryNaming.NormalizeSubKeyPath(subKeyPath), code = PlatformRegistryNaming.ToHiveCode(hive);
        return hives[code].TryAdd(norm, new(StringComparer.OrdinalIgnoreCase));
    });

    public bool DeleteSubKeyTree(PlatformRegistryHive hive, string subKeyPath, bool throwOnMissingSubKey = false)
    {
        string norm = PlatformRegistryNaming.NormalizeSubKeyPath(subKeyPath), code = PlatformRegistryNaming.ToHiveCode(hive);
        return ExecuteLocked(true, hives =>
        {
            string prefix = norm + "\\";
            var matches = hives[code].Keys.Where(k => string.Equals(k, norm, StringComparison.OrdinalIgnoreCase) || k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
            {
                if (throwOnMissingSubKey) throw new ArgumentException($"Registry subkey '{norm}' does not exist.", nameof(subKeyPath));
                return false;
            }
            foreach (string key in matches) hives[code].Remove(key);
            return true;
        });
    }

    public PlatformRegistryValue? GetValue(PlatformRegistryHive hive, string subKeyPath, string valueName) => ExecuteLocked(false, hives =>
    {
        string norm = PlatformRegistryNaming.NormalizeSubKeyPath(subKeyPath), code = PlatformRegistryNaming.ToHiveCode(hive);
        return hives[code].TryGetValue(norm, out var vals) && vals.TryGetValue(valueName ?? string.Empty, out var val) ? val : null;
    });

    public void SetValue(PlatformRegistryHive hive, string subKeyPath, string valueName, object? value, PlatformRegistryValueKind valueKind = PlatformRegistryValueKind.String) =>
        SetValues(hive, subKeyPath, new Dictionary<string, PlatformRegistryValue> { [valueName ?? string.Empty] = new(valueKind, value) });

    public void SetValues(PlatformRegistryHive hive, string subKeyPath, IReadOnlyDictionary<string, PlatformRegistryValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        string norm = PlatformRegistryNaming.NormalizeSubKeyPath(subKeyPath), code = PlatformRegistryNaming.ToHiveCode(hive);
        ExecuteLocked(true, hives =>
        {
            if (!hives[code].TryGetValue(norm, out var vals)) hives[code][norm] = vals = new(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in values) vals[kv.Key] = NormalizeTypedValue(kv.Value.Kind, kv.Value.Data);
            return true;
        });
    }

    public bool DeleteValue(PlatformRegistryHive hive, string subKeyPath, string valueName, bool throwOnMissingValue = false)
    {
        string norm = PlatformRegistryNaming.NormalizeSubKeyPath(subKeyPath), code = PlatformRegistryNaming.ToHiveCode(hive), name = valueName ?? string.Empty;
        return ExecuteLocked(true, hives =>
        {
            if (hives[code].TryGetValue(norm, out var vals) && vals.Remove(name)) return true;
            if (throwOnMissingValue) throw new ArgumentException($"Registry value '{name}' does not exist under '{norm}'.", nameof(valueName));
            return false;
        });
    }

    public IReadOnlyList<string> GetSubKeyNames(PlatformRegistryHive hive, string subKeyPath) => ExecuteLocked(false, hives =>
    {
        string norm = PlatformRegistryNaming.NormalizeSubKeyPath(subKeyPath), prefix = norm + "\\", code = PlatformRegistryNaming.ToHiveCode(hive);
        return hives[code].Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => { string r = k[prefix.Length..]; int s = r.IndexOf('\\'); return s >= 0 ? r[..s] : r; })
            .Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToArray();
    });

    public IReadOnlyList<string> GetValueNames(PlatformRegistryHive hive, string subKeyPath) => GetValues(hive, subKeyPath).Keys.ToArray();

    public IReadOnlyDictionary<string, PlatformRegistryValue> GetValues(PlatformRegistryHive hive, string subKeyPath) => ExecuteLocked(false, hives =>
    {
        string norm = PlatformRegistryNaming.NormalizeSubKeyPath(subKeyPath), code = PlatformRegistryNaming.ToHiveCode(hive);
        return hives[code].TryGetValue(norm, out var vals)
            ? new Dictionary<string, PlatformRegistryValue>(vals, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, PlatformRegistryValue>(StringComparer.OrdinalIgnoreCase);
    });

    private T ExecuteLocked<T>(bool saveAfter, Func<HiveTree, T> action)
    {
        using var mutex = new Mutex(initiallyOwned: false, MutexName);
        bool hasMutex = false;
        var sw = Stopwatch.StartNew();
        try
        {
            try { hasMutex = mutex.WaitOne(_lockTimeout); } catch (AbandonedMutexException) { hasMutex = true; }
            if (!hasMutex) throw new TimeoutException($"Timed out acquiring sandbox registry mutex '{MutexName}'.");
            using PidSafeLockfile lockHandle = AcquireLockWithBackoff(sw);
            HiveTree hives = TryLoad(HiveFilePath) ?? TryLoad(BackupFilePath) ?? CreateEmptyHives();
            T result = action(hives);
            if (saveAfter) SaveHiveAtomically(hives);
            return result;
        }
        finally { if (hasMutex) try { mutex.ReleaseMutex(); } catch { } }
    }

    private PidSafeLockfile AcquireLockWithBackoff(Stopwatch sw)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (PidSafeLockfile.TryAcquire(LockFilePath, "foundation-sandbox", "registry-hive", out PidSafeLockfile? handle, out _) && handle is not null)
                    return handle;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            if (sw.Elapsed >= _lockTimeout) throw new TimeoutException($"Timed out acquiring sandbox registry lockfile '{LockFilePath}'.");
            Thread.Sleep(Math.Min(100, 15 * (attempt + 1)));
        }
    }

    private static HiveTree? TryLoad(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            string raw = File.ReadAllText(path, Encoding.UTF8);
            var dto = string.IsNullOrWhiteSpace(raw) ? null : JsonSerializer.Deserialize<HiveDocumentDto>(raw, JsonOpts);
            if (dto?.RegistryHives is null) return null;
            HiveTree hives = CreateEmptyHives();
            foreach (string code in HiveCodes)
            {
                if (!dto.RegistryHives.TryGetValue(code, out var subKeys) || subKeys is null) continue;
                foreach (var sk in subKeys)
                {
                    var valMap = new Dictionary<string, PlatformRegistryValue>(StringComparer.OrdinalIgnoreCase);
                    if (sk.Value is not null)
                        foreach (var vp in sk.Value)
                        {
                            var kind = PlatformRegistryNaming.ParseKindCode(vp.Value?.RegistryValueKind);
                            valMap[vp.Key] = NormalizeTypedValue(kind, ParseData(kind, vp.Value?.RegistryValueData ?? default));
                        }
                    hives[code][PlatformRegistryNaming.NormalizeSubKeyPath(sk.Key)] = valMap;
                }
            }
            return hives;
        }
        catch { return null; }
    }

    private void SaveHiveAtomically(HiveTree hives)
    {
        string? dir = Path.GetDirectoryName(HiveFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var dto = new HiveDocumentDto { SandboxRoot = SandboxRoot, UpdatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) };
        foreach (string code in HiveCodes)
            dto.RegistryHives[code] = hives[code].OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(
                sk => sk.Key,
                sk => sk.Value.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(
                    vp => vp.Key,
                    vp => new HiveEntryDto { RegistryValueKind = PlatformRegistryNaming.ToKindCode(vp.Value.Kind), RegistryValueData = ToElement(vp.Value) }));

        string tempPath = $"{HiveFilePath}.{Guid.NewGuid():N}.tmp";
        byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto, JsonOpts));
        try
        {
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None)) { fs.Write(payload, 0, payload.Length); fs.Flush(true); }
            if (File.Exists(HiveFilePath)) try { File.Copy(HiveFilePath, BackupFilePath, true); } catch { }
            for (int attempt = 1; ; attempt++)
            {
                try { File.Move(tempPath, HiveFilePath, true); break; }
                catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 4) { Thread.Sleep(15 * attempt); }
            }
        }
        finally { if (File.Exists(tempPath)) try { File.Delete(tempPath); } catch { } }
    }

    private static JsonElement ToElement(PlatformRegistryValue val) => val.Kind switch
    {
        PlatformRegistryValueKind.DWord => JsonSerializer.SerializeToElement(val.AsInt32() ?? 0),
        PlatformRegistryValueKind.QWord => JsonSerializer.SerializeToElement(val.AsInt64() ?? 0L),
        PlatformRegistryValueKind.MultiString => JsonSerializer.SerializeToElement(val.AsMultiString() ?? Array.Empty<string>()),
        PlatformRegistryValueKind.Binary => JsonSerializer.SerializeToElement(Convert.ToHexString(val.AsBinary() ?? Array.Empty<byte>()).ToLowerInvariant()),
        _ => JsonSerializer.SerializeToElement(val.AsString() ?? string.Empty)
    };

    private static object? ParseData(PlatformRegistryValueKind kind, JsonElement el) => el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : kind switch
    {
        PlatformRegistryValueKind.DWord => el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out long d) ? unchecked((int)d) : int.Parse(el.ToString(), CultureInfo.InvariantCulture),
        PlatformRegistryValueKind.QWord => el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out long q) ? q : long.Parse(el.ToString(), CultureInfo.InvariantCulture),
        PlatformRegistryValueKind.MultiString => el.ValueKind == JsonValueKind.Array ? el.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray() : new[] { el.ToString() },
        PlatformRegistryValueKind.Binary => Convert.FromHexString((el.GetString() ?? string.Empty).Trim()),
        _ => el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString()
    };

    private static PlatformRegistryValue NormalizeTypedValue(PlatformRegistryValueKind kind, object? value) => kind switch
    {
        PlatformRegistryValueKind.DWord => new(kind, new PlatformRegistryValue(kind, value).AsInt32() ?? 0),
        PlatformRegistryValueKind.QWord => new(kind, new PlatformRegistryValue(kind, value).AsInt64() ?? 0L),
        PlatformRegistryValueKind.MultiString => new(kind, new PlatformRegistryValue(kind, value).AsMultiString() ?? Array.Empty<string>()),
        PlatformRegistryValueKind.Binary => new(kind, new PlatformRegistryValue(kind, value).AsBinary() ?? Array.Empty<byte>()),
        _ => new(kind, value?.ToString() ?? string.Empty)
    };

    private static HiveTree CreateEmptyHives() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["HKCU"] = new(StringComparer.OrdinalIgnoreCase), ["HKLM"] = new(StringComparer.OrdinalIgnoreCase), ["HKCR"] = new(StringComparer.OrdinalIgnoreCase)
    };
}
