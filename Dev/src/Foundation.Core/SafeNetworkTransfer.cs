// @module: Foundation.Core.Networking
// @purpose: Implements Standard 19 §5 6-Pillar Safe Network Transfers (timeouts, jitter backoff, HTTP Range resume, disk preflight, throttle, hash check).
// @depends-on: System, System.IO, System.Net.Http, System.Security.Cryptography, System.Threading, System.Threading.Tasks
// @exports: SafeTransferOptions, SafeTransferResult, SafeNetworkTransfer

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Foundation.Core;

public sealed record SafeTransferOptions
{
    public string JobId { get; init; } = "default";
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ReadStallTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan? ResponseHeadersTimeout { get; init; }
    public int MaxRetries { get; init; } = 3;
    public long? ExpectedSizeBytes { get; init; }
    public string? ExpectedSha256Hex { get; init; }
    public string? ExpectedSha1Hex { get; init; }
    public long MaxBytesPerSecond { get; init; } = 0;
    public long MinFreeSpaceReserveBytes { get; init; } = 512L * 1024 * 1024;
    public Action<long, long?>? OnBytesProgress { get; init; }
}

public sealed record SafeTransferResult(
    string FinalFilePath,
    long BytesTransferred,
    string Sha256Hex,
    string Sha1Hex,
    int RetryCount,
    bool ResumedFromPartial
);

/// <summary>
/// Enforces all 6 pillars of Standard 19 §5 Network Transfer Safety:
/// 1. ConnectTimeout (10s) &amp; Per-Read Stall Timeout (30s)
/// 2. Exponential Backoff + Full Jitter (MaxRetries = 3, respecting Retry-After)
/// 3. HTTP Range Resume on .partial_&lt;jobId&gt; staging files
/// 4. Disk Free-Space Pre-Flight Check (1.2x expected size + 512 MB reserve)
/// 5. Token-Bucket Bandwidth Throttling
/// 6. Streaming SHA-256 &amp; SHA-1 Verification before atomic rename
/// </summary>
public static class SafeNetworkTransfer
{
    public static void AssertSufficientDiskSpace(
        string destinationFilePath,
        long expectedPayloadBytes,
        long reserveBytes = 512L * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);
        string? root = Path.GetPathRoot(Path.GetFullPath(destinationFilePath));
        if (string.IsNullOrWhiteSpace(root)) return;

        var drive = new DriveInfo(root);
        if (!drive.IsReady) return;

        long requiredBytes = (long)Math.Ceiling(Math.Max(0L, expectedPayloadBytes) * 1.2) + Math.Max(0L, reserveBytes);
        if (drive.AvailableFreeSpace < requiredBytes)
        {
            throw new IOException(
                $"Insufficient disk space on '{root}': {drive.AvailableFreeSpace:N0} bytes free, require {requiredBytes:N0} bytes.");
        }
    }

    public static async Task<SafeTransferResult> DownloadFileAsync(
        Uri sourceUri,
        string destinationFilePath,
        SafeTransferOptions? options = null,
        HttpMessageHandler? customHandler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);

        SafeTransferOptions opts = options ?? new SafeTransferOptions();
        string fullDest = Path.GetFullPath(destinationFilePath);
        string? parentDir = Path.GetDirectoryName(fullDest);
        if (!string.IsNullOrEmpty(parentDir)) Directory.CreateDirectory(parentDir);

        if (opts.ExpectedSizeBytes.HasValue)
        {
            AssertSufficientDiskSpace(fullDest, opts.ExpectedSizeBytes.Value, opts.MinFreeSpaceReserveBytes);
        }

        string safeJobId = SanitizeJobId(opts.JobId);
        string partialPath = $"{fullDest}.partial_{safeJobId}";
        bool resumed = false;
        bool retried416 = false;
        int attempt = 0;

        using HttpClient client = CreateHttpClient(opts.ConnectTimeout, customHandler);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                long existingOffset = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0L;
                using var request = new HttpRequestMessage(HttpMethod.Get, sourceUri);
                if (existingOffset > 0) request.Headers.Range = new RangeHeaderValue(existingOffset, null);

                TimeSpan headerTimeout = opts.ResponseHeadersTimeout ?? opts.ReadStallTimeout;
                using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (headerTimeout > TimeSpan.Zero && headerTimeout != Timeout.InfiniteTimeSpan)
                {
                    headerCts.CancelAfter(headerTimeout);
                }

                using HttpResponseMessage response = await client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, headerCts.Token).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && existingOffset > 0)
                {
                    try { if (File.Exists(partialPath)) File.Delete(partialPath); } catch { /* ignore */ }
                    resumed = false;
                    if (!retried416)
                    {
                        retried416 = true;
                        continue;
                    }
                }

                if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                {
                    throw new HttpRequestException($"Transient HTTP {(int)response.StatusCode}", null, response.StatusCode)
                    {
                        Data = { ["RetryAfter"] = response.Headers.RetryAfter?.Delta }
                    };
                }

                response.EnsureSuccessStatusCode();
                bool isPartial = response.StatusCode == HttpStatusCode.PartialContent && existingOffset > 0;
                if (isPartial) resumed = true;
                else existingOffset = 0;

                long? totalBytes = response.Content.Headers.ContentLength.HasValue
                    ? response.Content.Headers.ContentLength.Value + existingOffset
                    : opts.ExpectedSizeBytes;

                if (totalBytes.HasValue)
                {
                    AssertSufficientDiskSpace(fullDest, totalBytes.Value, opts.MinFreeSpaceReserveBytes);
                }

                await StreamToPartialFileAsync(
                    response, partialPath, isPartial, existingOffset, totalBytes, opts, cancellationToken).ConfigureAwait(false);
                break;
            }
            catch (Exception ex) when (attempt < opts.MaxRetries && IsTransient(ex, cancellationToken))
            {
                attempt++;
                TimeSpan delay = ComputeBackoffWithFullJitter(attempt, ex.Data["RetryAfter"] as TimeSpan?);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        (string sha256Hex, string sha1Hex, long finalLength) = await ComputeHashesAsync(partialPath, cancellationToken).ConfigureAwait(false);
        VerifyExpectedHash(partialPath, fullDest, "SHA-256", opts.ExpectedSha256Hex, sha256Hex);
        VerifyExpectedHash(partialPath, fullDest, "SHA-1", opts.ExpectedSha1Hex, sha1Hex);

        File.Move(partialPath, fullDest, overwrite: true);
        return new SafeTransferResult(fullDest, finalLength, sha256Hex, sha1Hex, attempt, resumed);
    }

    private static void VerifyExpectedHash(string partialPath, string fullDest, string alg, string? expected, string actual)
    {
        if (!string.IsNullOrWhiteSpace(expected) && !string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(partialPath); } catch { /* ignore */ }
            throw new InvalidDataException($"{alg} verification failed for '{fullDest}': expected {expected}, got {actual}.");
        }
    }

    private static async Task StreamToPartialFileAsync(
        HttpResponseMessage response,
        string partialPath,
        bool appendToExisting,
        long initialBytes,
        long? totalBytes,
        SafeTransferOptions opts,
        CancellationToken cancellationToken)
    {
        FileMode mode = appendToExisting ? FileMode.Append : FileMode.Create;
        await using Stream netStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var fileStream = new FileStream(partialPath, mode, FileAccess.Write, FileShare.None, 65536, useAsync: true);

        byte[] buffer = new byte[65536];
        long totalWritten = initialBytes;
        var windowClock = Stopwatch.StartNew();
        long windowBytes = 0;

        while (true)
        {
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readCts.CancelAfter(opts.ReadStallTimeout);

            int read = await netStream.ReadAsync(buffer, readCts.Token).ConfigureAwait(false);
            if (read <= 0) break;

            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            totalWritten += read;
            windowBytes += read;
            opts.OnBytesProgress?.Invoke(totalWritten, totalBytes);

            if (opts.MaxBytesPerSecond > 0)
            {
                double expectedMs = (double)windowBytes / opts.MaxBytesPerSecond * 1000.0;
                double actualMs = windowClock.Elapsed.TotalMilliseconds;
                if (expectedMs > actualMs && (expectedMs - actualMs) > 5)
                {
                    await Task.Delay((int)Math.Min(1000, expectedMs - actualMs), cancellationToken).ConfigureAwait(false);
                }
                if (windowClock.ElapsedMilliseconds >= 1000)
                {
                    windowClock.Restart();
                    windowBytes = 0;
                }
            }
        }
        await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(string Sha256Hex, string Sha1Hex, long Length)> ComputeHashesAsync(
        string filePath, CancellationToken cancellationToken)
    {
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        byte[] buffer = new byte[65536];
        long total = 0;
        while (true)
        {
            int read = await fs.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0) break;
            sha256.AppendData(buffer, 0, read);
            sha1.AppendData(buffer, 0, read);
            total += read;
        }
        return (
            Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant(),
            Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant(),
            total);
    }

    private static HttpClient CreateHttpClient(TimeSpan connectTimeout, HttpMessageHandler? customHandler)
    {
        if (customHandler is not null)
        {
            return new HttpClient(customHandler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        }
        var handler = new SocketsHttpHandler { ConnectTimeout = connectTimeout, AutomaticDecompression = DecompressionMethods.All };
        return new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private static string SanitizeJobId(string? jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return "default";
        char[] invalid = Path.GetInvalidFileNameChars();
        char[] chars = jobId.Trim().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (c == ':' || c == '/' || c == '\\' || Array.IndexOf(invalid, c) >= 0)
            {
                chars[i] = '_';
            }
        }
        string sanitized = new string(chars).Trim('.');
        return string.IsNullOrWhiteSpace(sanitized) ? "default" : sanitized;
    }

    private static bool IsTransient(Exception ex, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return false;
        if (ex is HttpRequestException httpEx && httpEx.StatusCode is HttpStatusCode status)
        {
            int code = (int)status;
            if (code is 400 or 401 or 403 or 404 or 410)
            {
                return false;
            }
        }
        return ex is HttpRequestException or IOException or OperationCanceledException;
    }

    private static TimeSpan ComputeBackoffWithFullJitter(int attempt, TimeSpan? retryAfter)
    {
        if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero)
        {
            return retryAfter.Value <= TimeSpan.FromSeconds(15) ? retryAfter.Value : TimeSpan.FromSeconds(15);
        }
        double capMs = Math.Min(4000.0, 250.0 * Math.Pow(2, attempt));
        return TimeSpan.FromMilliseconds(Math.Max(50.0, Random.Shared.NextDouble() * capMs));
    }
}
