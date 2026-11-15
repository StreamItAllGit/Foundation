// @module: Foundation.Sidecars.SinglePassStreamHasher
// @purpose: Zero-allocation ArrayPool<byte> single-pass CRC32/MD5/SHA1/SHA256 stream hasher with simultaneous headerless ROM hashing.
// @depends-on: System, System.Buffers, System.Collections.Generic, System.IO, System.Security.Cryptography, System.Threading, System.Threading.Tasks, Foundation.Core, Foundation.Retro
// @exports: ComputedStreamHashes, Crc32Accumulator, SinglePassStreamHasher

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;
using Foundation.Retro;

namespace Foundation.Sidecars;

public sealed class ComputedStreamHashes
{
    public long SizeBytes { get; set; }
    public string Crc32 { get; set; } = "";
    public string Md5 { get; set; } = "";
    public string Sha1 { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public bool HeaderRuleMatched { get; set; }
    public string? MatchedHeaderRuleId { get; set; }
    public int HeaderBytesSkipped { get; set; }
    public string HeaderlessCrc32 { get; set; } = "";
    public string HeaderlessMd5 { get; set; } = "";
    public string HeaderlessSha1 { get; set; } = "";
}

public struct Crc32Accumulator
{
    private static readonly uint[] Table = BuildTable();
    private uint _crc;

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
            table[i] = c;
        }
        return table;
    }

    public static Crc32Accumulator Create() => new() { _crc = 0xFFFFFFFFu };

    public void Append(ReadOnlySpan<byte> data)
    {
        uint c = _crc;
        var t = Table;
        for (int i = 0; i < data.Length; i++)
            c = t[(c ^ data[i]) & 0xFF] ^ (c >> 8);
        _crc = c;
    }

    public readonly uint FinalUInt32 => _crc ^ 0xFFFFFFFFu;
    public readonly string FinalHex => FinalUInt32.ToString("X8");

    public static uint ComputeUInt32(ReadOnlySpan<byte> data)
    {
        var acc = Create();
        acc.Append(data);
        return acc.FinalUInt32;
    }
}

public static class SinglePassStreamHasher
{
    public static async Task<ComputedStreamHashes> HashFileSinglePassAsync(
        string filePath,
        IReadOnlyList<RetroHeaderSkipRule>? headerRules = null,
        bool includeSha256 = false,
        CancellationToken ct = default,
        Action<long, long>? onBytesRead = null)
    {
        string safePath = AppEnvironment.NormalizeLongPath(filePath);
        await using var fs = new FileStream(safePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 131072, useAsync: true);
        return await HashStreamSinglePassAsync(fs, fs.Length, Path.GetExtension(filePath), headerRules, includeSha256, ct, onBytesRead);
    }

    public static async Task<ComputedStreamHashes> HashStreamSinglePassAsync(
        Stream stream,
        long knownLength,
        string fileExtension,
        IReadOnlyList<RetroHeaderSkipRule>? headerRules = null,
        bool includeSha256 = false,
        CancellationToken ct = default,
        Action<long, long>? onBytesRead = null)
    {
        var crc = Crc32Accumulator.Create();
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        using var sha256 = includeSha256 ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;

        var headerlessCrc = Crc32Accumulator.Create();
        using var headerlessMd5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        using var headerlessSha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(131072);
        long totalRead = 0;
        bool inspectedLeading = false;
        int skipBytes = 0;
        string? ruleId = null;

        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                if (!inspectedLeading)
                {
                    inspectedLeading = true;
                    long effectiveSize = knownLength > 0 ? knownLength : read;
                    skipBytes = HeaderlessRomStripper.DetectHeaderSkipLength(buffer.AsSpan(0, read), effectiveSize, fileExtension, headerRules, out ruleId);
                }

                AppendChunkHashes(buffer, read, totalRead, skipBytes, ref crc, md5, sha1, sha256, ref headerlessCrc, headerlessMd5, headerlessSha1);
                totalRead += read;
                onBytesRead?.Invoke(totalRead, knownLength);
            }

            var result = new ComputedStreamHashes
            {
                SizeBytes = totalRead,
                Crc32 = crc.FinalHex,
                Md5 = Convert.ToHexString(md5.GetHashAndReset()),
                Sha1 = Convert.ToHexString(sha1.GetHashAndReset()),
                Sha256 = sha256 != null ? Convert.ToHexString(sha256.GetHashAndReset()) : ""
            };

            if (skipBytes > 0 && totalRead > skipBytes)
            {
                result.HeaderRuleMatched = true;
                result.MatchedHeaderRuleId = ruleId;
                result.HeaderBytesSkipped = skipBytes;
                result.HeaderlessCrc32 = headerlessCrc.FinalHex;
                result.HeaderlessMd5 = Convert.ToHexString(headerlessMd5.GetHashAndReset());
                result.HeaderlessSha1 = Convert.ToHexString(headerlessSha1.GetHashAndReset());
            }

            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void AppendChunkHashes(
        byte[] buffer,
        int read,
        long totalReadBefore,
        int skipBytes,
        ref Crc32Accumulator crc,
        IncrementalHash md5,
        IncrementalHash sha1,
        IncrementalHash? sha256,
        ref Crc32Accumulator headerlessCrc,
        IncrementalHash headerlessMd5,
        IncrementalHash headerlessSha1)
    {
        ReadOnlySpan<byte> span = buffer.AsSpan(0, read);
        crc.Append(span);
        md5.AppendData(span);
        sha1.AppendData(span);
        sha256?.AppendData(span);

        if (skipBytes > 0)
        {
            long chunkEnd = totalReadBefore + read;
            if (chunkEnd > skipBytes)
            {
                int sliceOffset = totalReadBefore < skipBytes ? (int)(skipBytes - totalReadBefore) : 0;
                ReadOnlySpan<byte> strippedSlice = span[sliceOffset..];
                headerlessCrc.Append(strippedSlice);
                headerlessMd5.AppendData(strippedSlice);
                headerlessSha1.AppendData(strippedSlice);
            }
        }
    }
}
