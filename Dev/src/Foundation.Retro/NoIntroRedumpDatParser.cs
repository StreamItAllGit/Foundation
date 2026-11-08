// @module: Foundation.Retro.NoIntroRedumpDatParser
// @purpose: Logiqx, No-Intro, Redump, MAME, RetroAchievements XML, ClrMamePro text, and .sfv/.md5/.sha1 DAT parser.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Security.Cryptography, System.Text, System.Text.RegularExpressions, System.Xml
// @exports: ParsedDatCatalogHeader, ParsedDatRomRecord, NoIntroRedumpDatParser

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Foundation.Retro;

public sealed class ParsedDatCatalogHeader
{
    public string CatalogId { get; set; } = "";
    public string PlatformId { get; set; } = "Unknown";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string SourceKind { get; set; } = "Standard";
    public string SourcePath { get; set; } = "";
    public int EntryCount { get; set; }
    public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ParsedDatRomRecord
{
    public string CatalogId { get; set; } = "";
    public string PlatformId { get; set; } = "";
    public string GameName { get; set; } = "";
    public string RomName { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Crc32 { get; set; } = "";
    public string Md5 { get; set; } = "";
    public string Sha1 { get; set; } = "";
    public string Serial { get; set; } = "";
    public string Region { get; set; } = "Unknown";
    public bool IsBios { get; set; }
    public string ParentName { get; set; } = "";
    public string Status { get; set; } = "";
}

public static class NoIntroRedumpDatParser
{
    public static (ParsedDatCatalogHeader Catalog, List<ParsedDatRomRecord> Entries) ParseDatFile(
        string datFilePath,
        string? explicitPlatformId = null,
        string? explicitSourceKind = null,
        Func<string?, string?>? platformResolver = null,
        IReadOnlyList<RetroProvenanceTagRule>? provenanceRules = null)
    {
        string rawHead;
        using (var sr = new StreamReader(datFilePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            char[] buf = new char[512];
            int n = sr.Read(buf, 0, buf.Length);
            rawHead = new string(buf, 0, n).TrimStart();
        }

        var result = rawHead.StartsWith('<')
            ? ParseXmlDatFile(datFilePath, explicitPlatformId, platformResolver, provenanceRules)
            : ParseClrMameProOrChecksumDat(datFilePath, explicitPlatformId, platformResolver, provenanceRules);

        result.Catalog.SourceKind = DetectDatSourceKind(explicitSourceKind, result.Catalog.Name, datFilePath);
        return result;
    }

    public static string DetectDatSourceKind(string? explicitSourceKind, string headerName, string datFilePath)
    {
        if (!string.IsNullOrWhiteSpace(explicitSourceKind) &&
            !string.Equals(explicitSourceKind, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            string trimmed = explicitSourceKind.Trim();
            if (trimmed.StartsWith("retro", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "RA", StringComparison.OrdinalIgnoreCase))
                return "RetroAchievements";
            if (string.Equals(trimmed, "Custom", StringComparison.OrdinalIgnoreCase))
                return "Custom";
            return "Standard";
        }

        string probe = $"{headerName} {Path.GetFileName(datFilePath)}";
        if (probe.Contains("RetroAchievements", StringComparison.OrdinalIgnoreCase) ||
            probe.Contains("Unofficial-RA", StringComparison.OrdinalIgnoreCase) ||
            probe.Contains("RADATool", StringComparison.OrdinalIgnoreCase) ||
            probe.Contains("rcheevos", StringComparison.OrdinalIgnoreCase) ||
            probe.Contains("(RA)", StringComparison.OrdinalIgnoreCase) ||
            probe.Contains("_RA_", StringComparison.OrdinalIgnoreCase) ||
            probe.StartsWith("RA - ", StringComparison.OrdinalIgnoreCase))
        {
            return "RetroAchievements";
        }
        return "Standard";
    }

    public static string ResolveDatPlatformId(
        string? explicitPlatformId,
        string headerName,
        string datFilePath,
        Func<string?, string?>? platformResolver = null)
    {
        Func<string?, string?> resolve = platformResolver ?? (hint => ConsoleSystemCatalog.ResolvePlatformId(hint));
        if (!string.IsNullOrWhiteSpace(explicitPlatformId))
        {
            return resolve(explicitPlatformId) ?? explicitPlatformId.Trim();
        }

        string cleanedHeader = Regex.Replace(headerName, @"\s*-\s*Datfile.*$|\s*\(\d+\).*$", "", RegexOptions.IgnoreCase).Trim();
        string? fromHeader = resolve(cleanedHeader);
        if (!string.IsNullOrWhiteSpace(fromHeader)) return fromHeader;

        string fileBase = Regex.Replace(Path.GetFileNameWithoutExtension(datFilePath), @"\s*-\s*Datfile.*$|\s*\(.*$", "", RegexOptions.IgnoreCase).Trim();
        string? fromFile = resolve(fileBase);
        if (!string.IsNullOrWhiteSpace(fromFile)) return fromFile;

        int dash = cleanedHeader.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0)
        {
            string? afterDash = resolve(cleanedHeader[(dash + 3)..].Trim());
            if (!string.IsNullOrWhiteSpace(afterDash)) return afterDash;
        }

        return "Unknown";
    }

    private static (ParsedDatCatalogHeader Catalog, List<ParsedDatRomRecord> Entries) ParseXmlDatFile(
        string datFilePath,
        string? explicitPlatformId,
        Func<string?, string?>? platformResolver,
        IReadOnlyList<RetroProvenanceTagRule>? provenanceRules)
    {
        string headerName = Path.GetFileNameWithoutExtension(datFilePath);
        string headerVersion = "";
        var entries = new List<ParsedDatRomRecord>();

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreComments = true,
            IgnoreWhitespace = true
        };

        string? currentGameName = null;
        string currentParent = "";
        bool currentIsBios = false;
        string currentSerial = "";

        using (var reader = XmlReader.Create(datFilePath, settings))
        {
            bool insideHeader = false;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    string el = reader.Name;
                    if (el.Equals("header", StringComparison.OrdinalIgnoreCase)) insideHeader = true;
                    else if (insideHeader && el.Equals("name", StringComparison.OrdinalIgnoreCase)) headerName = reader.ReadElementContentAsString().Trim();
                    else if (insideHeader && el.Equals("version", StringComparison.OrdinalIgnoreCase)) headerVersion = reader.ReadElementContentAsString().Trim();
                    else if (el.Equals("game", StringComparison.OrdinalIgnoreCase) || el.Equals("machine", StringComparison.OrdinalIgnoreCase))
                    {
                        currentGameName = reader.GetAttribute("name") ?? "";
                        currentParent = reader.GetAttribute("cloneof") ?? reader.GetAttribute("romof") ?? "";
                        string? isBiosAttr = reader.GetAttribute("isbios");
                        currentIsBios = string.Equals(isBiosAttr, "yes", StringComparison.OrdinalIgnoreCase) ||
                                        currentGameName.Contains("[BIOS]", StringComparison.OrdinalIgnoreCase);
                        currentSerial = "";
                    }
                    else if (currentGameName != null && el.Equals("serial", StringComparison.OrdinalIgnoreCase))
                    {
                        currentSerial = reader.ReadElementContentAsString().Trim();
                    }
                    else if (currentGameName != null && el.Equals("rom", StringComparison.OrdinalIgnoreCase))
                    {
                        string romName = reader.GetAttribute("name") ?? currentGameName;
                        long.TryParse(reader.GetAttribute("size"), out long sz);
                        string crc = (reader.GetAttribute("crc") ?? "").Trim().ToUpperInvariant();
                        string md5 = (reader.GetAttribute("md5") ?? "").Trim().ToUpperInvariant();
                        string sha1 = (reader.GetAttribute("sha1") ?? "").Trim().ToUpperInvariant();
                        string serial = (reader.GetAttribute("serial") ?? currentSerial).Trim();
                        string status = (reader.GetAttribute("status") ?? "").Trim().ToLowerInvariant();
                        var parsedName = DatTitleNormalizer.ParseFilename(currentGameName, provenanceRules);

                        entries.Add(new ParsedDatRomRecord
                        {
                            GameName = currentGameName,
                            RomName = romName,
                            SizeBytes = sz,
                            Crc32 = crc,
                            Md5 = md5,
                            Sha1 = sha1,
                            Serial = serial,
                            Region = parsedName.Region,
                            IsBios = currentIsBios,
                            ParentName = currentParent,
                            Status = status
                        });
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (reader.Name.Equals("header", StringComparison.OrdinalIgnoreCase)) insideHeader = false;
                    else if (reader.Name.Equals("game", StringComparison.OrdinalIgnoreCase) || reader.Name.Equals("machine", StringComparison.OrdinalIgnoreCase)) currentGameName = null;
                }
            }
        }

        return FinalizeCatalog(datFilePath, explicitPlatformId, headerName, headerVersion, entries, platformResolver);
    }

    private static (ParsedDatCatalogHeader Catalog, List<ParsedDatRomRecord> Entries) ParseClrMameProOrChecksumDat(
        string datFilePath,
        string? explicitPlatformId,
        Func<string?, string?>? platformResolver,
        IReadOnlyList<RetroProvenanceTagRule>? provenanceRules)
    {
        string text = File.ReadAllText(datFilePath, Encoding.UTF8);
        string headerName = Path.GetFileNameWithoutExtension(datFilePath);
        string headerVersion = "";
        var entries = new List<ParsedDatRomRecord>();

        var headerMatch = Regex.Match(text, @"clrmamepro\s*\((?<inner>[\s\S]*?)\)", RegexOptions.IgnoreCase);
        if (headerMatch.Success)
        {
            string hInner = headerMatch.Groups["inner"].Value;
            var nm = Regex.Match(hInner, @"name\s+""([^""]+)""", RegexOptions.IgnoreCase);
            if (nm.Success) headerName = nm.Groups[1].Value.Trim();
            var vm = Regex.Match(hInner, @"version\s+""?([^\r\n""]+)""?", RegexOptions.IgnoreCase);
            if (vm.Success) headerVersion = vm.Groups[1].Value.Trim();
        }

        foreach (string body in ExtractBalancedClrBlocks(text, "game").Concat(ExtractBalancedClrBlocks(text, "machine")))
        {
            var gNameMatch = Regex.Match(body, @"name\s+(?:""([^""]+)""|(\S+))", RegexOptions.IgnoreCase);
            string gameName = gNameMatch.Success
                ? (!string.IsNullOrEmpty(gNameMatch.Groups[1].Value) ? gNameMatch.Groups[1].Value : gNameMatch.Groups[2].Value)
                : "Unknown";
            string gameSerial = MatchClrToken(body, "serial") ?? "";
            bool isBios = gameName.Contains("[BIOS]", StringComparison.OrdinalIgnoreCase);
            var parsedMeta = DatTitleNormalizer.ParseFilename(gameName, provenanceRules);

            foreach (string r in ExtractBalancedClrBlocks(body, "rom").Concat(ExtractBalancedClrBlocks(body, "disk")))
            {
                string romName = MatchClrToken(r, "name") ?? gameName;
                long.TryParse(MatchClrToken(r, "size"), out long sz);
                entries.Add(new ParsedDatRomRecord
                {
                    GameName = gameName,
                    RomName = romName,
                    SizeBytes = sz,
                    Crc32 = (MatchClrToken(r, "crc") ?? "").ToUpperInvariant(),
                    Md5 = (MatchClrToken(r, "md5") ?? "").ToUpperInvariant(),
                    Sha1 = (MatchClrToken(r, "sha1") ?? "").ToUpperInvariant(),
                    Serial = MatchClrToken(r, "serial") ?? gameSerial,
                    Region = parsedMeta.Region,
                    IsBios = isBios
                });
            }
        }

        if (entries.Count == 0)
        {
            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith(';') || line.StartsWith('#')) continue;

                var md5Match = Regex.Match(line, @"^([0-9a-fA-F]{32}|[0-9a-fA-F]{40})\s+\*?(.+)$");
                if (md5Match.Success)
                {
                    string hash = md5Match.Groups[1].Value.ToUpperInvariant();
                    string fn = md5Match.Groups[2].Value.Trim();
                    string gn = Path.GetFileNameWithoutExtension(fn);
                    entries.Add(new ParsedDatRomRecord
                    {
                        GameName = gn, RomName = fn, Md5 = hash.Length == 32 ? hash : "", Sha1 = hash.Length == 40 ? hash : "",
                        Region = DatTitleNormalizer.ParseFilename(gn, provenanceRules).Region
                    });
                    continue;
                }

                var sfvMatch = Regex.Match(line, @"^(.+?)\s+([0-9a-fA-F]{8})$");
                if (sfvMatch.Success)
                {
                    string fn = sfvMatch.Groups[1].Value.Trim();
                    string crc = sfvMatch.Groups[2].Value.ToUpperInvariant();
                    string gn = Path.GetFileNameWithoutExtension(fn);
                    entries.Add(new ParsedDatRomRecord
                    {
                        GameName = gn, RomName = fn, Crc32 = crc,
                        Region = DatTitleNormalizer.ParseFilename(gn, provenanceRules).Region
                    });
                }
            }
        }

        return FinalizeCatalog(datFilePath, explicitPlatformId, headerName, headerVersion, entries, platformResolver);
    }

    private static (ParsedDatCatalogHeader Catalog, List<ParsedDatRomRecord> Entries) FinalizeCatalog(
        string datFilePath,
        string? explicitPlatformId,
        string headerName,
        string headerVersion,
        List<ParsedDatRomRecord> entries,
        Func<string?, string?>? platformResolver)
    {
        string resolvedPlatform = ResolveDatPlatformId(explicitPlatformId, headerName, datFilePath, platformResolver);
        string catalogId = "dat-" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{resolvedPlatform}|{headerName}|{Path.GetFileName(datFilePath)}"))).ToLowerInvariant()[..12];

        foreach (var e in entries)
        {
            e.CatalogId = catalogId;
            e.PlatformId = resolvedPlatform;
        }

        return (new ParsedDatCatalogHeader
        {
            CatalogId = catalogId, PlatformId = resolvedPlatform, Name = headerName, Version = headerVersion,
            SourcePath = Path.GetFullPath(datFilePath), EntryCount = entries.Count, ImportedAtUtc = DateTime.UtcNow
        }, entries);
    }

    private static List<string> ExtractBalancedClrBlocks(string text, string keyword)
    {
        var results = new List<string>();
        int idx = 0;
        while (idx < text.Length)
        {
            int kwPos = text.IndexOf(keyword, idx, StringComparison.OrdinalIgnoreCase);
            if (kwPos < 0) break;

            bool validStart = kwPos == 0 || !char.IsLetterOrDigit(text[kwPos - 1]);
            int afterKw = kwPos + keyword.Length;
            bool validEnd = afterKw < text.Length && (char.IsWhiteSpace(text[afterKw]) || text[afterKw] == '(');
            if (!validStart || !validEnd) { idx = afterKw; continue; }

            int openParen = afterKw;
            while (openParen < text.Length && char.IsWhiteSpace(text[openParen])) openParen++;
            if (openParen >= text.Length || text[openParen] != '(') { idx = afterKw; continue; }

            int depth = 1;
            bool inQuotes = false;
            int cursor = openParen + 1;
            while (cursor < text.Length && depth > 0)
            {
                char c = text[cursor];
                if (c == '"') inQuotes = !inQuotes;
                else if (!inQuotes) { if (c == '(') depth++; else if (c == ')') depth--; }
                cursor++;
            }

            if (depth == 0) { results.Add(text.Substring(openParen + 1, cursor - openParen - 2)); idx = cursor; }
            else break;
        }
        return results;
    }

    private static string? MatchClrToken(string block, string key)
    {
        var m = Regex.Match(block, $@"\b{Regex.Escape(key)}\s+(?:""([^""]+)""|(\S+))", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return !string.IsNullOrEmpty(m.Groups[1].Value) ? m.Groups[1].Value : m.Groups[2].Value;
    }
}
