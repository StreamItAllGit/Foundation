// @module: Foundation.Installer.Ownership
// @purpose: Verifies install-manifest ownership across Tier 1 and Tier 2 installers so sibling products never overwrite or uninstall another product's directory.
// @depends-on: System, System.IO, System.Text, System.Text.Json
// @exports: InstallDirectoryOwnership, InstallOwnershipCheckResult, InstallerEngine (partial: IsOwnInstallManifest, TryReadInstallManifest, CheckDirectoryOwnership, BuildForeignOwnershipErrorMessage)

using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Foundation.Installer;

public enum InstallDirectoryOwnership
{
    EmptyOrClean,
    OwnedBySelf,
    OwnedByForeignProduct,
    UnownedArtifacts
}

public sealed record InstallOwnershipCheckResult(
    InstallDirectoryOwnership Status,
    string? ExistingProductName,
    InstallManifest? Manifest,
    string? ManifestPath
);

public sealed partial class InstallerEngine
{
    public static bool IsOwnInstallManifest(InstallManifest? manifest, string expectedProductName)
    {
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.ProductName) || string.IsNullOrWhiteSpace(expectedProductName))
            return false;

        return string.Equals(manifest.ProductName.Trim(), expectedProductName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsOwnInstallManifest(FoundationInstallManifest? manifest, string expectedAppId, string expectedDisplayName)
    {
        if (manifest is null) return false;
        if (!string.IsNullOrWhiteSpace(manifest.AppId) &&
            !string.IsNullOrWhiteSpace(expectedAppId) &&
            string.Equals(manifest.AppId.Trim(), expectedAppId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (!string.IsNullOrWhiteSpace(manifest.DisplayName) &&
            !string.IsNullOrWhiteSpace(expectedDisplayName) &&
            string.Equals(manifest.DisplayName.Trim(), expectedDisplayName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return false;
    }

    public static InstallManifest? TryReadInstallManifest(string? candidateDir) =>
        TryReadInstallManifest(candidateDir, out _);

    public static InstallManifest? TryReadInstallManifest(string? candidateDir, out string? manifestPath)
    {
        manifestPath = null;
        if (string.IsNullOrWhiteSpace(candidateDir))
            return null;

        try
        {
            string trimmed = candidateDir.Trim();
            if (!Directory.Exists(trimmed))
                return null;

            string[] candidates =
            {
                Path.Combine(trimmed, "app", InstallManifestFileName),
                Path.Combine(trimmed, InstallManifestFileName),
                Path.Combine(trimmed, "app", "uninstall_manifest.json"),
                Path.Combine(trimmed, "uninstall_manifest.json")
            };

            foreach (string path in candidates)
            {
                if (!File.Exists(path)) continue;
                manifestPath = path;
                string raw = File.ReadAllText(path, Encoding.UTF8);
                InstallManifest? parsed = JsonSerializer.Deserialize(raw, FoundationInstallerJsonContext.Default.InstallManifest);
                if (parsed is not null && !string.IsNullOrWhiteSpace(parsed.ProductName))
                {
                    return parsed;
                }

                // Fall back cleanly when reading a Tier 2 FoundationInstallManifest JSON schema.
                using var doc = JsonDocument.Parse(raw);
                string? fallbackProduct = null;
                if (doc.RootElement.TryGetProperty("displayName", out JsonElement dn) && dn.ValueKind == JsonValueKind.String)
                    fallbackProduct = dn.GetString();
                if (string.IsNullOrWhiteSpace(fallbackProduct) &&
                    doc.RootElement.TryGetProperty("appId", out JsonElement aid) && aid.ValueKind == JsonValueKind.String)
                    fallbackProduct = aid.GetString();
                if (!string.IsNullOrWhiteSpace(fallbackProduct))
                {
                    parsed ??= new InstallManifest();
                    parsed.ProductName = fallbackProduct.Trim();
                }
                return parsed;
            }
        }
        catch (Exception ex)
        {
            _ = ex; // Unreadable manifest files fail closed in directory-artifact checks.
        }

        return null;
    }

    public InstallOwnershipCheckResult CheckDirectoryOwnership(string? candidateDir)
    {
        if (string.IsNullOrWhiteSpace(candidateDir) || !Directory.Exists(candidateDir.Trim()))
        {
            return new InstallOwnershipCheckResult(InstallDirectoryOwnership.EmptyOrClean, null, null, null);
        }

        InstallManifest? existing = TryReadInstallManifest(candidateDir, out string? manifestPath);
        if (existing is not null && !string.IsNullOrWhiteSpace(existing.ProductName))
        {
            bool owned = IsOwnInstallManifest(existing, Descriptor.ProductName) ||
                         string.Equals(existing.ProductName.Trim(), Descriptor.DisplayName.Trim(), StringComparison.OrdinalIgnoreCase);
            return new InstallOwnershipCheckResult(
                owned ? InstallDirectoryOwnership.OwnedBySelf : InstallDirectoryOwnership.OwnedByForeignProduct,
                existing.ProductName.Trim(),
                existing,
                manifestPath);
        }

        bool hasArtifacts = HasExistingInstallationArtifacts(candidateDir, Descriptor.MainExecutableFileName, Descriptor.ProductName);
        return new InstallOwnershipCheckResult(
            hasArtifacts ? InstallDirectoryOwnership.UnownedArtifacts : InstallDirectoryOwnership.EmptyOrClean,
            null,
            existing,
            manifestPath);
    }

    public string BuildForeignOwnershipErrorMessage(string installDir, string existingProductName, bool isUninstall)
    {
        if (isUninstall)
        {
            return $"ProductMismatch: Directory '{installDir}' is owned by '{existingProductName}', not '{Descriptor.ProductName}'. " +
                   $"Refusing to uninstall or delete another product's files.";
        }

        string suggestedDir = Path.Combine(installDir, Descriptor.ProductName);
        return $"ProductMismatch: Directory '{installDir}' is already owned by '{existingProductName}', not '{Descriptor.ProductName}'. " +
               $"Refusing to overwrite a foreign installation. Select a product-specific target such as '{suggestedDir}'.";
    }

    internal static bool CheckForeignOwnershipForTheme(
        string? targetDir,
        string appId,
        string displayName,
        out string? existingOwner)
    {
        existingOwner = null;
        if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir.Trim()))
            return false;

        InstallManifest? manifest = TryReadInstallManifest(targetDir, out string? path);
        if (manifest is null) return false;

        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                var tier2 = JsonSerializer.Deserialize(File.ReadAllText(path, Encoding.UTF8), FoundationInstallerJsonContext.Default.FoundationInstallManifest);
                if (tier2 is not null && (!string.IsNullOrWhiteSpace(tier2.AppId) || !string.IsNullOrWhiteSpace(tier2.DisplayName)))
                {
                    if (IsOwnInstallManifest(tier2, appId, displayName)) return false;
                    existingOwner = !string.IsNullOrWhiteSpace(tier2.DisplayName) ? tier2.DisplayName.Trim() : tier2.AppId.Trim();
                    return true;
                }
            }
            catch (Exception ex) { _ = ex; }
        }

        if (!string.IsNullOrWhiteSpace(manifest.ProductName))
        {
            bool owned = string.Equals(manifest.ProductName.Trim(), appId.Trim(), StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(manifest.ProductName.Trim(), displayName.Trim(), StringComparison.OrdinalIgnoreCase);
            if (!owned)
            {
                existingOwner = manifest.ProductName.Trim();
                return true;
            }
        }
        return false;
    }
}
