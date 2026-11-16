// @module: Foundation.Core.SandboxDriveController
// @purpose: Explicit opt-in .env/.secrets\.env loader, active subst drive mapper, 4-drive mount resolver, and host collision validator for sandbox tooling.
// @depends-on: System, System.Collections.Generic, System.IO, System.Text
// @exports: SandboxDriveController

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Foundation.Core;

/// <summary>
/// Explicit opt-in tooling controller for reading .env / .secrets\.env drive overrides, querying active Windows subst mounts,
/// resolving 4-drive sandbox mount descriptors, and enforcing host-collision safety.
/// Never invoked implicitly by AppEnvironment or SandboxEnvironmentSeam.
/// </summary>
public static class SandboxDriveController
{
    private static readonly (string Role, string DefaultSubDir)[] CanonicalDriveRoles =
    {
        (SandboxEnvironmentSeam.EnvSandboxCDrive, "C"),
        (SandboxEnvironmentSeam.EnvSandboxDDrive, "D"),
        (SandboxEnvironmentSeam.EnvSandboxRemovableDrive, "Removable"),
        (SandboxEnvironmentSeam.EnvSandboxDiscDrive, "Disc")
    };

    public static string GetDefaultUserSecretsEnvPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".gemini",
            ".secrets",
            ".env");

    public static Dictionary<string, string?> LoadEnvStore(
        string? devEnvPath = null,
        string? secretsEnvPath = null,
        bool includeUserSecretsFallback = false,
        IReadOnlyDictionary<string, string?>? cliOverrides = null,
        IReadOnlyDictionary<string, string?>? processEnvOverride = null)
    {
        var merged = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        string? resolvedSecrets = !string.IsNullOrWhiteSpace(secretsEnvPath)
            ? Path.GetFullPath(secretsEnvPath)
            : (includeUserSecretsFallback ? GetDefaultUserSecretsEnvPath() : null);

        if (!string.IsNullOrWhiteSpace(resolvedSecrets) && File.Exists(resolvedSecrets))
            MergeEnvValues(merged, ParseEnvFile(resolvedSecrets), overwriteEmpty: false);

        if (!string.IsNullOrWhiteSpace(devEnvPath) && File.Exists(devEnvPath))
            MergeEnvValues(merged, ParseEnvFile(devEnvPath), overwriteEmpty: false);

        IReadOnlyDictionary<string, string?> processEnv = processEnvOverride ?? SandboxEnvironmentSeam.GetProcessEnvironment();
        MergeEnvValues(merged, processEnv, overwriteEmpty: false);

        if (cliOverrides is not null)
            MergeEnvValues(merged, cliOverrides, overwriteEmpty: true);

        return merged;
    }

    public static Dictionary<string, string?> ParseEnvFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(filePath)) return result;

        foreach (string rawLine in File.ReadAllLines(filePath, Encoding.UTF8))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
            if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
                line = line[7..].TrimStart();

            int eqIdx = line.IndexOf('=');
            if (eqIdx <= 0) continue;

            string key = line[..eqIdx].Trim();
            string rawVal = line[(eqIdx + 1)..].Trim();
            string value = UnquoteEnvValue(rawVal);
            result[key] = string.IsNullOrEmpty(value) ? null : value;
        }
        return result;
    }

    public static IReadOnlyDictionary<string, string> GetSubstMappings()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows()) return map;

        for (char letter = 'A'; letter <= 'Z'; letter++)
        {
            string token = $"{letter}:";
            string? backing = SandboxEnvironmentSeam.TryResolveSubstBackingDirectory(token);
            if (!string.IsNullOrWhiteSpace(backing))
                map[token] = backing;
        }
        return map;
    }

    public static IReadOnlyDictionary<string, string> ParseSubstOutput(string? substStdout)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(substStdout)) return map;

        foreach (string rawLine in substStdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int arrowIdx = rawLine.IndexOf("=>", StringComparison.Ordinal);
            if (arrowIdx <= 0) continue;

            string left = rawLine[..arrowIdx].Trim();
            string right = rawLine[(arrowIdx + 2)..].Trim();
            if (left.Length >= 2 && char.IsAsciiLetter(left[0]) && left[1] == ':' && !string.IsNullOrWhiteSpace(right))
            {
                string token = $"{char.ToUpperInvariant(left[0])}:";
                map[token] = SandboxEnvironmentSeam.NormalizePath(right);
            }
        }
        return map;
    }

    public static string? InferSandboxRoot(
        IReadOnlyDictionary<string, string?>? envStore = null,
        IReadOnlyDictionary<string, string>? substMappings = null)
    {
        if (envStore is not null && envStore.TryGetValue(SandboxEnvironmentSeam.EnvSandboxRoot, out string? explicitRoot) &&
            !string.IsNullOrWhiteSpace(explicitRoot))
        {
            return SandboxEnvironmentSeam.NormalizePath(explicitRoot);
        }

        IReadOnlyDictionary<string, string> activeSubst = substMappings ?? GetSubstMappings();
        if (envStore is not null)
        {
            foreach ((string role, _) in CanonicalDriveRoles)
            {
                if (!envStore.TryGetValue(role, out string? rawMount) || string.IsNullOrWhiteSpace(rawMount)) continue;
                string? backing = ResolveBackingDirectory(SandboxEnvironmentSeam.NormalizePath(rawMount), activeSubst);
                if (!string.IsNullOrWhiteSpace(backing))
                {
                    string? parent = Path.GetDirectoryName(backing);
                    if (!string.IsNullOrWhiteSpace(parent)) return SandboxEnvironmentSeam.NormalizePath(parent);
                }
            }
        }
        return null;
    }

    public static IReadOnlyList<SandboxMountSpec> ResolveDriveMounts(
        IReadOnlyDictionary<string, string?>? envStore = null,
        IReadOnlyDictionary<string, string>? substMappings = null)
    {
        IReadOnlyDictionary<string, string?> store = envStore ?? SandboxEnvironmentSeam.GetProcessEnvironment();
        IReadOnlyDictionary<string, string> activeSubst = substMappings ?? GetSubstMappings();
        string? sandboxRoot = InferSandboxRoot(store, activeSubst);
        var specs = new List<SandboxMountSpec>(CanonicalDriveRoles.Length);

        foreach ((string role, string defaultSubDir) in CanonicalDriveRoles)
        {
            store.TryGetValue(role, out string? rawMount);
            string driveMount = !string.IsNullOrWhiteSpace(rawMount)
                ? SandboxEnvironmentSeam.NormalizePath(rawMount)
                : string.Empty;

            string? directoryPath = !string.IsNullOrEmpty(driveMount)
                ? ResolveBackingDirectory(driveMount, activeSubst)
                : null;

            if (string.IsNullOrWhiteSpace(directoryPath) && !string.IsNullOrWhiteSpace(sandboxRoot))
            {
                directoryPath = SandboxEnvironmentSeam.NormalizePath(Path.Combine(sandboxRoot, defaultSubDir));
            }

            bool isEnabled = !string.IsNullOrEmpty(driveMount) &&
                (IsMountedInSubstOrLive(driveMount, activeSubst, substMappings is not null));

            specs.Add(new SandboxMountSpec(role, driveMount, directoryPath, isEnabled));
        }

        return specs;
    }

    public static void ValidateNoHostCollisions(SandboxResolution resolution) =>
        SandboxEnvironmentSeam.ValidateResolution(resolution);

    public static SandboxResolution ValidateNoHostCollisions(IReadOnlyDictionary<string, string?> envStore)
    {
        ArgumentNullException.ThrowIfNull(envStore);
        return SandboxEnvironmentSeam.Resolve(envStore);
    }

    private static string? ResolveBackingDirectory(string normalizedMount, IReadOnlyDictionary<string, string> substMappings)
    {
        if (normalizedMount.Length == 3 && normalizedMount[1] == ':' && normalizedMount[2] == '\\')
        {
            string token = normalizedMount[..2];
            return substMappings.TryGetValue(token, out string? backing) ? backing : null;
        }
        return normalizedMount;
    }

    private static bool IsMountedInSubstOrLive(
        string normalizedMount,
        IReadOnlyDictionary<string, string> activeSubst,
        bool usingExplicitSubstMap)
    {
        if (normalizedMount.Length == 3 && normalizedMount[1] == ':' && normalizedMount[2] == '\\')
        {
            string token = normalizedMount[..2];
            if (activeSubst.ContainsKey(token)) return true;
            return !usingExplicitSubstMap && Directory.Exists(normalizedMount);
        }
        return Directory.Exists(normalizedMount);
    }

    private static void MergeEnvValues(
        Dictionary<string, string?> target,
        IReadOnlyDictionary<string, string?> source,
        bool overwriteEmpty)
    {
        foreach (KeyValuePair<string, string?> kv in source)
        {
            if (string.IsNullOrWhiteSpace(kv.Key)) continue;
            if (!overwriteEmpty && string.IsNullOrWhiteSpace(kv.Value)) continue;
            target[kv.Key.Trim()] = string.IsNullOrWhiteSpace(kv.Value) ? null : kv.Value.Trim();
        }
    }

    private static string UnquoteEnvValue(string raw)
    {
        if (raw.Length >= 2 &&
            ((raw.StartsWith('"') && raw.EndsWith('"')) || (raw.StartsWith('\'') && raw.EndsWith('\''))))
        {
            return raw[1..^1];
        }

        int inlineCommentIdx = raw.IndexOf(" #", StringComparison.Ordinal);
        return (inlineCommentIdx >= 0 ? raw[..inlineCommentIdx] : raw).Trim();
    }
}
