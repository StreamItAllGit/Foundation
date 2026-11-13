// @module: Foundation.Core.Imaging
// @purpose: Pure managed RGBA byte[] framebuffer canvas with Porter-Duff SrcOver blending, shapes, and built-in 5x7 ASCII bitmap font.
// @depends-on: System
// @exports: RgbaColor, PixelOverlayCanvas

using System;

namespace Foundation.Core;

public readonly record struct RgbaColor(byte R, byte G, byte B, byte A = 255);

/// <summary>
/// Pure-.NET RGBA pixel canvas with Porter-Duff SrcOver alpha compositing and built-in 5x7 bitmap text rendering
/// for SDL3/headless HUD overlays and deterministic PNG screenshot capture.
/// </summary>
public sealed class PixelOverlayCanvas
{
    private const int GlyphWidth = 5;
    private const int GlyphHeight = 7;
    private const int GlyphAdvanceX = 6;
    private const int GlyphAdvanceY = 9;

    // Packed 5x7 bitmaps (7 rows x 5 bits, MSB row first) for printable ASCII 0x20..0x7E.
    private static readonly ulong[] Font5x7 =
    [
        0x00000000000000, 0x04040404000400, 0x0A0A0000000000, 0x0A1F0A0A1F0A00, // ' ', '!', '"', '#'
        0x040F140E051E04, 0x191A04080B1300, 0x0C12140815120D, 0x04040000000000, // '$', '%', '&', '\''
        0x02040808080402, 0x08040202020408, 0x000A041F040A00, 0x0004041F040400, // '(', ')', '*', '+'
        0x00000000000408, 0x0000001F000000, 0x00000000000400, 0x01020408100000, // ',', '-', '.', '/'
        0x0E11131519110E, 0x040C040404040E, 0x0E11010608101F, 0x0E11010601110E, // '0'..'3'
        0x02060A121F0202, 0x1F101E0101110E, 0x0608101E11110E, 0x1F010204080808, // '4'..'7'
        0x0E11110E11110E, 0x0E11110F01020C, 0x00040000000400, 0x00040000000408, // '8'..'9', ':', ';'
        0x02040810080402, 0x00001F001F0000, 0x08040201020408, 0x0E110204040004, // '<', '=', '>', '?'
        0x0E11171517100E, 0x0E11111F111111, 0x1E11111E11111E, 0x0E11101010110E, // '@', 'A'..'C'
        0x1E11111111111E, 0x1F10101E10101F, 0x1F10101E101010, 0x0E11101711110F, // 'D'..'G'
        0x1111111F111111, 0x0E04040404040E, 0x0702020212120C, 0x11121418141211, // 'H'..'K'
        0x1010101010101F, 0x111B1515111111, 0x11191513111111, 0x0E11111111110E, // 'L'..'O'
        0x1E11111E101010, 0x0E11111115120D, 0x1E11111E141211, 0x0F10100E01011E, // 'P'..'S'
        0x1F040404040404, 0x1111111111110E, 0x11111111110A04, 0x11111115151B11, // 'T'..'W'
        0x11110A040A1111, 0x11110A04040404, 0x1F01020408101F, 0x0E08080808080E, // 'X'..'Z', '['
        0x10080402010000, 0x0E02020202020E, 0x040A1100000000, 0x0000000000001F, // '\\', ']', '^', '_'
        0x08040000000000, 0x00000E010F110F, 0x10101E1111111E, 0x00000E1110110E, // '`', 'a'..'c'
        0x01010F1111110F, 0x00000E111F100E, 0x0609081C080808, 0x000F11110F010E, // 'd'..'g'
        0x10101619111111, 0x04000C0404040E, 0x0200060202120C, 0x10101214181412, // 'h'..'k'
        0x0C04040404040E, 0x00001A15151111, 0x00001E11111111, 0x00000E1111110E, // 'l'..'o'
        0x00001E111E1010, 0x00000F110F0101, 0x00001619101010, 0x00000F100E011E, // 'p'..'s'
        0x08081E08080906, 0x0000111111130D, 0x00001111110A04, 0x0000111115150A, // 't'..'w'
        0x0000110A040A11, 0x000011110F010E, 0x00001F0204081F, 0x06080810080806, // 'x'..'z', '{'
        0x04040404040404, 0x0C02020102020C, 0x00000815020000                    // '|', '}', '~'
    ];

    public int Width { get; }
    public int Height { get; }
    public byte[] Buffer { get; }
    public ReadOnlySpan<byte> Pixels => Buffer;

    public PixelOverlayCanvas(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        Width = width;
        Height = height;
        Buffer = new byte[checked(width * height * 4)];
    }

    public PixelOverlayCanvas(int width, int height, byte[] existingRgbaBuffer, bool copyBuffer = true)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        ArgumentNullException.ThrowIfNull(existingRgbaBuffer);

        int expected = checked(width * height * 4);
        if (existingRgbaBuffer.Length != expected)
        {
            throw new ArgumentException($"Expected {expected} bytes for {width}x{height}.", nameof(existingRgbaBuffer));
        }

        Width = width;
        Height = height;
        Buffer = copyBuffer ? existingRgbaBuffer.AsSpan().ToArray() : existingRgbaBuffer;
    }

    public void Clear(RgbaColor color)
    {
        for (int i = 0; i < Buffer.Length; i += 4)
        {
            Buffer[i] = color.R;
            Buffer[i + 1] = color.G;
            Buffer[i + 2] = color.B;
            Buffer[i + 3] = color.A;
        }
    }

    public void BlendSrcOver(int x, int y, RgbaColor src)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height || src.A == 0)
        {
            return;
        }

        int offset = ((y * Width) + x) * 4;
        if (src.A == 255)
        {
            Buffer[offset] = src.R;
            Buffer[offset + 1] = src.G;
            Buffer[offset + 2] = src.B;
            Buffer[offset + 3] = 255;
            return;
        }

        float sa = src.A / 255.0f;
        float da = Buffer[offset + 3] / 255.0f;
        float invSa = 1.0f - sa;
        float outA = sa + (da * invSa);
        if (outA <= 0.00001f)
        {
            Buffer[offset] = 0;
            Buffer[offset + 1] = 0;
            Buffer[offset + 2] = 0;
            Buffer[offset + 3] = 0;
            return;
        }

        float invOutA = 1.0f / outA;
        Buffer[offset] = (byte)Math.Clamp((int)MathF.Round(((src.R * sa) + (Buffer[offset] * da * invSa)) * invOutA), 0, 255);
        Buffer[offset + 1] = (byte)Math.Clamp((int)MathF.Round(((src.G * sa) + (Buffer[offset + 1] * da * invSa)) * invOutA), 0, 255);
        Buffer[offset + 2] = (byte)Math.Clamp((int)MathF.Round(((src.B * sa) + (Buffer[offset + 2] * da * invSa)) * invOutA), 0, 255);
        Buffer[offset + 3] = (byte)Math.Clamp((int)MathF.Round(outA * 255.0f), 0, 255);
    }

    public void FillRect(int x, int y, int width, int height, RgbaColor color)
    {
        if (width <= 0 || height <= 0 || color.A == 0) return;

        int x0 = Math.Max(0, x);
        int y0 = Math.Max(0, y);
        int x1 = Math.Min(Width, x + width);
        int y1 = Math.Min(Height, y + height);

        for (int py = y0; py < y1; py++)
        {
            for (int px = x0; px < x1; px++)
            {
                BlendSrcOver(px, py, color);
            }
        }
    }

    public void StrokeRect(int x, int y, int width, int height, RgbaColor color, int thickness = 1)
    {
        if (width <= 0 || height <= 0 || thickness <= 0 || color.A == 0) return;
        int t = Math.Min(thickness, Math.Min(width, height));

        FillRect(x, y, width, t, color);
        if (height > t)
        {
            FillRect(x, y + height - t, width, t, color);
        }
        int innerH = height - (t * 2);
        if (innerH > 0)
        {
            FillRect(x, y + t, t, innerH, color);
            if (width > t)
            {
                FillRect(x + width - t, y + t, t, innerH, color);
            }
        }
    }

    public (int Width, int Height) MeasureText(string? text, int scale = 1)
    {
        if (string.IsNullOrEmpty(text)) return (0, 0);
        int s = Math.Max(1, scale);
        int maxCols = 0;
        int cols = 0;
        int lines = 1;

        foreach (char ch in text)
        {
            if (ch == '\r') continue;
            if (ch == '\n')
            {
                maxCols = Math.Max(maxCols, cols);
                cols = 0;
                lines++;
                continue;
            }
            cols++;
        }

        maxCols = Math.Max(maxCols, cols);
        if (maxCols == 0) return (0, lines * GlyphHeight * s);
        int w = ((maxCols * GlyphAdvanceX) - 1) * s;
        int h = ((lines * GlyphAdvanceY) - 2) * s;
        return (w, h);
    }

    public void DrawText(int x, int y, string? text, RgbaColor color, int scale = 1)
    {
        if (string.IsNullOrEmpty(text) || color.A == 0) return;
        int s = Math.Max(1, scale);
        int cursorX = x;
        int cursorY = y;

        foreach (char ch in text)
        {
            if (ch == '\r') continue;
            if (ch == '\n')
            {
                cursorX = x;
                cursorY += GlyphAdvanceY * s;
                continue;
            }

            ulong glyph = GetGlyph(ch);
            for (int row = 0; row < GlyphHeight; row++)
            {
                int rowBits = (int)((glyph >> ((GlyphHeight - 1 - row) * 8)) & 0x1F);
                if (rowBits == 0) continue;

                for (int col = 0; col < GlyphWidth; col++)
                {
                    if ((rowBits & (1 << (GlyphWidth - 1 - col))) != 0)
                    {
                        FillRect(cursorX + (col * s), cursorY + (row * s), s, s, color);
                    }
                }
            }

            cursorX += GlyphAdvanceX * s;
        }
    }

    public byte[] ToPngBytes() => RgbaPngWriter.Encode(Width, Height, Buffer);

    public void SavePng(string filePath) => RgbaPngWriter.WritePng(filePath, Width, Height, Buffer);

    private static ulong GetGlyph(char ch)
    {
        int idx = ch - ' ';
        if ((uint)idx < (uint)Font5x7.Length)
        {
            return Font5x7[idx];
        }
        return Font5x7['?' - ' '];
    }
}
