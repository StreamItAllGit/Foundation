// @module: Foundation.Retro.SoftPatchEngine
// @purpose: Native in-memory IPS, BPS (BPS1), and UPS (UPS1) binary ROM soft-patchers with CRC32 verification.
// @depends-on: System, System.Collections.Generic, System.IO
// @exports: SoftPatchEngine

using System;
using System.Collections.Generic;
using System.IO;

namespace Foundation.Retro;

public static class SoftPatchEngine
{
    private static readonly uint[] Crc32Table = BuildCrc32Table();

    private static uint[] BuildCrc32Table()
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

    public static uint ComputeCrc32UInt32(ReadOnlySpan<byte> data)
    {
        uint c = 0xFFFFFFFFu;
        var t = Crc32Table;
        for (int i = 0; i < data.Length; i++)
            c = t[(c ^ data[i]) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    public static byte[] ApplyIpsPatchInMemory(byte[] baseRom, byte[] ipsPatch)
    {
        if (ipsPatch.Length < 8 || ipsPatch[0] != (byte)'P' || ipsPatch[1] != (byte)'A' ||
            ipsPatch[2] != (byte)'T' || ipsPatch[3] != (byte)'C' || ipsPatch[4] != (byte)'H')
            throw new InvalidDataException("Invalid IPS header: expected 'PATCH' magic.");

        var output = new List<byte>(baseRom.Length + 65536);
        output.AddRange(baseRom);
        int pos = 5;

        while (pos + 3 <= ipsPatch.Length)
        {
            if (ipsPatch[pos] == (byte)'E' && ipsPatch[pos + 1] == (byte)'O' && ipsPatch[pos + 2] == (byte)'F')
            {
                pos += 3;
                if (pos + 3 == ipsPatch.Length)
                {
                    int truncateLen = (ipsPatch[pos] << 16) | (ipsPatch[pos + 1] << 8) | ipsPatch[pos + 2];
                    if (truncateLen >= 0 && truncateLen < output.Count)
                        output.RemoveRange(truncateLen, output.Count - truncateLen);
                }
                return output.ToArray();
            }

            if (pos + 5 > ipsPatch.Length) throw new InvalidDataException("Unexpected end of IPS patch stream.");
            int offset = (ipsPatch[pos] << 16) | (ipsPatch[pos + 1] << 8) | ipsPatch[pos + 2];
            int size = (ipsPatch[pos + 3] << 8) | ipsPatch[pos + 4];
            pos += 5;

            if (size == 0)
            {
                if (pos + 3 > ipsPatch.Length) throw new InvalidDataException("Truncated IPS RLE hunk.");
                int rleCount = (ipsPatch[pos] << 8) | ipsPatch[pos + 1];
                byte rleByte = ipsPatch[pos + 2];
                pos += 3;
                EnsureOutputCapacity(output, offset + rleCount);
                for (int i = 0; i < rleCount; i++) output[offset + i] = rleByte;
            }
            else
            {
                if (pos + size > ipsPatch.Length) throw new InvalidDataException("Truncated IPS data hunk.");
                EnsureOutputCapacity(output, offset + size);
                for (int i = 0; i < size; i++) output[offset + i] = ipsPatch[pos + i];
                pos += size;
            }
        }
        throw new InvalidDataException("IPS patch missing 'EOF' marker.");
    }

    public static byte[] ApplyBpsPatchInMemory(byte[] sourceRom, byte[] bpsPatch)
    {
        if (bpsPatch.Length < 19 || bpsPatch[0] != (byte)'B' || bpsPatch[1] != (byte)'P' ||
            bpsPatch[2] != (byte)'S' || bpsPatch[3] != (byte)'1')
            throw new InvalidDataException("Invalid BPS header: expected 'BPS1' magic.");

        int footerPos = bpsPatch.Length - 12;
        uint expectedSourceCrc = BitConverter.ToUInt32(bpsPatch, footerPos);
        uint expectedTargetCrc = BitConverter.ToUInt32(bpsPatch, footerPos + 4);
        uint expectedPatchCrc = BitConverter.ToUInt32(bpsPatch, footerPos + 8);

        uint actualPatchCrc = ComputeCrc32UInt32(bpsPatch.AsSpan(0, bpsPatch.Length - 4));
        if (actualPatchCrc != expectedPatchCrc)
            throw new InvalidDataException($"BPS patch CRC32 mismatch (expected {expectedPatchCrc:X8}, got {actualPatchCrc:X8}).");

        uint actualSourceCrc = ComputeCrc32UInt32(sourceRom);
        if (actualSourceCrc != expectedSourceCrc)
            throw new InvalidDataException($"BPS source ROM CRC32 mismatch (expected {expectedSourceCrc:X8}, got {actualSourceCrc:X8}).");

        int pos = 4;
        ulong sourceSize = DecodeBpsNumber(bpsPatch, ref pos);
        if (sourceSize != (ulong)sourceRom.Length)
            throw new InvalidDataException($"BPS source size mismatch (expected {sourceSize}, got {sourceRom.Length}).");

        ulong targetSize = DecodeBpsNumber(bpsPatch, ref pos);
        ulong metadataSize = DecodeBpsNumber(bpsPatch, ref pos);
        pos += (int)metadataSize;

        byte[] target = new byte[targetSize];
        int outputOffset = 0;
        long sourceRel = 0;
        long targetRel = 0;

        while (pos < footerPos)
        {
            ulong data = DecodeBpsNumber(bpsPatch, ref pos);
            ulong command = data & 3UL;
            int length = (int)((data >> 2) + 1UL);

            switch (command)
            {
                case 0:
                    for (int i = 0; i < length; i++) target[outputOffset] = sourceRom[outputOffset++];
                    break;
                case 1:
                    for (int i = 0; i < length; i++) target[outputOffset++] = bpsPatch[pos++];
                    break;
                case 2:
                {
                    ulong offsetData = DecodeBpsNumber(bpsPatch, ref pos);
                    sourceRel += ((offsetData & 1UL) != 0 ? -1L : 1L) * (long)(offsetData >> 1);
                    for (int i = 0; i < length; i++) target[outputOffset++] = sourceRom[sourceRel++];
                    break;
                }
                case 3:
                {
                    ulong offsetData = DecodeBpsNumber(bpsPatch, ref pos);
                    targetRel += ((offsetData & 1UL) != 0 ? -1L : 1L) * (long)(offsetData >> 1);
                    for (int i = 0; i < length; i++) target[outputOffset++] = target[targetRel++];
                    break;
                }
            }
        }

        uint actualTargetCrc = ComputeCrc32UInt32(target);
        if (actualTargetCrc != expectedTargetCrc)
            throw new InvalidDataException($"BPS output target CRC32 mismatch (expected {expectedTargetCrc:X8}, got {actualTargetCrc:X8}).");
        return target;
    }

    public static byte[] CreateSimpleBpsPatch(byte[] sourceBytes, byte[] targetBytes)
    {
        using var ms = new MemoryStream();
        ms.Write("BPS1"u8);
        EncodeBpsNumber(ms, (ulong)sourceBytes.Length);
        EncodeBpsNumber(ms, (ulong)targetBytes.Length);
        EncodeBpsNumber(ms, 0UL);
        if (targetBytes.Length > 0)
        {
            EncodeBpsNumber(ms, (((ulong)targetBytes.Length - 1UL) << 2) | 1UL);
            ms.Write(targetBytes, 0, targetBytes.Length);
        }
        ms.Write(BitConverter.GetBytes(ComputeCrc32UInt32(sourceBytes)));
        ms.Write(BitConverter.GetBytes(ComputeCrc32UInt32(targetBytes)));
        byte[] preFooter = ms.ToArray();
        ms.Write(BitConverter.GetBytes(ComputeCrc32UInt32(preFooter)));
        return ms.ToArray();
    }

    public static byte[] ApplyUpsPatchInMemory(byte[] sourceRom, byte[] upsPatch)
    {
        if (upsPatch.Length < 18 || upsPatch[0] != (byte)'U' || upsPatch[1] != (byte)'P' ||
            upsPatch[2] != (byte)'S' || upsPatch[3] != (byte)'1')
            throw new InvalidDataException("Invalid UPS header: expected 'UPS1' magic.");

        int footerPos = upsPatch.Length - 12;
        uint inputCrc = BitConverter.ToUInt32(upsPatch, footerPos);
        uint outputCrc = BitConverter.ToUInt32(upsPatch, footerPos + 4);
        uint expectedPatchCrc = BitConverter.ToUInt32(upsPatch, footerPos + 8);

        uint actualPatchCrc = ComputeCrc32UInt32(upsPatch.AsSpan(0, upsPatch.Length - 4));
        if (actualPatchCrc != expectedPatchCrc)
            throw new InvalidDataException($"UPS patch CRC32 mismatch (expected {expectedPatchCrc:X8}, got {actualPatchCrc:X8}).");

        int pos = 4;
        ulong inputSize = DecodeBpsNumber(upsPatch, ref pos);
        ulong outputSize = DecodeBpsNumber(upsPatch, ref pos);
        uint actualSourceCrc = ComputeCrc32UInt32(sourceRom);
        ulong targetSize;
        uint expectedTargetCrc;

        if ((ulong)sourceRom.Length == inputSize && actualSourceCrc == inputCrc) { targetSize = outputSize; expectedTargetCrc = outputCrc; }
        else if ((ulong)sourceRom.Length == outputSize && actualSourceCrc == outputCrc) { targetSize = inputSize; expectedTargetCrc = inputCrc; }
        else throw new InvalidDataException($"UPS source ROM size/CRC32 mismatch (got size {sourceRom.Length}, CRC {actualSourceCrc:X8}; expected {inputSize}/{inputCrc:X8}).");

        byte[] target = new byte[targetSize];
        Buffer.BlockCopy(sourceRom, 0, target, 0, (int)Math.Min((ulong)sourceRom.Length, targetSize));

        long relativeOffset = 0;
        while (pos < footerPos)
        {
            relativeOffset += (long)DecodeBpsNumber(upsPatch, ref pos);
            while (pos < footerPos)
            {
                byte xorByte = upsPatch[pos++];
                if (xorByte == 0) { relativeOffset++; break; }
                if (relativeOffset >= 0 && (ulong)relativeOffset < targetSize)
                {
                    byte srcB = (ulong)relativeOffset < (ulong)sourceRom.Length ? sourceRom[relativeOffset] : (byte)0;
                    target[relativeOffset] = (byte)(srcB ^ xorByte);
                }
                relativeOffset++;
            }
        }

        uint actualTargetCrc = ComputeCrc32UInt32(target);
        if (actualTargetCrc != expectedTargetCrc)
            throw new InvalidDataException($"UPS output target CRC32 mismatch (expected {expectedTargetCrc:X8}, got {actualTargetCrc:X8}).");
        return target;
    }

    public static byte[] CreateSimpleUpsPatch(byte[] sourceBytes, byte[] targetBytes)
    {
        using var ms = new MemoryStream();
        ms.Write("UPS1"u8);
        EncodeBpsNumber(ms, (ulong)sourceBytes.Length);
        EncodeBpsNumber(ms, (ulong)targetBytes.Length);

        int maxLen = Math.Max(sourceBytes.Length, targetBytes.Length);
        int relative = 0;
        int i = 0;
        while (i < maxLen)
        {
            byte s = i < sourceBytes.Length ? sourceBytes[i] : (byte)0;
            byte t = i < targetBytes.Length ? targetBytes[i] : (byte)0;
            if (s == t) { i++; continue; }

            EncodeBpsNumber(ms, (ulong)(i - relative));
            while (i < maxLen)
            {
                byte sb = i < sourceBytes.Length ? sourceBytes[i] : (byte)0;
                byte tb = i < targetBytes.Length ? targetBytes[i] : (byte)0;
                byte diff = (byte)(sb ^ tb);
                if (diff == 0) break;
                ms.WriteByte(diff);
                i++;
            }
            ms.WriteByte(0);
            i++;
            relative = i;
        }

        ms.Write(BitConverter.GetBytes(ComputeCrc32UInt32(sourceBytes)));
        ms.Write(BitConverter.GetBytes(ComputeCrc32UInt32(targetBytes)));
        byte[] preFooter = ms.ToArray();
        ms.Write(BitConverter.GetBytes(ComputeCrc32UInt32(preFooter)));
        return ms.ToArray();
    }

    private static ulong DecodeBpsNumber(byte[] data, ref int pos)
    {
        ulong result = 0;
        ulong shift = 1;
        while (true)
        {
            if (pos >= data.Length) throw new InvalidDataException("Truncated BPS variable-length number.");
            byte x = data[pos++];
            result += (ulong)(x & 0x7F) * shift;
            if ((x & 0x80) != 0) break;
            shift <<= 7;
            result += shift;
        }
        return result;
    }

    private static void EncodeBpsNumber(Stream stream, ulong data)
    {
        while (true)
        {
            ulong x = data & 0x7FUL;
            data >>= 7;
            if (data == 0) { stream.WriteByte((byte)(0x80UL | x)); break; }
            stream.WriteByte((byte)x);
            data--;
        }
    }

    private static void EnsureOutputCapacity(List<byte> buf, int requiredCount)
    {
        while (buf.Count < requiredCount) buf.Add(0);
    }
}
