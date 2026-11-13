// @module: Foundation.Core.Imaging
// @purpose: Pure-.NET lossless RGBA 8-bit PNG encoder using ZLibStream and IEEE 802.3 CRC32 without GDI+ dependencies.
// @depends-on: System, System.Buffers.Binary, System.IO, System.IO.Compression, System.Text
// @exports: RgbaPngWriter

using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Foundation.Core;

/// <summary>
/// Pure-.NET lossless 8-bit RGBA PNG encoder with zero GDI+ or System.Drawing dependencies.
/// </summary>
public static class RgbaPngWriter
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] Crc32Table = BuildCrc32Table();

    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        int expectedLen = checked(width * height * 4);
        if (rgba.Length != expectedLen)
        {
            throw new ArgumentException(
                $"Expected {expectedLen} RGBA bytes for {width}x{height}, received {rgba.Length}.",
                nameof(rgba));
        }

        using var output = new MemoryStream();
        output.Write(PngSignature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[0..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..8], height);
        ihdr[8] = 8;  // Bit depth
        ihdr[9] = 6;  // Color type: RGBA
        ihdr[10] = 0; // Compression: deflate
        ihdr[11] = 0; // Filter: adaptive
        ihdr[12] = 0; // Interlace: none
        WriteChunk(output, "IHDR", ihdr);

        byte[] compressedIdat;
        using (var idatStream = new MemoryStream())
        {
            using (var zlib = new ZLibStream(idatStream, CompressionLevel.Optimal, leaveOpen: true))
            {
                int rowBytes = width * 4;
                byte[] scanline = new byte[1 + rowBytes];
                scanline[0] = 0; // Filter type 0 (None)
                for (int y = 0; y < height; y++)
                {
                    rgba.Slice(y * rowBytes, rowBytes).CopyTo(scanline.AsSpan(1));
                    zlib.Write(scanline, 0, scanline.Length);
                }
            }
            compressedIdat = idatStream.ToArray();
        }

        WriteChunk(output, "IDAT", compressedIdat);
        WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    public static void WritePng(string filePath, int width, int height, ReadOnlySpan<byte> rgba)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string full = Path.GetFullPath(filePath);
        string? parent = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        byte[] encoded = Encode(width, height, rgba);
        File.WriteAllBytes(full, encoded);
    }

    private static void WriteChunk(Stream output, string chunkType, ReadOnlySpan<byte> data)
    {
        Span<byte> lenBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lenBytes, (uint)data.Length);
        output.Write(lenBytes);

        Span<byte> typeBytes = stackalloc byte[4];
        Encoding.ASCII.GetBytes(chunkType, typeBytes);
        output.Write(typeBytes);

        if (!data.IsEmpty)
        {
            output.Write(data);
        }

        uint crc = 0xFFFFFFFFu;
        crc = UpdateCrc(crc, typeBytes);
        crc = UpdateCrc(crc, data);
        crc ^= 0xFFFFFFFFu;

        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        uint c = crc;
        uint[] table = Crc32Table;
        for (int i = 0; i < data.Length; i++)
        {
            c = table[(c ^ data[i]) & 0xFF] ^ (c >> 8);
        }
        return c;
    }

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
            }
            table[i] = c;
        }
        return table;
    }
}
