// @module: Foundation.Core.ReleaseUpdater
// @purpose: Built-in GitHub-first + Cloudflare R2-fallback updater client with per-project PIN resolution and Standard 41 verified downloads.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Net.Http, System.Net.Http.Headers, System.Reflection, System.Text, System.Text.Json, System.Text.Json.Serialization, System.Threading, System.Threading.Tasks
// @exports: ReleaseUpdateManifest, ReleaseUpdateCheckResult, ReleaseUpdaterClient

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Foundation.Core;

public sealed record ReleaseUpdateManifest
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; } = 1;
    [JsonPropertyName("productName")] public string ProductName { get; init; } = string.Empty;
    [JsonPropertyName("projectSlug")] public string ProjectSlug { get; init; } = string.Empty;
    [JsonPropertyName("studio")] public string Studio { get; init; } = "oddlot";
    [JsonPropertyName("visibility")] public string Visibility { get; init; } = "private";
    [JsonPropertyName("channel")] public string Channel { get; init; } = "prerelease";
    [JsonPropertyName("currentVersion")] public string CurrentVersion { get; init; } = "0.1.0";
    [JsonPropertyName("primaryManifestUrl")] public string PrimaryManifestUrl { get; init; } = string.Empty;
    [JsonPropertyName("fallbackManifestUrls")] public List<string> FallbackManifestUrls { get; init; } = new();
    [JsonPropertyName("betaPin")] public string? BetaPin { get; init; }
}

public sealed record ReleaseUpdateCheckResult(
    bool UpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string SourceKind,
    string ManifestUrlUsed,
    string? DownloadUrl,
    IReadOnlyList<string> FallbackDownloadUrls,
    string? ExpectedFileName,
    long? ExpectedSizeBytes,
    string? ExpectedSha256,
    string? ReleaseNotes);

public static class ReleaseUpdaterClient
{
    public static string? ResolveBetaPin(
        string? explicitPin,
        string? productOrSlug,
        string? searchDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPin))
            return explicitPin.Trim();

        string dir = string.IsNullOrWhiteSpace(searchDirectory) ? AppContext.BaseDirectory : searchDirectory;
        foreach (string candidate in new[]
        {
            Path.Combine(dir, ".beta.manifest.local.json"),
            Path.Combine(dir, "Dev", ".beta.manifest.local.json")
        })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(candidate, Encoding.UTF8));
                if (doc.RootElement.TryGetProperty("betaPin", out JsonElement pinEl) &&
                    pinEl.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(pinEl.GetString()))
                {
                    return pinEl.GetString()!.Trim();
                }
            }
            catch
            {
                // Ignore malformed local override files and continue fallback chain.
            }
        }

        if (!string.IsNullOrWhiteSpace(productOrSlug))
        {
            string norm = new string(productOrSlug.Trim().ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            foreach (string key in new[] { $"BETA_PIN_{norm}", $"{norm}_BETA_PIN" })
            {
                string? val = Environment.GetEnvironmentVariable(key);
                if (!string.IsNullOrWhiteSpace(val))
                    return val.Trim();
            }
        }

        return null;
    }

    public static ReleaseUpdateManifest? LoadManifest(
        string? appDirectory = null,
        Assembly? assembly = null,
        string? productName = null)
    {
        string dir = string.IsNullOrWhiteSpace(appDirectory) ? AppContext.BaseDirectory : appDirectory;
        string diskPath = Path.Combine(dir, "update.manifest.json");
        ReleaseUpdateManifest? parsed = null;

        if (File.Exists(diskPath))
        {
            parsed = JsonSerializer.Deserialize(File.ReadAllText(diskPath, Encoding.UTF8), FoundationCoreJsonContext.Default.ReleaseUpdateManifest);
        }
        else if (assembly is not null)
        {
            string? resName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("update.manifest.json", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(resName))
            {
                using Stream? stream = assembly.GetManifestResourceStream(resName);
                if (stream is not null)
                {
                    using var sr = new StreamReader(stream, Encoding.UTF8);
                    parsed = JsonSerializer.Deserialize(sr.ReadToEnd(), FoundationCoreJsonContext.Default.ReleaseUpdateManifest);
                }
            }
        }

        if (parsed is null) return null;
        string? pin = ResolveBetaPin(parsed.BetaPin, productName ?? parsed.ProjectSlug, dir);
        return parsed with { BetaPin = pin };
    }

    public static async Task<ReleaseUpdateCheckResult> CheckForUpdateAsync(
        ReleaseUpdateManifest manifest,
        string? preferredAssetSuffix = "-Portable.zip",
        bool allowLoopbackHttpForTesting = false,
        HttpMessageHandler? customHandler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        string? effectivePin = ResolveBetaPin(manifest.BetaPin, manifest.ProjectSlug);
        var urls = new List<string>();
        if (!string.IsNullOrWhiteSpace(manifest.PrimaryManifestUrl))
            urls.Add(manifest.PrimaryManifestUrl.Trim());
        if (manifest.FallbackManifestUrls is { Count: > 0 })
            urls.AddRange(manifest.FallbackManifestUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()));

        var errors = new List<string>();
        var parsedEntries = new List<(string SourceKind, string Url, string Version, string? Notes, List<ParsedAsset> Assets)>();

        using var client = customHandler is not null
            ? new HttpClient(customHandler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(15) }
            : new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        foreach (string rawUrl in urls)
        {
            string resolved = ApplyPinPlaceholder(rawUrl, effectivePin);
            if (!Uri.TryCreate(resolved, UriKind.Absolute, out Uri? uri)) continue;

            try
            {
                SafeNetworkTransfer.ValidateRequestUri(uri, requireHttps: true, allowLoopbackHttp: allowLoopbackHttpForTesting);
                using var req = new HttpRequestMessage(HttpMethod.Get, uri);
                req.Headers.UserAgent.Add(new ProductInfoHeaderValue("FoundationUpdater", "1.0"));
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (!string.IsNullOrWhiteSpace(effectivePin) && !IsGitHubHost(uri.Host))
                {
                    req.Headers.TryAddWithoutValidation("X-Beta-Pin", effectivePin);
                }

                using HttpResponseMessage resp = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    errors.Add($"{uri.Host}: HTTP {(int)resp.StatusCode}");
                    continue;
                }

                string json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (IsGitHubHost(uri.Host))
                {
                    var gh = ParseGitHubReleaseJson(json, manifest.Channel);
                    if (gh is not null)
                        parsedEntries.Add(("github", resolved, gh.Value.Version, gh.Value.Notes, gh.Value.Assets));
                    else
                        errors.Add($"{uri.Host}: no matching release payload");
                }
                else
                {
                    var r2 = ParseReleaseHubJson(json);
                    if (r2 is not null)
                        parsedEntries.Add(("r2", resolved, r2.Value.Version, r2.Value.Notes, r2.Value.Assets));
                    else
                        errors.Add($"{uri.Host}: invalid latest.json payload");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                errors.Add($"{uri.Host}: {ex.Message}");
            }
        }

        if (parsedEntries.Count == 0)
        {
            throw new IOException($"All update endpoints failed for {manifest.ProductName}: {string.Join("; ", errors)}");
        }

        var primary = parsedEntries[0];
        ParsedAsset? primaryAsset = PickPreferredAsset(primary.Assets, preferredAssetSuffix);
        var fallbackDownloads = new List<string>();
        foreach (var extra in parsedEntries.Skip(1))
        {
            ParsedAsset? match = extra.Assets.FirstOrDefault(a =>
                primaryAsset is not null && string.Equals(a.Name, primaryAsset.Name, StringComparison.OrdinalIgnoreCase))
                ?? PickPreferredAsset(extra.Assets, preferredAssetSuffix);
            if (match is not null)
            {
                fallbackDownloads.Add(ApplyPinPlaceholder(match.Url, effectivePin));
            }
        }

        bool isNewer = CompareSemVer(primary.Version, manifest.CurrentVersion) > 0;
        return new ReleaseUpdateCheckResult(
            UpdateAvailable: isNewer,
            CurrentVersion: manifest.CurrentVersion,
            LatestVersion: primary.Version,
            SourceKind: primary.SourceKind,
            ManifestUrlUsed: primary.Url,
            DownloadUrl: primaryAsset?.Url is not null ? ApplyPinPlaceholder(primaryAsset.Url, effectivePin) : null,
            FallbackDownloadUrls: fallbackDownloads,
            ExpectedFileName: primaryAsset?.Name,
            ExpectedSizeBytes: primaryAsset?.Size,
            ExpectedSha256: primaryAsset?.Sha256,
            ReleaseNotes: primary.Notes);
    }

    public static async Task<SafeTransferResult> DownloadVerifiedUpdateAsync(
        ReleaseUpdateCheckResult update,
        string destinationFilePath,
        string? betaPin = null,
        Action<long, long?>? onProgress = null,
        bool allowLoopbackHttpForTesting = false,
        HttpMessageHandler? customHandler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(update.DownloadUrl))
            candidates.Add(update.DownloadUrl);
        if (update.FallbackDownloadUrls is { Count: > 0 })
            candidates.AddRange(update.FallbackDownloadUrls.Where(u => !string.IsNullOrWhiteSpace(u)));

        if (candidates.Count == 0)
            throw new InvalidOperationException("No download URLs available in update check result.");

        var headers = !string.IsNullOrWhiteSpace(betaPin)
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Beta-Pin"] = betaPin.Trim() }
            : null;

        var errors = new List<string>();
        foreach (string rawUrl in candidates)
        {
            string resolved = ApplyPinPlaceholder(rawUrl, betaPin);
            if (!Uri.TryCreate(resolved, UriKind.Absolute, out Uri? uri)) continue;
            try
            {
                var opts = new SafeTransferOptions
                {
                    JobId = $"upd_{Guid.NewGuid():N}",
                    RequireHttps = true,
                    AllowLoopbackHttpForTesting = allowLoopbackHttpForTesting,
                    ExpectedSizeBytes = update.ExpectedSizeBytes is > 0 ? update.ExpectedSizeBytes : null,
                    ExpectedSha256Hex = string.IsNullOrWhiteSpace(update.ExpectedSha256) ? null : update.ExpectedSha256,
                    OnBytesProgress = onProgress,
                    RequestHeaders = !IsGitHubHost(uri.Host) ? headers : null
                };
                return await SafeNetworkTransfer.DownloadFileAsync(uri, destinationFilePath, opts, customHandler, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                errors.Add($"{uri.Host}: {ex.Message}");
            }
        }

        throw new IOException($"All update download mirrors failed: {string.Join("; ", errors)}");
    }

    public static int CompareSemVer(string left, string right)
    {
        (int[] LNums, string? LPre) = ParseSemVer(left);
        (int[] RNums, string? RPre) = ParseSemVer(right);
        for (int i = 0; i < 3; i++)
        {
            if (LNums[i] != RNums[i]) return LNums[i].CompareTo(RNums[i]);
        }
        if (string.IsNullOrEmpty(LPre) && string.IsNullOrEmpty(RPre)) return 0;
        if (string.IsNullOrEmpty(LPre)) return 1;
        if (string.IsNullOrEmpty(RPre)) return -1;
        return string.Compare(LPre, RPre, StringComparison.OrdinalIgnoreCase);
    }

    private static (int[] Nums, string? Pre) ParseSemVer(string raw)
    {
        string s = (raw ?? string.Empty).Trim().TrimStart('v', 'V');
        int plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        int dash = s.IndexOf('-');
        string core = dash >= 0 ? s[..dash] : s;
        string? pre = dash >= 0 ? s[(dash + 1)..] : null;
        string[] parts = core.Split('.');
        int[] nums = new int[3];
        for (int i = 0; i < 3 && i < parts.Length; i++)
        {
            int.TryParse(parts[i], out nums[i]);
        }
        return (nums, pre);
    }

    private static string ApplyPinPlaceholder(string url, string? pin) =>
        string.IsNullOrWhiteSpace(pin) ? url : url.Replace("{pin}", Uri.EscapeDataString(pin.Trim()), StringComparison.OrdinalIgnoreCase);

    private static bool IsGitHubHost(string host) =>
        host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);

    private sealed record ParsedAsset(string Name, string Url, long? Size, string? Sha256);

    private static ParsedAsset? PickPreferredAsset(List<ParsedAsset> assets, string? preferredSuffix)
    {
        if (assets.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(preferredSuffix))
        {
            ParsedAsset? match = assets.FirstOrDefault(a => a.Name.EndsWith(preferredSuffix, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        return assets[0];
    }

    private static (string Version, string? Notes, List<ParsedAsset> Assets)? ParseGitHubReleaseJson(
        string json,
        string desiredChannel)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement rel = doc.RootElement;
        if (rel.ValueKind == JsonValueKind.Array)
        {
            bool wantPre = string.Equals(desiredChannel, "prerelease", StringComparison.OrdinalIgnoreCase);
            JsonElement? chosen = null;
            foreach (JsonElement item in rel.EnumerateArray())
            {
                bool draft = item.TryGetProperty("draft", out var d) && d.GetBoolean();
                if (draft) continue;
                bool isPre = item.TryGetProperty("prerelease", out var p) && p.GetBoolean();
                if (wantPre || !isPre)
                {
                    chosen = item;
                    break;
                }
            }
            if (chosen is null) return null;
            rel = chosen.Value;
        }

        string tag = rel.TryGetProperty("tag_name", out var tagEl) ? (tagEl.GetString() ?? string.Empty).TrimStart('v', 'V') : string.Empty;
        if (string.IsNullOrWhiteSpace(tag)) return null;
        string? notes = rel.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null;
        var assets = new List<ParsedAsset>();
        if (rel.TryGetProperty("assets", out var arrEl) && arrEl.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement a in arrEl.EnumerateArray())
            {
                string name = a.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "" : "";
                string dl = a.TryGetProperty("browser_download_url", out var uEl) ? uEl.GetString() ?? "" : "";
                long? sz = a.TryGetProperty("size", out var sEl) && sEl.TryGetInt64(out long v) ? v : null;
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(dl))
                    assets.Add(new ParsedAsset(name, dl, sz, null));
            }
        }
        return (tag, notes, assets);
    }

    private static (string Version, string? Notes, List<ParsedAsset> Assets)? ParseReleaseHubJson(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        string ver = root.TryGetProperty("version", out var vEl) ? (vEl.GetString() ?? string.Empty).TrimStart('v', 'V') : string.Empty;
        if (string.IsNullOrWhiteSpace(ver)) return null;
        string? notes = root.TryGetProperty("releaseNotes", out var nEl) ? nEl.GetString() : null;
        var assets = new List<ParsedAsset>();
        if (root.TryGetProperty("assets", out var arrEl) && arrEl.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement a in arrEl.EnumerateArray())
            {
                string name = a.TryGetProperty("fileName", out var fEl) ? fEl.GetString() ?? "" : "";
                string dl = a.TryGetProperty("downloadUrl", out var uEl) ? uEl.GetString() ?? "" : "";
                long? sz = a.TryGetProperty("sizeBytes", out var sEl) && sEl.TryGetInt64(out long v) ? v : null;
                string? sha = a.TryGetProperty("sha256", out var hEl) ? hEl.GetString() : null;
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(dl))
                    assets.Add(new ParsedAsset(name, dl, sz, sha));
            }
        }
        return (ver, notes, assets);
    }
}
