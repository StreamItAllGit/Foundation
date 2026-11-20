// @module: Foundation.Installer.InstallerEngine.VerifyAndUpgrade
// @purpose: Embedded, external, --offline-bundle, and online payload SHA-256, byte-size, and ZIP central-directory verification.
// @depends-on: System, System.IO, System.IO.Compression, System.Security.Cryptography, System.Threading, System.Threading.Tasks
// @exports: InstallerEngine (partial: HasEmbeddedPayload, HasPayloadSource, VerifyPayloadAsync)

using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Foundation.Installer;

public sealed partial class InstallerEngine
{
    public bool HasEmbeddedPayload() =>
        _payloadAssembly.GetManifestResourceInfo(Descriptor.EffectiveEmbeddedPayloadResource) != null;

    public bool HasPayloadSource(string? externalPayloadPath = null)
    {
        if (Descriptor.IsOnlineSetup &&
            (!string.IsNullOrWhiteSpace(Descriptor.PayloadDownloadUrl) || !string.IsNullOrWhiteSpace(externalPayloadPath)))
        {
            return true;
        }
        using Stream? stream = OpenPayloadStream(externalPayloadPath);
        return stream != null;
    }

    public Task<(bool Valid, string Sha256, long SizeBytes, int EntryCount, string Message)> VerifyPayloadAsync(
        string? externalPayloadPath = null,
        CancellationToken ct = default) =>
        VerifyPayloadAsync(externalPayloadPath, offlineBundlePath: null, installProfile: null, ct);

    public async Task<(bool Valid, string Sha256, long SizeBytes, int EntryCount, string Message)> VerifyPayloadAsync(
        string? externalPayloadPath,
        string? offlineBundlePath,
        string? installProfile = null,
        CancellationToken ct = default)
    {
        await using ResolvedPayloadBatch batch = await ResolvePayloadBatchAsync(
            externalPayloadPath, offlineBundlePath, installProfile, progress: null, ct).ConfigureAwait(false);
        return (batch.Valid, batch.CombinedSha256, batch.TotalBytes, batch.TotalEntries, batch.Message);
    }

    internal async Task<(bool Valid, string Sha256, long SizeBytes, int EntryCount, string Message)> VerifyLocalPayloadStreamAsync(
        string? externalPayloadPath,
        CancellationToken ct)
    {
        await using Stream? payloadStream = OpenPayloadStream(externalPayloadPath);
        if (payloadStream == null)
            return (false, string.Empty, 0, 0, $"No embedded {Descriptor.DisplayName} payload archive or --payload file was found.");

        (string hex, long total) = await ComputeSha256AndLengthAsync(payloadStream, ct).ConfigureAwait(false);
        await using Stream? zipCheckStream = OpenPayloadStream(externalPayloadPath);
        if (zipCheckStream == null)
            return (false, hex, total, 0, "Unable to reopen payload archive for central directory verification.");

        var (zipValid, fileEntries, zipErr) = CountAndVerifyZipEntries(zipCheckStream);
        if (!zipValid)
            return (false, hex, total, fileEntries, zipErr);

        string msg = $"Verified {Descriptor.DisplayName} v{InstallerVersion} payload ({fileEntries:N0} files, {total:N0} compressed bytes, SHA-256: {hex}).";
        _ctx.WriteLog("INFO", msg);
        return (true, hex, total, fileEntries, msg);
    }

    internal static async Task<(bool Valid, string Sha256, long SizeBytes, int EntryCount, string Message)> VerifySingleZipFileAsync(
        string zipFilePath,
        long? expectedSizeBytes,
        string? expectedSha256Hex,
        CancellationToken ct)
    {
        string hex;
        long total;
        await using (var fs = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true))
        {
            (hex, total) = await ComputeSha256AndLengthAsync(fs, ct).ConfigureAwait(false);
        }

        if (expectedSizeBytes is > 0 && total != expectedSizeBytes.Value)
            return (false, hex, total, 0, $"Byte size mismatch for '{Path.GetFileName(zipFilePath)}': expected {expectedSizeBytes.Value}, got {total}.");

        if (!string.IsNullOrWhiteSpace(expectedSha256Hex) &&
            !string.Equals(hex, expectedSha256Hex.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return (false, hex, total, 0, $"SHA-256 mismatch for '{Path.GetFileName(zipFilePath)}': expected {expectedSha256Hex}, got {hex}.");
        }

        await using var zipFs = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        var (zipValid, fileEntries, zipErr) = CountAndVerifyZipEntries(zipFs);
        return zipValid
            ? (true, hex, total, fileEntries, $"Verified '{Path.GetFileName(zipFilePath)}' ({fileEntries:N0} files).")
            : (false, hex, total, fileEntries, zipErr);
    }

    private static async Task<(string Sha256, long Length)> ComputeSha256AndLengthAsync(Stream stream, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        byte[] buffer = new byte[65536];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            sha.TransformBlock(buffer, 0, read, null, 0);
            total += read;
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return (Convert.ToHexString(sha.Hash!).ToLowerInvariant(), total);
    }

    private static (bool Valid, int FileEntries, string ErrorMessage) CountAndVerifyZipEntries(Stream zipStream)
    {
        int fileEntries = 0;
        try
        {
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
            long expandedTotal = 0;
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                fileEntries++;
                expandedTotal += entry.Length;
                if (expandedTotal > MaxArchiveExpandedBytes)
                    return (false, fileEntries, "Payload archive exceeds maximum allowed uncompressed size boundary.");
            }
        }
        catch (InvalidDataException ex)
        {
            return (false, fileEntries, $"Invalid ZIP payload archive: {ex.Message}");
        }

        return fileEntries == 0
            ? (false, 0, "Payload archive contains zero installable files.")
            : (true, fileEntries, string.Empty);
    }
}
