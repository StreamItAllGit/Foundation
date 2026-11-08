// @module: Foundation.Core.Security
// @purpose: Encrypts and decrypts sensitive tokens/webhooks at rest in data\secrets.dpapi using Windows DPAPI CurrentUser scope.
// @depends-on: System, System.Collections.Generic, System.IO, System.Security.Cryptography, System.Text, System.Text.Json
// @exports: DpapiSecretStore

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Foundation.Core;

/// <summary>
/// Stores sensitive secrets (Discord webhooks, API tokens, credentials) encrypted at rest in data\secrets.dpapi
/// using Windows DPAPI (DataProtectionScope.CurrentUser) per Standard 19 §2.3 so secrets never appear in plaintext settings.json.
/// </summary>
public sealed class DpapiSecretStore
{
    private static readonly byte[] EntropyBytes = Encoding.UTF8.GetBytes("Foundation.Core.DpapiSecretStore.v1");

    public string SecretsFilePath { get; }
    private readonly object _sync = new();

    public DpapiSecretStore(string dataDirectory, string fileName = "secrets.dpapi")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        string fullDir = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(fullDir);
        SecretsFilePath = Path.Combine(fullDir, fileName);
    }

    public void SetSecret(string key, string? secretValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_sync)
        {
            Dictionary<string, string> map = LoadSecretsDictionaryUnsynchronized();
            if (string.IsNullOrWhiteSpace(secretValue))
            {
                map.Remove(key.Trim());
            }
            else
            {
                map[key.Trim()] = secretValue;
            }
            SaveSecretsDictionaryUnsynchronized(map);
        }
    }

    public string? GetSecret(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        lock (_sync)
        {
            Dictionary<string, string> map = LoadSecretsDictionaryUnsynchronized();
            return map.TryGetValue(key.Trim(), out string? val) ? val : null;
        }
    }

    public bool RemoveSecret(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        lock (_sync)
        {
            Dictionary<string, string> map = LoadSecretsDictionaryUnsynchronized();
            bool removed = map.Remove(key.Trim());
            if (removed)
            {
                SaveSecretsDictionaryUnsynchronized(map);
            }
            return removed;
        }
    }

    public IReadOnlyCollection<string> GetSecretKeys()
    {
        lock (_sync)
        {
            Dictionary<string, string> map = LoadSecretsDictionaryUnsynchronized();
            return new List<string>(map.Keys);
        }
    }

    private Dictionary<string, string> LoadSecretsDictionaryUnsynchronized()
    {
        if (!File.Exists(SecretsFilePath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            byte[] cipherBytes = File.ReadAllBytes(SecretsFilePath);
            if (cipherBytes.Length == 0)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            byte[] plainBytes = UnprotectBytes(cipherBytes);
            string json = Encoding.UTF8.GetString(plainBytes);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return parsed is not null
                ? new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveSecretsDictionaryUnsynchronized(Dictionary<string, string> secrets)
    {
        string json = JsonSerializer.Serialize(secrets);
        byte[] plainBytes = Encoding.UTF8.GetBytes(json);
        byte[] cipherBytes = ProtectBytes(plainBytes);

        string tempPath = $"{SecretsFilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(tempPath, cipherBytes);
            File.Move(tempPath, SecretsFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* ignore */ }
            }
        }
    }

    private static byte[] ProtectBytes(byte[] plainBytes)
    {
        if (OperatingSystem.IsWindows())
        {
            return ProtectedData.Protect(plainBytes, EntropyBytes, DataProtectionScope.CurrentUser);
        }
        return plainBytes;
    }

    private static byte[] UnprotectBytes(byte[] cipherBytes)
    {
        if (OperatingSystem.IsWindows())
        {
            return ProtectedData.Unprotect(cipherBytes, EntropyBytes, DataProtectionScope.CurrentUser);
        }
        return cipherBytes;
    }
}
