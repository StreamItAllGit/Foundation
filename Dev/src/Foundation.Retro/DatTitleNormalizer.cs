// @module: Foundation.Retro.DatTitleNormalizer
// @purpose: Filename/region/revision/language/provenance/serial parser, malformed tag syntax healer, and title key normalizer.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Text, System.Text.RegularExpressions
// @exports: ParsedRomFilenameMetadata, DatTitleNormalizer

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Foundation.Retro;

public sealed class ParsedRomFilenameMetadata
{
    public string RawBaseName { get; set; } = "";
    public string CleanTitle { get; set; } = "";
    public string DatCorrelationName { get; set; } = "";
    public string Region { get; set; } = "Unknown";
    public string Languages { get; set; } = "";
    public string Revision { get; set; } = "";
    public string Serial { get; set; } = "";
    public int DiscNumber { get; set; } = 1;
    public bool HasDiscMarker { get; set; }
    public string? ExplicitProvenanceCode { get; set; }
    public bool IsRomHackTagged { get; set; }
    public bool IsHomebrewTagged { get; set; }
    public bool HasMalformedTags { get; set; }
    public string? SuggestedFixedFileName { get; set; }
    public string? LintReason { get; set; }
}

public static class DatTitleNormalizer
{
    private static readonly Regex ParenOrBracketRegex = new(@"\(([^()]+)\)|\[([^\[\]]+)\]", RegexOptions.Compiled);
    private static readonly Regex NestedBrokenParenRegex = new(@"\(([^()]*?)\s+(\([^()]+\))", RegexOptions.Compiled);
    private static readonly Regex DiscRegex = new(@"^(?:Disc|Disk|CD|GD)\s*(\d+)(?:\s*of\s*\d+)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RevisionRegex = new(@"^(?:Rev\s*[A-Z0-9.]+|v\d+(?:\.\d+)*[a-z]?|\d+\.\d+(?:\.\d+)*[a-z]?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LanguageListRegex = new(@"^(?:En|Ja|Fr|De|Es|It|Nl|Pt|Sv|No|Da|Fi|Zh|Ko|Pl|Ru)(?:,\s*(?:En|Ja|Fr|De|Es|It|Nl|Pt|Sv|No|Da|Fi|Zh|Ko|Pl|Ru))*$", RegexOptions.Compiled);
    private static readonly Regex SerialRegex = new(@"^(?:BLUS|BCUS|BLES|BCES|BLJM|BCJS|NPUA|NPUB|NPEB|NPJB|CUSA|PPSA|SLUS|SCUS|SLES|SCES|SLPS|SLPM|SCPS|ULUS|ULES|UCUS|UCES|PCSE|PCSA|HAC|CTR|WUP)[-_]?\d{4,6}[A-Z0-9]*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MultiSpaceRegex = new(@"\s{2,}", RegexOptions.Compiled);

    public static readonly IReadOnlyDictionary<string, string> SingleRegionAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["USA"] = "USA", ["US"] = "USA", ["U"] = "USA", ["America"] = "USA", ["North America"] = "USA", ["NTSC-U"] = "USA", ["USA/NTSC"] = "USA",
        ["World"] = "World", ["W"] = "World",
        ["Japan"] = "Japan", ["JP"] = "Japan", ["J"] = "Japan", ["NTSC-J"] = "Japan",
        ["Europe"] = "Europe", ["EUR"] = "Europe", ["EU"] = "Europe", ["E"] = "Europe", ["PAL"] = "Europe", ["UK"] = "Europe", ["United Kingdom"] = "Europe",
        ["Australia"] = "Australia", ["AUS"] = "Australia",
        ["Korea"] = "Korea", ["KOR"] = "Korea",
        ["Asia"] = "Asia",
        ["Brazil"] = "Brazil", ["BRA"] = "Brazil",
        ["Canada"] = "Canada", ["CAN"] = "Canada",
        ["China"] = "China", ["CHN"] = "China",
        ["Taiwan"] = "Taiwan", ["Hong Kong"] = "Hong Kong",
        ["France"] = "France", ["FRA"] = "France",
        ["Germany"] = "Germany", ["GER"] = "Germany",
        ["Italy"] = "Italy", ["ITA"] = "Italy",
        ["Netherlands"] = "Netherlands", ["NED"] = "Netherlands",
        ["Spain"] = "Spain", ["ESP"] = "Spain",
        ["Sweden"] = "Sweden", ["SWE"] = "Sweden",
        ["Russia"] = "Russia", ["RUS"] = "Russia",
        ["Latin America"] = "Latin America", ["Scandinavia"] = "Scandinavia"
    };

    public static ParsedRomFilenameMetadata ParseFilename(
        string fileNameOrPath,
        IReadOnlyList<RetroProvenanceTagRule>? provenanceRules = null)
    {
        string fileName = Path.GetFileName(fileNameOrPath);
        string ext = GetCompoundExtension(fileName);
        string baseName = ext.Length > 0 && fileName.Length > ext.Length
            ? fileName[..^ext.Length]
            : Path.GetFileNameWithoutExtension(fileName);

        var meta = new ParsedRomFilenameMetadata { RawBaseName = baseName };

        string healedBase = HealMalformedTagSyntax(baseName, out bool wasHealed, out string? lintReason);
        if (wasHealed)
        {
            meta.HasMalformedTags = true;
            meta.LintReason = lintReason;
            meta.SuggestedFixedFileName = healedBase + ext;
        }

        string working = healedBase;
        var customLookup = (provenanceRules ?? Array.Empty<RetroProvenanceTagRule>())
            .ToDictionary(r => r.TagToken.Trim('(', ')', '[', ']'), r => r, StringComparer.OrdinalIgnoreCase);

        var tokensToStripFromDatName = new List<string>();
        var tokensToStripFromCleanTitle = new List<string>();

        foreach (Match m in ParenOrBracketRegex.Matches(working))
        {
            string fullToken = m.Value;
            bool isParen = fullToken.StartsWith('(');
            string inner = (isParen ? m.Groups[1].Value : m.Groups[2].Value).Trim();
            tokensToStripFromCleanTitle.Add(fullToken);

            if (customLookup.TryGetValue(inner, out var rule))
            {
                if (rule.StripForDatMatching)
                    tokensToStripFromDatName.Add(fullToken);

                if (string.Equals(rule.Code, "RH", StringComparison.OrdinalIgnoreCase))
                    meta.IsRomHackTagged = true;
                else if (string.Equals(rule.Code, "HB", StringComparison.OrdinalIgnoreCase))
                    meta.IsHomebrewTagged = true;
                else
                    meta.ExplicitProvenanceCode = PickHigherProvenanceCode(meta.ExplicitProvenanceCode, rule.Code, provenanceRules);
                continue;
            }

            if (inner.Equals("PR", StringComparison.OrdinalIgnoreCase) ||
                inner.Equals("Lic", StringComparison.OrdinalIgnoreCase) ||
                inner.Equals("PRt", StringComparison.OrdinalIgnoreCase))
            {
                meta.ExplicitProvenanceCode = PickHigherProvenanceCode(meta.ExplicitProvenanceCode, inner, provenanceRules);
                tokensToStripFromDatName.Add(fullToken);
                continue;
            }
            if (inner.Equals("RH", StringComparison.OrdinalIgnoreCase))
            {
                meta.IsRomHackTagged = true;
                tokensToStripFromDatName.Add(fullToken);
                continue;
            }
            if (inner.Equals("HB", StringComparison.OrdinalIgnoreCase))
            {
                meta.IsHomebrewTagged = true;
                tokensToStripFromDatName.Add(fullToken);
                continue;
            }
            if (SerialRegex.IsMatch(inner))
            {
                meta.Serial = inner.Trim().ToUpperInvariant().Replace("_", "-");
                tokensToStripFromDatName.Add(fullToken);
                continue;
            }

            var discMatch = DiscRegex.Match(inner);
            if (discMatch.Success && int.TryParse(discMatch.Groups[1].Value, out int discNo) && discNo >= 1)
            {
                meta.DiscNumber = discNo;
                meta.HasDiscMarker = true;
                continue;
            }
            if (TryParseAndNormalizeRegionTag(inner, out string normalizedRegion))
            {
                meta.Region = normalizedRegion;
                continue;
            }
            if (LanguageListRegex.IsMatch(inner))
            {
                meta.Languages = inner.Replace(" ", "");
                continue;
            }
            if (RevisionRegex.IsMatch(inner))
            {
                meta.Revision = inner;
                continue;
            }
        }

        string cleanTitle = working;
        foreach (string tok in tokensToStripFromCleanTitle)
            cleanTitle = cleanTitle.Replace(tok, " ");
        meta.CleanTitle = MultiSpaceRegex.Replace(cleanTitle, " ").Trim(' ', '-', '_');
        if (string.IsNullOrWhiteSpace(meta.CleanTitle))
            meta.CleanTitle = working.Trim();

        string datCorrelation = working;
        foreach (string tok in tokensToStripFromDatName)
            datCorrelation = datCorrelation.Replace(tok, " ");
        datCorrelation = Regex.Replace(datCorrelation, @"\(JP\)", "(Japan)", RegexOptions.IgnoreCase);
        meta.DatCorrelationName = MultiSpaceRegex.Replace(datCorrelation, " ").Trim();

        if (string.IsNullOrEmpty(meta.Languages) && string.Equals(meta.Region, "USA", StringComparison.OrdinalIgnoreCase))
            meta.Languages = "En";
        else if (string.IsNullOrEmpty(meta.Languages) && string.Equals(meta.Region, "Japan", StringComparison.OrdinalIgnoreCase))
            meta.Languages = "Ja";

        return meta;
    }

    public static string HealMalformedTagSyntax(string baseName, out bool wasModified, out string? reason)
    {
        wasModified = false;
        reason = null;
        string current = baseName;

        while (NestedBrokenParenRegex.IsMatch(current))
        {
            string next = NestedBrokenParenRegex.Replace(current, "($1) $2", 1);
            if (next == current) break;
            current = next;
            wasModified = true;
            reason = "Unclosed '(' before subsequent parenthetical tag";
        }

        int openParen = current.Count(c => c == '(');
        int closeParen = current.Count(c => c == ')');
        if (openParen > closeParen)
        {
            current += new string(')', openParen - closeParen);
            wasModified = true;
            reason ??= "Missing closing ')' parenthesis";
        }

        int openBracket = current.Count(c => c == '[');
        int closeBracket = current.Count(c => c == ']');
        if (openBracket > closeBracket)
        {
            current += new string(']', openBracket - closeBracket);
            wasModified = true;
            reason ??= "Missing closing ']' bracket";
        }

        string collapsed = MultiSpaceRegex.Replace(current, " ").Trim();
        if (collapsed != current)
        {
            current = collapsed;
            if (!wasModified && collapsed != baseName)
            {
                wasModified = true;
                reason = "Collapsed redundant whitespace";
            }
        }

        return current;
    }

    public static string GetCompoundExtension(string fileName)
    {
        if (fileName.EndsWith(".nkit.iso", StringComparison.OrdinalIgnoreCase))
            return ".nkit.iso";
        if (fileName.EndsWith(".manifest.tsv", StringComparison.OrdinalIgnoreCase))
            return ".manifest.tsv";
        var partMatch = Regex.Match(fileName, @"\.part\d+\.rar$", RegexOptions.IgnoreCase);
        if (partMatch.Success)
            return partMatch.Value;
        return Path.GetExtension(fileName);
    }

    public static int GetProvenancePriorityScore(string? tierCode, IReadOnlyList<RetroProvenanceTagRule>? customRules = null)
    {
        if (string.IsNullOrWhiteSpace(tierCode)) return 0;
        if (customRules != null && customRules.Count > 0)
        {
            var rule = customRules.FirstOrDefault(r => !r.IsRoleMarker && string.Equals(r.Code, tierCode, StringComparison.OrdinalIgnoreCase));
            if (rule != null && rule.PriorityRank > 0)
                return Math.Max(5, 110 - (rule.PriorityRank * 10));
        }

        return tierCode.Trim().ToUpperInvariant() switch
        {
            "PR" => 100,
            "LIC" => 90,
            "PRT" => 80,
            "UNTAGGED" => 40,
            "MASSSET" => 10,
            _ => 0
        };
    }

    public static bool ImpliesPersonalOwnership(string? tierCode, IReadOnlyList<RetroProvenanceTagRule>? customRules = null)
    {
        if (string.IsNullOrWhiteSpace(tierCode)) return false;
        if (customRules != null && customRules.Count > 0)
        {
            var rule = customRules.FirstOrDefault(r => !r.IsRoleMarker && string.Equals(r.Code, tierCode, StringComparison.OrdinalIgnoreCase));
            if (rule != null) return rule.ImpliesPersonalOwnership;
        }
        return tierCode.Equals("PR", StringComparison.OrdinalIgnoreCase) ||
               tierCode.Equals("Lic", StringComparison.OrdinalIgnoreCase) ||
               tierCode.Equals("PRt", StringComparison.OrdinalIgnoreCase);
    }

    public static string PickHigherProvenanceCode(string? a, string b, IReadOnlyList<RetroProvenanceTagRule>? customRules = null)
    {
        if (string.IsNullOrWhiteSpace(a)) return b;
        return GetProvenancePriorityScore(a, customRules) >= GetProvenancePriorityScore(b, customRules) ? a : b;
    }

    public static bool TryParseAndNormalizeRegionTag(string raw, out string normalizedRegion)
    {
        normalizedRegion = "";
        if (string.IsNullOrWhiteSpace(raw)) return false;
        string trimmed = raw.Trim();
        if (trimmed.Equals("UE", StringComparison.OrdinalIgnoreCase)) { normalizedRegion = "USA, Europe"; return true; }
        if (trimmed.Equals("JU", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("UJ", StringComparison.OrdinalIgnoreCase)) { normalizedRegion = "Japan, USA"; return true; }
        if (trimmed.Equals("JUE", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("UJE", StringComparison.OrdinalIgnoreCase)) { normalizedRegion = "Japan, USA, Europe"; return true; }

        if (!trimmed.Contains(','))
        {
            if (SingleRegionAliases.TryGetValue(trimmed, out string? single)) { normalizedRegion = single; return true; }
            return false;
        }

        string[] parts = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;
        var normalizedList = new List<string>(parts.Length);
        foreach (string p in parts)
        {
            if (!SingleRegionAliases.TryGetValue(p, out string? mapped)) return false;
            if (!normalizedList.Contains(mapped, StringComparer.OrdinalIgnoreCase)) normalizedList.Add(mapped);
        }
        normalizedRegion = string.Join(", ", normalizedList);
        return true;
    }

    public static IReadOnlyList<string> SplitNormalizedRegions(string? rawRegion)
    {
        if (string.IsNullOrWhiteSpace(rawRegion) || string.Equals(rawRegion, "Unknown", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<string>();
        if (TryParseAndNormalizeRegionTag(rawRegion, out string normalized))
            return normalized.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        return rawRegion
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tok => SingleRegionAliases.TryGetValue(tok, out string? mapped) ? mapped : tok)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string NormalizeTitleKey(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        var sb = new StringBuilder(title.Length);
        foreach (char c in title)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}
