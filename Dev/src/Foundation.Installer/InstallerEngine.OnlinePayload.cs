// @module: Foundation.Installer.OnlinePayload
// @purpose: Resolves, downloads (HTTPS + SHA-256 + byte-size verified via SafeNetworkTransfer), validates --offline-bundle payloads, and extracts profile assets safely.
// @depends-on: System, System.Collections.Generic, System.IO, System.IO.Compression, System.Linq, System.Net.Http, System.Security.Cryptography, System.Threading, System.Threading.Tasks, Foundation.Core
// @exports: InstallerEngine (partial: online & offline payload resolution, ZIP verification, and bounded extraction)

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;

namespace Foundation.Installer;

public sealed partial class InstallerEngine
{
    public HttpMessageHandler? CustomHttpHandler { get; set; }
    public bool AllowLoopbackHttpForTesting { get; set; }

    internal sealed record ResolvedPayloadItem(
        string DisplayLabel,
        string? LocalZipPath,
        string? RelativeSubdir,
        string Sha256Hex,
        long SizeBytes,
        int EntryCount);

    internal sealed class ResolvedPayloadBatch : IAsyncDisposable
    {
        public bool Valid { get; init; }
        public string Message { get; init; } = string.Empty;
        public string CombinedSha256 { get; init; } = string.Empty;
        public long TotalBytes { get; init; }
        public int TotalEntries { get; init; }
        public List<ResolvedPayloadItem> Items { get; init; } = new();
        public string? StagingTempDir { get; init; }

        public ValueTask DisposeAsync()
        {
            if (!string.IsNullOrWhiteSpace(StagingTempDir) && Directory.Exists(StagingTempDir))
            {
                try { Directory.Delete(StagingTempDir, recursive: true); } catch { /* best-effort temp cleanup */ }
            }
            return ValueTask.CompletedTask;
        }
    }

    public IReadOnlyList<InstallerPayloadAsset> SelectPayloadAssetsForProfile(string? installProfile)
    {
        if (Descriptor.PayloadAssets is null || Descriptor.PayloadAssets.Count == 0)
            return Array.Empty<InstallerPayloadAsset>();

        string profile = string.IsNullOrWhiteSpace(installProfile)
            ? Descriptor.DefaultProfile.Trim().ToLowerInvariant()
            : installProfile.Trim().ToLowerInvariant();

        return Descriptor.PayloadAssets
            .Where(a =>
            {
                string ap = (a.InstallProfile ?? "lightweight").Trim().ToLowerInvariant();
                if (ap is "*" or "all" or "common") return true;
                if (profile == "full") return ap is "full" or "lightweight";
                return string.Equals(ap, profile, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
    }

    internal async Task<ResolvedPayloadBatch> ResolvePayloadBatchAsync(
        string? externalPayloadPath,
        string? offlineBundlePath,
        string? installProfile,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(externalPayloadPath) || !Descriptor.IsOnlineSetup)
        {
            var local = await VerifyLocalPayloadStreamAsync(externalPayloadPath, ct).ConfigureAwait(false);
            if (!local.Valid)
                return new ResolvedPayloadBatch { Valid = false, Message = local.Message };

            return new ResolvedPayloadBatch
            {
                Valid = true,
                Message = local.Message,
                CombinedSha256 = local.Sha256,
                TotalBytes = local.SizeBytes,
                TotalEntries = local.EntryCount,
                Items = new List<ResolvedPayloadItem>
                {
                    new(externalPayloadPath ?? Descriptor.EffectiveEmbeddedPayloadResource,
                        !string.IsNullOrWhiteSpace(externalPayloadPath) ? Path.GetFullPath(externalPayloadPath) : null,
                        null, local.Sha256, local.SizeBytes, local.EntryCount)
                }
            };
        }

        if (!string.IsNullOrWhiteSpace(offlineBundlePath))
        {
            return await ResolveFromOfflineBundleAsync(offlineBundlePath, installProfile, ct).ConfigureAwait(false);
        }

        return await ResolveFromRemoteOnlineSetupAsync(installProfile, progress, ct).ConfigureAwait(false);
    }

    private async Task<ResolvedPayloadBatch> ResolveFromOfflineBundleAsync(
        string offlineBundlePath,
        string? installProfile,
        CancellationToken ct)
    {
        string fullBundle = Path.GetFullPath(offlineBundlePath.Trim());
        IReadOnlyList<InstallerPayloadAsset> selected = SelectPayloadAssetsForProfile(installProfile);
        var items = new List<ResolvedPayloadItem>();

        if (File.Exists(fullBundle))
        {
            string bundleName = Path.GetFileName(fullBundle);
            InstallerPayloadAsset? matched = selected.FirstOrDefault(a =>
                string.Equals(Path.GetFileName(a.FileName), bundleName, StringComparison.OrdinalIgnoreCase))
                ?? selected.FirstOrDefault();

            var check = await VerifySingleZipFileAsync(
                fullBundle, matched?.ExpectedSizeBytes, matched?.ExpectedSha256, ct).ConfigureAwait(false);
            if (!check.Valid)
            {
                return new ResolvedPayloadBatch { Valid = false, Message = BuildOfflineSetupFailureMessage(check.Message) };
            }

            items.Add(new ResolvedPayloadItem(bundleName, fullBundle, matched?.RelativeSubdir, check.Sha256, check.SizeBytes, check.EntryCount));
            return BuildBatchSummary(items);
        }

        if (Directory.Exists(fullBundle))
        {
            if (selected.Count == 0)
            {
                return new ResolvedPayloadBatch
                {
                    Valid = false,
                    Message = BuildOfflineSetupFailureMessage($"No payloadAssets[] entries match profile '{installProfile ?? Descriptor.DefaultProfile}'.")
                };
            }

            foreach (InstallerPayloadAsset asset in selected)
            {
                string safeName = Path.GetFileName(asset.FileName);
                string assetPath = EnsureWithinDirectory(fullBundle, Path.Combine(fullBundle, safeName));
                if (!File.Exists(assetPath))
                {
                    return new ResolvedPayloadBatch
                    {
                        Valid = false,
                        Message = BuildOfflineSetupFailureMessage($"Missing '{safeName}' inside offline bundle directory '{fullBundle}'.")
                    };
                }

                var check = await VerifySingleZipFileAsync(
                    assetPath, asset.ExpectedSizeBytes > 0 ? asset.ExpectedSizeBytes : null, asset.ExpectedSha256, ct).ConfigureAwait(false);
                if (!check.Valid)
                {
                    return new ResolvedPayloadBatch { Valid = false, Message = BuildOfflineSetupFailureMessage(check.Message) };
                }

                items.Add(new ResolvedPayloadItem(safeName, assetPath, asset.RelativeSubdir, check.Sha256, check.SizeBytes, check.EntryCount));
            }
            return BuildBatchSummary(items);
        }

        return new ResolvedPayloadBatch
        {
            Valid = false,
            Message = BuildOfflineSetupFailureMessage($"Offline bundle path not found: '{fullBundle}'.")
        };
    }

    private async Task<ResolvedPayloadBatch> ResolveFromRemoteOnlineSetupAsync(
        string? installProfile,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var candidateBaseUrls = new List<string>();
        if (!string.IsNullOrWhiteSpace(Descriptor.PayloadDownloadUrl))
            candidateBaseUrls.Add(Descriptor.PayloadDownloadUrl.Trim());
        if (Descriptor.PayloadFallbackUrls is { Count: > 0 })
        {
            foreach (string fb in Descriptor.PayloadFallbackUrls)
            {
                if (!string.IsNullOrWhiteSpace(fb) &&
                    !candidateBaseUrls.Contains(fb.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    candidateBaseUrls.Add(fb.Trim());
                }
            }
        }

        if (candidateBaseUrls.Count == 0)
        {
            return new ResolvedPayloadBatch
            {
                Valid = false,
                Message = BuildOfflineSetupFailureMessage("Online Setup manifest is missing a valid absolute payloadDownloadUrl.")
            };
        }

        IReadOnlyList<InstallerPayloadAsset> selected = SelectPayloadAssetsForProfile(installProfile);
        if (selected.Count == 0)
        {
            return new ResolvedPayloadBatch
            {
                Valid = false,
                Message = BuildOfflineSetupFailureMessage($"Online Setup manifest defines no payloadAssets[] for profile '{installProfile ?? Descriptor.DefaultProfile}'.")
            };
        }

        string? effectivePin = ReleaseUpdaterClient.ResolveBetaPin(Descriptor.BetaPin, Descriptor.ProductName);
        string tempRoot = _ctx.IsSandboxActive
            ? Path.Combine(KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, _ctx.EffectiveSandbox), "Temp")
            : Path.GetTempPath();
        string stagingDir = Path.Combine(tempRoot, $"FoundationOnline_{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(stagingDir);

        var mirrorErrors = new List<string>();
        for (int idx = 0; idx < candidateBaseUrls.Count; idx++)
        {
            string rawBase = candidateBaseUrls[idx];
            string resolvedBase = string.IsNullOrWhiteSpace(effectivePin)
                ? rawBase
                : rawBase.Replace("{pin}", Uri.EscapeDataString(effectivePin), StringComparison.OrdinalIgnoreCase);

            if (!Uri.TryCreate(resolvedBase, UriKind.Absolute, out Uri? baseUri))
            {
                mirrorErrors.Add($"Invalid URL '{rawBase}'");
                continue;
            }

            bool isGitHub = baseUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                            baseUri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
            var headers = (!isGitHub && !string.IsNullOrWhiteSpace(effectivePin))
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Beta-Pin"] = effectivePin }
                : null;

            var items = new List<ResolvedPayloadItem>();
            string attemptDir = Path.Combine(stagingDir, $"mirror_{idx}");
            Directory.CreateDirectory(attemptDir);
            try
            {
                foreach (InstallerPayloadAsset asset in selected)
                {
                    if (string.IsNullOrWhiteSpace(asset.ExpectedSha256))
                    {
                        throw new InvalidDataException($"Online Setup asset '{asset.FileName}' is missing required pinned expectedSha256.");
                    }

                    string safeFileName = Path.GetFileName(asset.FileName);
                    Uri assetUri = BuildAssetDownloadUri(baseUri, safeFileName);
                    string stagedPath = EnsureWithinDirectory(attemptDir, Path.Combine(attemptDir, safeFileName));

                    progress?.Report($"Downloading verified payload '{safeFileName}' from {baseUri.Host}...");
                    SafeTransferResult transfer = await SafeNetworkTransfer.DownloadFileAsync(
                        assetUri,
                        stagedPath,
                        new SafeTransferOptions
                        {
                            JobId = $"setup_{idx}_{Guid.NewGuid():N}",
                            ExpectedSha256Hex = asset.ExpectedSha256,
                            ExpectedSizeBytes = asset.ExpectedSizeBytes > 0 ? asset.ExpectedSizeBytes : null,
                            RequireHttps = true,
                            AllowLoopbackHttpForTesting = AllowLoopbackHttpForTesting,
                            RequestHeaders = headers,
                            OnBytesProgress = (done, total) =>
                            {
                                if (total is > 0)
                                {
                                    double pct = Math.Clamp(done * 100.0 / total.Value, 0.0, 100.0);
                                    progress?.Report($"Downloading {safeFileName}: {pct:F0}% ({done / (1024.0 * 1024.0):F1} MB)");
                                }
                            }
                        },
                        CustomHttpHandler,
                        ct).ConfigureAwait(false);

                    var check = await VerifySingleZipFileAsync(stagedPath, null, asset.ExpectedSha256, ct).ConfigureAwait(false);
                    if (!check.Valid)
                    {
                        throw new InvalidDataException(check.Message);
                    }

                    items.Add(new ResolvedPayloadItem(safeFileName, transfer.FinalFilePath, asset.RelativeSubdir, check.Sha256, check.SizeBytes, check.EntryCount));
                }

                return BuildBatchSummary(items, stagingDir);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                try { if (Directory.Exists(attemptDir)) Directory.Delete(attemptDir, recursive: true); } catch { }
                string warn = $"Mirror '{baseUri.Host}' failed ({ex.Message}); trying next fallback if available.";
                _ctx.WriteLog("WARN", warn);
                mirrorErrors.Add($"{baseUri.Host}: {ex.Message}");
            }
        }

        try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true); } catch { }
        string finalMsg = BuildOfflineSetupFailureMessage(string.Join(" | ", mirrorErrors));
        _ctx.WriteLog("ERROR", finalMsg);
        return new ResolvedPayloadBatch { Valid = false, Message = finalMsg };
    }

    private static Uri BuildAssetDownloadUri(Uri baseUri, string fileName)
    {
        if (string.Equals(Path.GetFileName(baseUri.AbsolutePath.TrimEnd('/')), fileName, StringComparison.OrdinalIgnoreCase))
            return baseUri;

        string rootText = baseUri.AbsoluteUri.EndsWith('/') ? baseUri.AbsoluteUri : baseUri.AbsoluteUri + "/";
        return new Uri(new Uri(rootText, UriKind.Absolute), Uri.EscapeDataString(fileName));
    }

    private string BuildOfflineSetupFailureMessage(string reason) =>
        $"Online Setup failed to retrieve a verified payload for {Descriptor.DisplayName} v{InstallerVersion}. " +
        $"Reason: {reason} " +
        $"Remedy: Check connectivity or download '{Descriptor.ProductName}-Offline-Setup-v{InstallerVersion}.exe' or '{Descriptor.ProductName}-v{InstallerVersion}-Portable.zip', or re-run with '--offline-bundle <zipOrDir>'.";

    private ResolvedPayloadBatch BuildBatchSummary(List<ResolvedPayloadItem> items, string? stagingDir = null)
    {
        long totalBytes = items.Sum(i => i.SizeBytes);
        int totalEntries = items.Sum(i => i.EntryCount);
        string combinedSha = items.Count == 1
            ? items[0].Sha256Hex
            : Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join('|', items.Select(i => i.Sha256Hex))))).ToLowerInvariant();
        string msg = $"Verified {Descriptor.DisplayName} v{InstallerVersion} payload ({totalEntries:N0} files, {totalBytes:N0} compressed bytes, SHA-256: {combinedSha}).";
        _ctx.WriteLog("INFO", msg);
        return new ResolvedPayloadBatch
        {
            Valid = true,
            Message = msg,
            CombinedSha256 = combinedSha,
            TotalBytes = totalBytes,
            TotalEntries = totalEntries,
            Items = items,
            StagingTempDir = stagingDir
        };
    }
}
