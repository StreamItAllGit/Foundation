// @module: Foundation.Core.QrCodeEncoder
// @purpose: Zero-dependency ISO/IEC 18004 byte-mode QR encoder (Versions 1-10, ECC Level M) and crisp SVG renderer.
// @depends-on: System, System.Collections.Generic, System.Security, System.Text
// @exports: QrCodeMatrix, QrCodeEncoder

using System;
using System.Collections.Generic;
using System.Security;
using System.Text;

namespace Foundation.Core;

public sealed record QrCodeMatrix(int Version, int Size, bool[,] Modules)
{
    public bool GetModule(int row, int col) => Modules[row, col];
}

/// <summary>
/// Pure-.NET ISO/IEC 18004 byte-mode QR encoder supporting Versions 1-10 at Error Correction Level M (up to 213 UTF-8 bytes).
/// </summary>
public static class QrCodeEncoder
{
    private readonly record struct EccVersionSpec(int TotalCodewords, int EcPerBlock, int G1Blocks, int G1Data, int G2Blocks, int G2Data)
    {
        public int TotalDataCodewords => (G1Blocks * G1Data) + (G2Blocks * G2Data);
    }

    private static readonly EccVersionSpec[] VersionSpecsM =
    {
        default, // 1-indexed
        new(26, 10, 1, 16, 0, 0),   // V1 (14 byte cap)
        new(44, 16, 1, 28, 0, 0),   // V2 (26 byte cap)
        new(70, 26, 1, 44, 0, 0),   // V3 (42 byte cap)
        new(100, 18, 2, 32, 0, 0),  // V4 (62 byte cap)
        new(134, 24, 2, 43, 0, 0),  // V5 (84 byte cap)
        new(172, 16, 4, 27, 0, 0),  // V6 (106 byte cap)
        new(196, 18, 4, 31, 0, 0),  // V7 (122 byte cap)
        new(242, 22, 2, 38, 2, 39), // V8 (152 byte cap)
        new(292, 22, 3, 36, 2, 37), // V9 (180 byte cap)
        new(346, 26, 4, 43, 1, 44), // V10 (213 byte cap)
    };

    private static readonly int[][] AlignmentCoords =
    {
        Array.Empty<int>(),
        Array.Empty<int>(),
        new[] { 6, 18 },
        new[] { 6, 22 },
        new[] { 6, 26 },
        new[] { 6, 30 },
        new[] { 6, 34 },
        new[] { 6, 22, 38 },
        new[] { 6, 24, 42 },
        new[] { 6, 26, 46 },
        new[] { 6, 28, 50 },
    };

    public static QrCodeMatrix Encode(string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        byte[] utf8 = Encoding.UTF8.GetBytes(payload);
        int version = SelectVersion(utf8.Length);
        EccVersionSpec spec = VersionSpecsM[version];
        int size = 17 + (version * 4);

        byte[] dataCodewords = BuildDataCodewords(utf8, version, spec.TotalDataCodewords);
        byte[] interleaved = BuildInterleavedCodewords(dataCodewords, spec);

        bool[,] fnMask = new bool[size, size];
        bool[,] baseGrid = new bool[size, size];
        PlaceFunctionPatterns(baseGrid, fnMask, version, size);

        bool[,] bestGrid = baseGrid;
        int bestPenalty = int.MaxValue;
        for (int mask = 0; mask < 8; mask++)
        {
            var candidate = (bool[,])baseGrid.Clone();
            PlaceDataAndMask(candidate, fnMask, interleaved, size, mask);
            PlaceFormatBits(candidate, size, mask);
            int penalty = ComputePenalty(candidate, size);
            if (penalty < bestPenalty)
            {
                bestPenalty = penalty;
                bestGrid = candidate;
            }
        }

        return new QrCodeMatrix(version, size, bestGrid);
    }

    public static string RenderSvg(
        string payload,
        string darkColor = "#0b0e14",
        string lightColor = "#ffffff",
        int quietZoneModules = 4,
        string? ariaLabel = "QR code")
    {
        QrCodeMatrix matrix = Encode(payload);
        int qz = Math.Clamp(quietZoneModules, 4, 16);
        int total = matrix.Size + (qz * 2);
        string safeDark = string.IsNullOrWhiteSpace(darkColor) ? "#0b0e14" : SecurityElement.Escape(darkColor.Trim())!;
        string safeLight = string.IsNullOrWhiteSpace(lightColor) ? "#ffffff" : SecurityElement.Escape(lightColor.Trim())!;
        string safeLabel = SecurityElement.Escape(string.IsNullOrWhiteSpace(ariaLabel) ? "QR code" : ariaLabel.Trim())!;

        var sb = new StringBuilder(matrix.Size * matrix.Size * 6);
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {total} {total}\" shape-rendering=\"crispEdges\" role=\"img\" aria-label=\"{safeLabel}\">");
        sb.Append($"<rect width=\"{total}\" height=\"{total}\" fill=\"{safeLight}\"/>");
        sb.Append($"<path fill=\"{safeDark}\" d=\"");
        for (int r = 0; r < matrix.Size; r++)
        {
            for (int c = 0; c < matrix.Size; c++)
            {
                if (matrix.Modules[r, c])
                {
                    sb.Append($"M{c + qz},{r + qz}h1v1h-1z");
                }
            }
        }
        sb.Append("\"/></svg>");
        return sb.ToString();
    }

    private static int SelectVersion(int byteCount)
    {
        for (int v = 1; v <= 10; v++)
        {
            int countBits = v >= 10 ? 16 : 8;
            int capacity = ((VersionSpecsM[v].TotalDataCodewords * 8) - 4 - countBits) / 8;
            if (byteCount <= capacity) return v;
        }
        throw new ArgumentException($"Payload length ({byteCount} UTF-8 bytes) exceeds ISO/IEC 18004 V10-M capacity (213 bytes).", nameof(byteCount));
    }

    private static byte[] BuildDataCodewords(byte[] utf8, int version, int totalDataCodewords)
    {
        var bits = new List<bool>(totalDataCodewords * 8);
        AppendBits(bits, 0b0100, 4);
        AppendBits(bits, utf8.Length, version >= 10 ? 16 : 8);
        foreach (byte b in utf8) AppendBits(bits, b, 8);

        int capacityBits = totalDataCodewords * 8;
        int terminator = Math.Min(4, capacityBits - bits.Count);
        AppendBits(bits, 0, terminator);
        while ((bits.Count & 7) != 0) bits.Add(false);

        var bytes = new byte[totalDataCodewords];
        for (int i = 0; i < bits.Count; i++)
        {
            if (bits[i]) bytes[i >> 3] |= (byte)(1 << (7 - (i & 7)));
        }

        int written = bits.Count >> 3;
        for (int i = written; i < totalDataCodewords; i++)
        {
            bytes[i] = ((i - written) & 1) == 0 ? (byte)0xEC : (byte)0x11;
        }
        return bytes;
    }

    private static void AppendBits(List<bool> bits, int value, int count)
    {
        for (int i = count - 1; i >= 0; i--)
        {
            bits.Add(((value >> i) & 1) != 0);
        }
    }

    private static byte[] BuildInterleavedCodewords(byte[] data, EccVersionSpec spec)
    {
        int totalBlocks = spec.G1Blocks + spec.G2Blocks;
        var dataBlocks = new byte[totalBlocks][];
        var eccBlocks = new byte[totalBlocks][];
        byte[] genPoly = BuildRsGeneratorPoly(spec.EcPerBlock);

        int offset = 0;
        for (int b = 0; b < totalBlocks; b++)
        {
            int len = b < spec.G1Blocks ? spec.G1Data : spec.G2Data;
            byte[] block = new byte[len];
            Array.Copy(data, offset, block, 0, len);
            offset += len;
            dataBlocks[b] = block;
            eccBlocks[b] = ComputeRsRemainder(block, genPoly);
        }

        var result = new byte[spec.TotalCodewords];
        int outIdx = 0;
        int maxDataLen = Math.Max(spec.G1Data, spec.G2Data);
        for (int i = 0; i < maxDataLen; i++)
        {
            for (int b = 0; b < totalBlocks; b++)
            {
                if (i < dataBlocks[b].Length) result[outIdx++] = dataBlocks[b][i];
            }
        }
        for (int i = 0; i < spec.EcPerBlock; i++)
        {
            for (int b = 0; b < totalBlocks; b++)
            {
                result[outIdx++] = eccBlocks[b][i];
            }
        }
        return result;
    }

    private static byte[] BuildRsGeneratorPoly(int degree)
    {
        byte[] poly = new byte[degree];
        poly[degree - 1] = 1;
        byte root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (int j = 0; j < degree; j++)
            {
                poly[j] = GfMul(poly[j], root);
                if (j + 1 < degree) poly[j] ^= poly[j + 1];
            }
            root = GfMul(root, 2);
        }
        return poly;
    }

    private static byte[] ComputeRsRemainder(byte[] data, byte[] genPoly)
    {
        int ecLen = genPoly.Length;
        byte[] rem = new byte[ecLen];
        foreach (byte b in data)
        {
            byte factor = (byte)(b ^ rem[0]);
            Array.Copy(rem, 1, rem, 0, ecLen - 1);
            rem[ecLen - 1] = 0;
            for (int i = 0; i < ecLen; i++)
            {
                rem[i] ^= GfMul(genPoly[i], factor);
            }
        }
        return rem;
    }

    private static byte GfMul(byte x, byte y)
    {
        int res = 0;
        for (int bit = 7; bit >= 0; bit--)
        {
            res = (res << 1) ^ ((res >> 7) * 0x11D);
            if (((y >> bit) & 1) != 0) res ^= x;
        }
        return (byte)res;
    }

    private static void PlaceFunctionPatterns(bool[,] grid, bool[,] fnMask, int version, int size)
    {
        for (int i = 0; i < size; i++)
        {
            SetFn(grid, fnMask, 6, i, (i & 1) == 0);
            SetFn(grid, fnMask, i, 6, (i & 1) == 0);
        }
        PlaceFinder(grid, fnMask, 0, 0, size);
        PlaceFinder(grid, fnMask, 0, size - 7, size);
        PlaceFinder(grid, fnMask, size - 7, 0, size);

        int[] coords = AlignmentCoords[version];
        foreach (int r in coords)
        {
            foreach (int c in coords)
            {
                if ((r == 6 && c == 6) || (r == 6 && c == coords[^1]) || (r == coords[^1] && c == 6)) continue;
                PlaceAlignment(grid, fnMask, r, c);
            }
        }

        // Reserve format info coordinates & dark module (4*V + 9, 8)
        for (int i = 0; i < 9; i++)
        {
            if (i != 6) { fnMask[8, i] = true; fnMask[i, 8] = true; }
        }
        for (int i = size - 8; i < size; i++)
        {
            fnMask[8, i] = true;
            fnMask[i, 8] = true;
        }
        SetFn(grid, fnMask, size - 8, 8, true);

        if (version >= 7) PlaceVersionBits(grid, fnMask, version, size);
    }

    private static void PlaceFinder(bool[,] grid, bool[,] fnMask, int topRow, int leftCol, int size)
    {
        for (int dr = -1; dr <= 7; dr++)
        {
            for (int dc = -1; dc <= 7; dc++)
            {
                int r = topRow + dr, c = leftCol + dc;
                if ((uint)r >= (uint)size || (uint)c >= (uint)size) continue;
                int dist = Math.Max(Math.Abs(dr - 3), Math.Abs(dc - 3));
                SetFn(grid, fnMask, r, c, dist is 0 or 1 or 3);
            }
        }
    }

    private static void PlaceAlignment(bool[,] grid, bool[,] fnMask, int centerRow, int centerCol)
    {
        for (int dr = -2; dr <= 2; dr++)
        {
            for (int dc = -2; dc <= 2; dc++)
            {
                SetFn(grid, fnMask, centerRow + dr, centerCol + dc, Math.Max(Math.Abs(dr), Math.Abs(dc)) != 1);
            }
        }
    }

    private static void PlaceVersionBits(bool[,] grid, bool[,] fnMask, int version, int size)
    {
        int rem = version;
        for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
        int bits = (version << 12) | rem;
        for (int i = 0; i < 18; i++)
        {
            bool dark = ((bits >> i) & 1) != 0;
            int a = size - 11 + (i % 3);
            int b = i / 3;
            SetFn(grid, fnMask, b, a, dark);
            SetFn(grid, fnMask, a, b, dark);
        }
    }

    private static void PlaceDataAndMask(bool[,] grid, bool[,] fnMask, byte[] codewords, int size, int mask)
    {
        int bitIdx = 0;
        int totalBits = codewords.Length * 8;
        bool upward = true;
        for (int right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int step = 0; step < size; step++)
            {
                int r = upward ? size - 1 - step : step;
                for (int dx = 0; dx < 2; dx++)
                {
                    int c = right - dx;
                    if (fnMask[r, c]) continue;
                    bool bit = bitIdx < totalBits && ((codewords[bitIdx >> 3] >> (7 - (bitIdx & 7))) & 1) != 0;
                    bitIdx++;
                    grid[r, c] = bit ^ IsMasked(mask, r, c);
                }
            }
            upward = !upward;
        }
    }

    private static bool IsMasked(int mask, int r, int c) => mask switch
    {
        0 => ((r + c) & 1) == 0,
        1 => (r & 1) == 0,
        2 => (c % 3) == 0,
        3 => ((r + c) % 3) == 0,
        4 => (((r / 2) + (c / 3)) & 1) == 0,
        5 => ((r * c) & 1) + ((r * c) % 3) == 0,
        6 => ((((r * c) & 1) + ((r * c) % 3)) & 1) == 0,
        _ => ((((r + c) & 1) + ((r * c) % 3)) & 1) == 0,
    };

    private static void PlaceFormatBits(bool[,] grid, int size, int mask)
    {
        int data = mask; // ECC Level M indicator is 00
        int rem = data;
        for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        int bits = ((data << 10) | rem) ^ 0x5412;

        for (int i = 0; i <= 5; i++) grid[i, 8] = ((bits >> i) & 1) != 0;
        grid[7, 8] = ((bits >> 6) & 1) != 0;
        grid[8, 8] = ((bits >> 7) & 1) != 0;
        grid[8, 7] = ((bits >> 8) & 1) != 0;
        for (int i = 9; i < 15; i++) grid[8, 14 - i] = ((bits >> i) & 1) != 0;

        for (int i = 0; i < 8; i++) grid[8, size - 1 - i] = ((bits >> i) & 1) != 0;
        for (int i = 8; i < 15; i++) grid[size - 15 + i, 8] = ((bits >> i) & 1) != 0;
        grid[size - 8, 8] = true;
    }

    private static int ComputePenalty(bool[,] grid, int size)
    {
        int penalty = 0;
        int darkCount = 0;
        for (int i = 0; i < size; i++)
        {
            penalty += ScoreLineRunAndPattern(grid, size, i, rowMode: true);
            penalty += ScoreLineRunAndPattern(grid, size, i, rowMode: false);
            for (int j = 0; j < size; j++)
            {
                if (grid[i, j]) darkCount++;
                if (i + 1 < size && j + 1 < size &&
                    grid[i, j] == grid[i, j + 1] &&
                    grid[i, j] == grid[i + 1, j] &&
                    grid[i, j] == grid[i + 1, j + 1])
                {
                    penalty += 3;
                }
            }
        }
        int total = size * size;
        int pctDiff = Math.Abs((darkCount * 200) - (total * 100));
        penalty += (pctDiff / (total * 10)) * 10;
        return penalty;
    }

    private static int ScoreLineRunAndPattern(bool[,] grid, int size, int fixedIdx, bool rowMode)
    {
        int score = 0, runLen = 0, runHist = 0;
        bool prev = false;
        for (int k = 0; k < size; k++)
        {
            bool cur = rowMode ? grid[fixedIdx, k] : grid[k, fixedIdx];
            runHist = ((runHist << 1) | (cur ? 1 : 0)) & 0x7FF;
            if (k >= 10 && (runHist == 0b10111010000 || runHist == 0b00001011101)) score += 40;
            if (k == 0 || cur != prev) { prev = cur; runLen = 1; }
            else { runLen++; if (runLen == 5) score += 3; else if (runLen > 5) score++; }
        }
        return score;
    }

    private static void SetFn(bool[,] grid, bool[,] fnMask, int row, int col, bool dark)
    {
        grid[row, col] = dark;
        fnMask[row, col] = true;
    }
}
