// @module: Foundation.Core.Settings
// @purpose: Implements Standard 19 §2 atomic .tmp -> .bak JSON settings persistence, corrupt-file self-healing, and 20-snapshot config_history rotation.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Text, System.Text.Json
// @exports: SettingsLoadOutcome, GranularSettingsManager

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foundation.Core;

public sealed record SettingsLoadOutcome<T>(
    T Value,
    bool LoadedFromPrimary,
    bool HealedFromBackup,
    bool CreatedDefault,
    string? DiagnosticWarning = null
);

/// <summary>
/// Manages granular domain settings files (settings.json, settings.notifications.json, settings.scheduler.json)
/// with atomic .tmp -> .bak -> replace writes, corrupt JSON self-healing from .bak, and 20-snapshot rotation in data\config_history\.
/// </summary>
public sealed class GranularSettingsManager
{
    public const int DefaultMaxHistorySnapshots = 20;

    private static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string DataDirectory { get; }
    public string ConfigHistoryDirectory { get; }
    public int MaxHistorySnapshots { get; }

    public GranularSettingsManager(string dataDirectory, int maxHistorySnapshots = DefaultMaxHistorySnapshots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        DataDirectory = Path.GetFullPath(dataDirectory);
        ConfigHistoryDirectory = Path.Combine(DataDirectory, "config_history");
        MaxHistorySnapshots = Math.Max(1, maxHistorySnapshots);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ConfigHistoryDirectory);
    }

    public string ResolveSettingsPath(string fileName = "settings.json")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return Path.Combine(DataDirectory, fileName);
    }

    public SettingsLoadOutcome<T> LoadOrSelfHeal<T>(
        string fileName,
        Func<T> defaultFactory,
        JsonSerializerOptions? customOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(defaultFactory);

        JsonSerializerOptions opts = customOptions ?? DefaultJsonOptions;
        string primaryPath = Path.IsPathRooted(fileName) ? Path.GetFullPath(fileName) : ResolveSettingsPath(fileName);
        string bakPath = primaryPath + ".bak";

        if (File.Exists(primaryPath))
        {
            try
            {
                string raw = File.ReadAllText(primaryPath, Encoding.UTF8);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    T? parsed = JsonSerializer.Deserialize<T>(raw, opts);
                    if (parsed is not null)
                    {
                        return new SettingsLoadOutcome<T>(parsed, true, false, false);
                    }
                }
            }
            catch (Exception primaryEx)
            {
                if (TryRestoreFromBackup(primaryPath, bakPath, opts, out T? restored) && restored is not null)
                {
                    return new SettingsLoadOutcome<T>(
                        restored,
                        false,
                        true,
                        false,
                        $"Primary settings file '{Path.GetFileName(primaryPath)}' was corrupt ({primaryEx.Message}); self-healed from '.bak'.");
                }

                T fallback = defaultFactory();
                SaveAtomically(primaryPath, fallback, opts);
                return new SettingsLoadOutcome<T>(
                    fallback,
                    false,
                    false,
                    true,
                    $"Primary settings file '{Path.GetFileName(primaryPath)}' and backup were unreadable; initialized clean defaults.");
            }
        }

        if (File.Exists(bakPath) && TryRestoreFromBackup(primaryPath, bakPath, opts, out T? fromBak) && fromBak is not null)
        {
            return new SettingsLoadOutcome<T>(
                fromBak,
                false,
                true,
                false,
                $"Primary settings file '{Path.GetFileName(primaryPath)}' was missing; restored from '.bak'.");
        }

        T created = defaultFactory();
        SaveAtomically(primaryPath, created, opts);
        return new SettingsLoadOutcome<T>(created, false, false, true);
    }

    public void SaveAtomically<T>(
        string fileNameOrPath,
        T settingsObject,
        JsonSerializerOptions? customOptions = null,
        bool recordSnapshot = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileNameOrPath);
        ArgumentNullException.ThrowIfNull(settingsObject);

        JsonSerializerOptions opts = customOptions ?? DefaultJsonOptions;
        string targetPath = Path.IsPathRooted(fileNameOrPath)
            ? Path.GetFullPath(fileNameOrPath)
            : ResolveSettingsPath(fileNameOrPath);

        string? parentDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        string tempPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";
        string bakPath = targetPath + ".bak";
        string json = JsonSerializer.Serialize(settingsObject, opts);

        try
        {
            File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (File.Exists(targetPath))
            {
                try
                {
                    File.Copy(targetPath, bakPath, overwrite: true);
                }
                catch
                {
                    // Continue atomic replace even if .bak copy fails
                }
            }

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(tempPath, targetPath, overwrite: true);
                    break;
                }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 3)
                {
                    System.Threading.Thread.Sleep(15 * attempt);
                }
            }

            if (recordSnapshot)
            {
                WriteHistorySnapshotAndRotate(Path.GetFileName(targetPath), json);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* ignore */ }
            }
        }
    }

    public IReadOnlyList<string> GetHistorySnapshots(string fileName = "settings.json")
    {
        string prefix = Path.GetFileNameWithoutExtension(fileName) + "_";
        if (!Directory.Exists(ConfigHistoryDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(ConfigHistoryDirectory, $"{prefix}*.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void WriteHistorySnapshotAndRotate(string fileName, string jsonPayload)
    {
        try
        {
            Directory.CreateDirectory(ConfigHistoryDirectory);
            string stem = Path.GetFileNameWithoutExtension(fileName);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            string snapshotPath = Path.Combine(ConfigHistoryDirectory, $"{stem}_{stamp}_{Guid.NewGuid().ToString("N")[..6]}.json");
            File.WriteAllText(snapshotPath, jsonPayload, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            List<string> existing = Directory.EnumerateFiles(ConfigHistoryDirectory, $"{stem}_*.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (int i = MaxHistorySnapshots; i < existing.Count; i++)
            {
                try { File.Delete(existing[i]); } catch { /* ignore */ }
            }
        }
        catch
        {
            // Snapshot pruning must never crash foreground settings saves
        }
    }

    private static bool TryRestoreFromBackup<T>(
        string primaryPath,
        string bakPath,
        JsonSerializerOptions opts,
        out T? restored)
    {
        restored = default;
        if (!File.Exists(bakPath))
        {
            return false;
        }

        try
        {
            string rawBak = File.ReadAllText(bakPath, Encoding.UTF8);
            restored = JsonSerializer.Deserialize<T>(rawBak, opts);
            if (restored is null)
            {
                return false;
            }
            File.Copy(bakPath, primaryPath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
