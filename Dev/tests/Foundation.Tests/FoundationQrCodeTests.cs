// @module: Foundation.Tests.QrCode
// @purpose: Validates ISO/IEC 18004 byte-mode Versions 1-10 ECC-M encoding, quiet zone SVG output, and capacity guards.
// @depends-on: System, Foundation.Core, Xunit
// @exports: FoundationQrCodeTests

using System;
using Foundation.Core;
using Xunit;

namespace Foundation.Tests;

public sealed class FoundationQrCodeTests
{
    [Fact]
    public void Encode_SelectsCorrectIsoVersionsUpToV10_WithFinderTimingAndVersionBits()
    {
        QrCodeMatrix v1 = QrCodeEncoder.Encode("http://127.0.0.1"); // 16 bytes -> V2 (V1 max is 14)
        Assert.Equal(2, v1.Version);
        Assert.Equal(25, v1.Size);

        QrCodeMatrix exactV1 = QrCodeEncoder.Encode("https://a.co"); // 12 bytes -> V1
        Assert.Equal(1, exactV1.Version);
        Assert.Equal(21, exactV1.Size);
        AssertFinderPattern(exactV1, 0, 0);
        AssertFinderPattern(exactV1, 0, 14);
        AssertFinderPattern(exactV1, 14, 0);
        Assert.True(exactV1.GetModule(exactV1.Size - 8, 8)); // fixed dark module

        string longTailscaleUrl = "https://mother-workstation-alpha.tail12345.ts.net:5100/app/media-tool/?tab=media-tool&mode=pwa&token=abcdefghij0123456789abcdefghij0123456789abcdefghij0123456789abcdefghij0123456789";
        QrCodeMatrix v9Or10 = QrCodeEncoder.Encode(longTailscaleUrl);
        Assert.InRange(v9Or10.Version, 7, 10);
        Assert.Equal(17 + (v9Or10.Version * 4), v9Or10.Size);
        AssertFinderPattern(v9Or10, 0, 0);
        AssertFinderPattern(v9Or10, 0, v9Or10.Size - 7);
        AssertFinderPattern(v9Or10, v9Or10.Size - 7, 0);

        // Exact 213 UTF-8 bytes fits V10-M
        QrCodeMatrix v10Max = QrCodeEncoder.Encode(new string('a', 213));
        Assert.Equal(10, v10Max.Version);
        Assert.Equal(57, v10Max.Size);
    }

    [Fact]
    public void Encode_RejectsNullEmptyAndOversizedPayloads()
    {
        Assert.ThrowsAny<ArgumentException>(() => QrCodeEncoder.Encode(""));
        Assert.ThrowsAny<ArgumentException>(() => QrCodeEncoder.Encode("   "));
        Assert.Throws<ArgumentException>(() => QrCodeEncoder.Encode(new string('x', 214)));
    }

    [Fact]
    public void RenderSvg_UsesFourModuleQuietZoneAndHighContrastDefaults()
    {
        string svg = QrCodeEncoder.RenderSvg("http://127.0.0.1:5100/");
        // 26 bytes fits V2 (25x25); 25 + 4*2 quiet zone = 33
        Assert.Contains("viewBox=\"0 0 33 33\"", svg);
        Assert.Contains("fill=\"#ffffff\"", svg);
        Assert.Contains("fill=\"#0b0e14\"", svg);
        Assert.Contains("shape-rendering=\"crispEdges\"", svg);

        string custom = QrCodeEncoder.RenderSvg("http://127.0.0.1:5100/", darkColor: "#38bdf8", lightColor: "#0b0e14", quietZoneModules: 2, ariaLabel: "Mother <QR>");
        // quietZone clamped to >= 4
        Assert.Contains("viewBox=\"0 0 33 33\"", custom);
        Assert.Contains("fill=\"#0b0e14\"", custom);
        Assert.Contains("fill=\"#38bdf8\"", custom);
        Assert.Contains("aria-label=\"Mother &lt;QR&gt;\"", custom);

        Assert.Equal(svg, QrCodeEncoder.RenderSvg("http://127.0.0.1:5100/"));
    }

    private static void AssertFinderPattern(QrCodeMatrix m, int topRow, int leftCol)
    {
        for (int dr = 0; dr < 7; dr++)
        {
            for (int dc = 0; dc < 7; dc++)
            {
                bool expected = dr is 0 or 6 || dc is 0 or 6 || (dr is >= 2 and <= 4 && dc is >= 2 and <= 4);
                Assert.Equal(expected, m.GetModule(topRow + dr, leftCol + dc));
            }
        }
    }
}
