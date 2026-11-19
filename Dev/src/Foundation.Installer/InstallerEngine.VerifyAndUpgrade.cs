// @module: Foundation.Installer.InstallerEngine.VerifyAndUpgrade
// @purpose: Embedded payload SHA-256 and ZIP central-directory verification.
// @depends-on: System, System.IO, System.IO.Compression, System.Security.Cryptography, System.Threading, System.Threading.Tasks
// @exports: InstallerEngine (partial: HasEmbeddedPayload, VerifyPayloadAsync)

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
        using Stream? stream = OpenPayloadStream(externalPayloadPath);
        return stream != null;
    }

    public async Task<(bool Valid, string Sha256, long SizeBytes, int EntryCount, string Message)> VerifyPayloadAsync(
        string? externalPayloadPath = null,
        CancellationToken ct = default)
    {
        await using Stream? payloadStream = OpenPayloadStream(externalPayloadPath);
        if (payloadStream == null)
        {
            return (false, string.Empty, 0, 0, $"No embedded {Descriptor.DisplayName} payload archive or --payload file was found.");
        }

        using var sha = SHA256.Create();
        byte[] buffer = new byte[65536];
        long total = 0;
        int read;
        while ((read = await payloadStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            sha.TransformBlock(buffer, 0, read, null, 0);
            total += read;
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        string hex = Convert.ToHexString(sha.Hash!).ToLowerInvariant();

        await using Stream? zipCheckStream = OpenPayloadStream(externalPayloadPath);
        if (zipCheckStream == null)
        {
            return (false, hex, total, 0, "Unable to reopen payload archive for central directory verification.");
        }

        int fileEntries = 0;
        using (var zip = new ZipArchive(zipCheckStream, ZipArchiveMode.Read, leaveOpen: false))
        {
            long expandedTotal = 0;
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;
                fileEntries++;
                expandedTotal += entry.Length;
                if (expandedTotal > MaxArchiveExpandedBytes)
                {
                    return (false, hex, total, fileEntries, "Payload archive exceeds maximum allowed uncompressed size boundary.");
                }
            }
        }

        if (fileEntries == 0)
        {
            return (false, hex, total, 0, "Payload archive contains zero installable files.");
        }

        string msg = $"Verified {Descriptor.DisplayName} v{InstallerVersion} payload ({fileEntries:N0} files, {total:N0} compressed bytes, SHA-256: {hex}).";
        _ctx.WriteLog("INFO", msg);
        return (true, hex, total, fileEntries, msg);
    }
}
