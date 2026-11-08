// @module: Foundation.Retro.HeaderlessRomStripper
// @purpose: Copier/emulator header-skip detection (iNES, fwNES, SMC/SWC, A78, LNX) and 20-format binary magic header console/serial detector.
// @depends-on: System, System.Buffers.Binary, System.Collections.Generic, System.IO, System.Linq, System.Text, System.Text.RegularExpressions
// @exports: HeaderlessRomStripper, MagicBytePlatformDetector

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Foundation.Retro;

public static class HeaderlessRomStripper
{
    public static int DetectHeaderSkipLength(
        ReadOnlySpan<byte> leadingBytes,
        long totalFileSize,
        string fileExtension,
        IReadOnlyList<RetroHeaderSkipRule>? rules,
        out string? matchedRuleId)
    {
        matchedRuleId = null;
        var effectiveRules = rules ?? ConsoleSystemCatalog.BuildDefaultHeaderSkipRules();
        if (effectiveRules.Count == 0 || totalFileSize <= 0)
            return 0;

        string normExt = fileExtension.StartsWith('.') ? fileExtension.ToLowerInvariant() : "." + fileExtension.ToLowerInvariant();
        foreach (var rule in effectiveRules)
        {
            if (rule.HeaderByteLength <= 0 || totalFileSize <= rule.HeaderByteLength)
                continue;
            if (rule.ApplicableExtensions.Count > 0 &&
                !rule.ApplicableExtensions.Any(e => string.Equals(e, normExt, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (rule.SizeModulo.HasValue && rule.SizeModulo.Value > 0 && rule.ExpectedRemainder.HasValue &&
                (totalFileSize % rule.SizeModulo.Value) != rule.ExpectedRemainder.Value)
                continue;

            if (!string.IsNullOrWhiteSpace(rule.MagicHex))
            {
                byte[] magic = Convert.FromHexString(rule.MagicHex);
                if (leadingBytes.Length < rule.MagicByteOffset + magic.Length ||
                    !leadingBytes.Slice(rule.MagicByteOffset, magic.Length).SequenceEqual(magic))
                    continue;
            }

            matchedRuleId = rule.RuleId;
            return rule.HeaderByteLength;
        }

        return 0;
    }
}

public static class MagicBytePlatformDetector
{
    public static (string? DetectedPlatformId, string? SuggestedRoleCode, string? ExtractedSerial) InspectBinaryMagic(string filePath)
    {
        if (!File.Exists(filePath))
            return (null, null, null);

        try
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            string fileName = Path.GetFileName(filePath);

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length < 16)
                return (null, null, null);

            Span<byte> head = stackalloc byte[(int)Math.Min(512, fs.Length)];
            int read = fs.Read(head);
            head = head[..read];

            if (head.Length >= 8 && head[0] == 0x7F && head[1] == (byte)'P' && head[2] == (byte)'K' && head[3] == (byte)'G')
            {
                bool isPs3 = head[7] == 0x01 || fileName.Contains("PS3", StringComparison.OrdinalIgnoreCase) || fileName.Contains("multiMAN", StringComparison.OrdinalIgnoreCase);
                return (isPs3 ? "PS3" : "PSP", "Utility", null);
            }

            if (head.Length >= 4 && head[0] == 0x7F && head[1] == (byte)'C' && head[2] == (byte)'N' && head[3] == (byte)'T')
                return ("PS4", "Game", null);

            if ((head.Length >= 5 && Encoding.ASCII.GetString(head[..5]) == "SCEUF") || ext == ".pup")
                return (fileName.Contains("PS4", StringComparison.OrdinalIgnoreCase) ? "PS4" : "PS3", "Firmware", null);

            if (head.Length >= 4 && head[0] == 0x00 && head[1] == (byte)'P' && head[2] == (byte)'S' && head[3] == (byte)'F')
                return ("PS3", null, null);

            if (head.Length >= 8 && Encoding.ASCII.GetString(head[..8]) == "MComprHD")
                return (null, null, null);

            if (head.Length >= 4 && ((head[0] == 'R' && head[1] == 'V' && head[2] == 'Z') || (head[0] == 'W' && head[1] == 'I' && head[2] == 'A')))
            {
                if (head.Length >= 0x4C)
                {
                    uint discType = BinaryPrimitives.ReadUInt32BigEndian(head.Slice(0x48, 4));
                    if (discType == 1) return ("GC", "Game", null);
                    if (discType == 2) return ("Wii", "Game", null);
                }
            }

            if (head.Length >= 0x20)
            {
                uint wiiMagic = BinaryPrimitives.ReadUInt32BigEndian(head.Slice(0x18, 4));
                if (wiiMagic == 0x5D1C9EA3u)
                    return ("Wii", "Game", Encoding.ASCII.GetString(head[..6]).Trim('\0'));

                uint gcMagic = BinaryPrimitives.ReadUInt32BigEndian(head.Slice(0x1C, 4));
                if (gcMagic == 0xC2339F3Du)
                    return ("GC", "Game", Encoding.ASCII.GetString(head[..6]).Trim('\0'));
            }

            if (head.Length >= 4 && head[0] == 'N' && head[1] == 'E' && head[2] == 'S' && head[3] == 0x1A)
                return ("NES", "Game", null);
            if (head.Length >= 4 && head[0] == 'F' && head[1] == 'D' && head[2] == 'S' && head[3] == 0x1A)
                return ("FDS", "Game", null);

            if (head.Length >= 10 && Encoding.ASCII.GetString(head.Slice(1, 9)) == "ATARI7800")
                return ("Atari7800", "Game", null);
            if (head.Length >= 4 && Encoding.ASCII.GetString(head[..4]) == "LYNX")
                return ("Lynx", "Game", null);

            if (head.Length >= 4 && (ext is ".z64" or ".n64" or ".v64" or ".rom" or ".bin"))
            {
                uint n64 = BinaryPrimitives.ReadUInt32BigEndian(head[..4]);
                if (n64 is 0x80371240u or 0x37804012u or 0x40123780u)
                    return ("N64", "Game", null);
            }

            if (head.Length >= 0x105 && Encoding.ASCII.GetString(head.Slice(0x100, 4)) == "SEGA")
            {
                int headerSpanLen = Math.Min(128, head.Length - 0x100);
                string headerBlock = Encoding.ASCII.GetString(head.Slice(0x100, headerSpanLen));
                string sysTag = headerBlock.Length >= 16 ? headerBlock[..16] : headerBlock;

                if (sysTag.Contains("32X", StringComparison.OrdinalIgnoreCase))
                    return ("32X", "Game", null);
                if (sysTag.Contains("PICO", StringComparison.OrdinalIgnoreCase) ||
                    (fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && filePath.Contains("Pico", StringComparison.OrdinalIgnoreCase)))
                    return ("PICO", "Game", null);

                bool isSegaCdHeader = headerBlock.Contains("MEGA-CD", StringComparison.OrdinalIgnoreCase) ||
                                      headerBlock.Contains("SEGA-CD", StringComparison.OrdinalIgnoreCase) ||
                                      headerBlock.Contains("SEGACD", StringComparison.OrdinalIgnoreCase) ||
                                      headerBlock.Contains("CD-ROM", StringComparison.OrdinalIgnoreCase) ||
                                      headerBlock.Contains("BOOT", StringComparison.OrdinalIgnoreCase) ||
                                      headerBlock.Contains("BRAM", StringComparison.OrdinalIgnoreCase);

                bool isSegaCdFileName = fileName.StartsWith("bios_CD_", StringComparison.OrdinalIgnoreCase) ||
                                        fileName.StartsWith("jp_mcd", StringComparison.OrdinalIgnoreCase) ||
                                        fileName.StartsWith("us_scd", StringComparison.OrdinalIgnoreCase) ||
                                        fileName.StartsWith("eu_mcd", StringComparison.OrdinalIgnoreCase) ||
                                        fileName.Contains("boot_cd", StringComparison.OrdinalIgnoreCase);

                bool isSegaCdPath = filePath.Contains("SegaCD", StringComparison.OrdinalIgnoreCase) ||
                                    filePath.Contains("Mega-CD", StringComparison.OrdinalIgnoreCase) ||
                                    filePath.Contains("MegaCD", StringComparison.OrdinalIgnoreCase) ||
                                    filePath.Contains("Sega CD", StringComparison.OrdinalIgnoreCase);

                if (isSegaCdHeader || isSegaCdFileName || (isSegaCdPath && (fileName.Contains("bios", StringComparison.OrdinalIgnoreCase) || filePath.Contains("[bios]", StringComparison.OrdinalIgnoreCase))))
                    return ("SegaCD", "BIOS", null);

                return ("Genesis", "Game", null);
            }

            if (fs.Length >= 0x9000 && (ext is ".iso" or ".bin" or ".img"))
            {
                byte[] pvd = new byte[2048];
                fs.Seek(0x8000, SeekOrigin.Begin);
                if (fs.Read(pvd, 0, pvd.Length) == 2048 &&
                    pvd[1] == 'C' && pvd[2] == 'D' && pvd[3] == '0' && pvd[4] == '0' && pvd[5] == '1')
                {
                    string sysId = Encoding.ASCII.GetString(pvd, 8, 32).Trim();
                    if (sysId.Contains("PSP GAME", StringComparison.OrdinalIgnoreCase))
                        return ("PSP", "Game", null);
                    if (sysId.Contains("PLAYSTATION", StringComparison.OrdinalIgnoreCase))
                    {
                        byte[] scanBuf = new byte[(int)Math.Min(131072, fs.Length - 0x8800)];
                        fs.Seek(0x8800, SeekOrigin.Begin);
                        int sRead = fs.Read(scanBuf, 0, scanBuf.Length);
                        string ascii = Encoding.ASCII.GetString(scanBuf, 0, sRead);
                        if (ascii.Contains("BOOT2", StringComparison.OrdinalIgnoreCase))
                        {
                            var m2 = Regex.Match(ascii, @"([A-Z]{4})[_-](\d{3})\.(\d{2})");
                            string? ser2 = m2.Success ? $"{m2.Groups[1].Value}-{m2.Groups[2].Value}{m2.Groups[3].Value}" : null;
                            return ("PS2", "Game", ser2);
                        }
                        if (ascii.Contains("BOOT", StringComparison.OrdinalIgnoreCase))
                        {
                            var m1 = Regex.Match(ascii, @"([A-Z]{4})[_-](\d{3})\.(\d{2})");
                            string? ser1 = m1.Success ? $"{m1.Groups[1].Value}-{m1.Groups[2].Value}{m1.Groups[3].Value}" : null;
                            return ("PS1", "Game", ser1);
                        }
                    }
                }
            }
        }
        catch { }

        return (null, null, null);
    }
}
