// @module: Foundation.Sidecars.HashSidecarService
// @purpose: Standard 17 .hash v1 sidecar parser, serializer, wrapper-path normalizer, COMPOSITE_SHA1 calculator, and archive CRC32 index validator.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Security.Cryptography, System.Text, Foundation.Core
// @exports: SidecarDocument, HashSidecarService

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Foundation.Core;

namespace Foundation.Sidecars;

public sealed class SidecarDocument
{
    public string Title { get; set; } = "";
    public bool IsCompositeFolder { get; set; }
    public string CompositeSha1 { get; set; } = "";
    public int TotalFilesHeader { get; set; }
    public long TotalSizeHeader { get; set; }
    public string ContainerFileName { get; set; } = "";
    public long ContainerSize { get; set; }
    public string ContainerCrc32 { get; set; } = "";
    public string ContainerMd5 { get; set; } = "";
    public string ContainerSha1 { get; set; } = "";
    public bool IsLegacyOuterArchiveHash { get; set; }
    public List<(string RelativePath, long Size, string Crc32, string Md5, string Sha1)> Files { get; set; } = new();
}

public static class HashSidecarService
{
    public static readonly IReadOnlySet<string> ReservedConsoleWrapperFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "PS3_GAME", "sce_sys", "code", "content", "meta", "romfs", "exefs", "BDMV", "VIDEO_TS"
    };

    public static string GetHashSidecarPath(string targetFileOrFolderPath) =>
        AppEnvironment.NormalizeLongPath(targetFileOrFolderPath + ".hash");

    public static SidecarDocument? ParseHashSidecarLines(IEnumerable<string> rawLines, string outerName)
    {
        if (rawLines == null) return null;
        try
        {
            var doc = new SidecarDocument();
            bool outerIsArchive = SevenZipAndZipInspector.IsStandardArchiveExtension(Path.GetExtension(outerName));

            foreach (string rawLine in rawLines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (line.StartsWith("# Title:", StringComparison.OrdinalIgnoreCase))
                    doc.Title = line["# Title:".Length..].Trim();
                else if (line.StartsWith("# Folder:", StringComparison.OrdinalIgnoreCase))
                {
                    doc.Title = line["# Folder:".Length..].Trim();
                    doc.IsCompositeFolder = true;
                }
                else if (line.StartsWith("# TotalFiles:", StringComparison.OrdinalIgnoreCase) &&
                         int.TryParse(line["# TotalFiles:".Length..].Trim(), out int tfComment) && doc.TotalFilesHeader <= 0)
                    doc.TotalFilesHeader = tfComment;
                else if (line.StartsWith("TOTAL_FILES:", StringComparison.OrdinalIgnoreCase) &&
                         int.TryParse(line["TOTAL_FILES:".Length..].Trim(), out int tfHeader))
                {
                    doc.TotalFilesHeader = tfHeader;
                    doc.IsCompositeFolder = true;
                }
                else if (line.StartsWith("TOTAL_SIZE:", StringComparison.OrdinalIgnoreCase) &&
                         long.TryParse(line["TOTAL_SIZE:".Length..].Trim(), out long tsHeader))
                {
                    doc.TotalSizeHeader = tsHeader;
                    doc.IsCompositeFolder = true;
                }
                else if (line.StartsWith("# Container:", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string seg in line.Split('|', StringSplitOptions.TrimEntries))
                    {
                        if (seg.StartsWith("# Container:", StringComparison.OrdinalIgnoreCase))
                            doc.ContainerFileName = seg["# Container:".Length..].Trim();
                        else if (seg.StartsWith("SIZE:", StringComparison.OrdinalIgnoreCase) && long.TryParse(seg["SIZE:".Length..].Trim(), out long csz))
                            doc.ContainerSize = csz;
                        else if (seg.StartsWith("CRC32:", StringComparison.OrdinalIgnoreCase))
                            doc.ContainerCrc32 = seg["CRC32:".Length..].Trim().ToUpperInvariant();
                        else if (seg.StartsWith("MD5:", StringComparison.OrdinalIgnoreCase))
                            doc.ContainerMd5 = seg["MD5:".Length..].Trim().ToUpperInvariant();
                        else if (seg.StartsWith("SHA1:", StringComparison.OrdinalIgnoreCase))
                            doc.ContainerSha1 = seg["SHA1:".Length..].Trim().ToUpperInvariant();
                    }
                }
                else if (line.StartsWith("COMPOSITE_SHA1:", StringComparison.OrdinalIgnoreCase))
                {
                    doc.IsCompositeFolder = true;
                    doc.CompositeSha1 = line["COMPOSITE_SHA1:".Length..].Trim().ToUpperInvariant();
                }
                else if (line.StartsWith("FILE:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("ENTRY:", StringComparison.OrdinalIgnoreCase))
                {
                    if (line.StartsWith("ENTRY:", StringComparison.OrdinalIgnoreCase)) doc.IsCompositeFolder = true;
                    string rel = "", crc = "", md5 = "", sha1 = "";
                    long sz = 0;

                    foreach (string seg in line.Split('|', StringSplitOptions.TrimEntries))
                    {
                        if (seg.StartsWith("FILE:", StringComparison.OrdinalIgnoreCase)) rel = seg["FILE:".Length..].Trim();
                        else if (seg.StartsWith("ENTRY:", StringComparison.OrdinalIgnoreCase)) rel = seg["ENTRY:".Length..].Trim();
                        else if (seg.StartsWith("SIZE:", StringComparison.OrdinalIgnoreCase)) long.TryParse(seg["SIZE:".Length..].Trim(), out sz);
                        else if (seg.StartsWith("CRC32:", StringComparison.OrdinalIgnoreCase)) crc = seg["CRC32:".Length..].Trim().ToUpperInvariant();
                        else if (seg.StartsWith("MD5:", StringComparison.OrdinalIgnoreCase)) md5 = seg["MD5:".Length..].Trim().ToUpperInvariant();
                        else if (seg.StartsWith("SHA1:", StringComparison.OrdinalIgnoreCase)) sha1 = seg["SHA1:".Length..].Trim().ToUpperInvariant();
                    }
                    if (!string.IsNullOrEmpty(rel)) doc.Files.Add((rel, sz, crc, md5, sha1));
                }
            }

            if (outerIsArchive && !doc.IsCompositeFolder && doc.Files.Count > 0 &&
                doc.Files.Any(f =>
                {
                    string recordedName = Path.GetFileName(f.RelativePath);
                    return string.Equals(recordedName, outerName, StringComparison.OrdinalIgnoreCase) ||
                           SevenZipAndZipInspector.IsStandardArchiveExtension(Path.GetExtension(recordedName));
                }))
            {
                doc.IsLegacyOuterArchiveHash = true;
                if (string.IsNullOrEmpty(doc.ContainerSha1) && doc.Files.Count == 1)
                {
                    doc.ContainerFileName = doc.Files[0].RelativePath;
                    doc.ContainerSize = doc.Files[0].Size;
                    doc.ContainerCrc32 = doc.Files[0].Crc32;
                    doc.ContainerMd5 = doc.Files[0].Md5;
                    doc.ContainerSha1 = doc.Files[0].Sha1;
                }
                doc.Files.Clear();
            }

            return (doc.Files.Count > 0 || !string.IsNullOrEmpty(doc.CompositeSha1) || !string.IsNullOrEmpty(doc.ContainerSha1)) ? doc : null;
        }
        catch { return null; }
    }

    public static bool HasValidContainerHash(SidecarDocument? sidecar, long expectedContainerSizeBytes) =>
        sidecar != null && expectedContainerSizeBytes > 0 &&
        sidecar.ContainerSize == expectedContainerSizeBytes &&
        !string.IsNullOrWhiteSpace(sidecar.ContainerCrc32) &&
        !string.IsNullOrWhiteSpace(sidecar.ContainerSha1);

    public static bool IsSidecarValidForSingleOrArchive(SidecarDocument? sidecar, long expectedSizeBytes, string? expectedCrc32 = null)
    {
        if (sidecar == null || sidecar.IsLegacyOuterArchiveHash || sidecar.IsCompositeFolder || sidecar.Files.Count == 0)
            return false;

        var primary = sidecar.Files
            .OrderByDescending(f => !f.RelativePath.EndsWith(".cue", StringComparison.OrdinalIgnoreCase) &&
                                    !f.RelativePath.EndsWith(".gdi", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(f => f.Size)
            .First();

        if (primary.Size != expectedSizeBytes || string.IsNullOrWhiteSpace(primary.Sha1) || string.IsNullOrWhiteSpace(primary.Crc32))
            return false;

        return string.IsNullOrWhiteSpace(expectedCrc32) ||
               string.Equals(primary.Crc32, expectedCrc32, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSidecarValidForMultiFileSet(
        SidecarDocument? sidecar,
        IReadOnlyList<(string RelativePath, long Size, string Crc32, string Md5, string Sha1)> expectedFiles)
    {
        if (sidecar == null || sidecar.IsLegacyOuterArchiveHash || sidecar.IsCompositeFolder ||
            expectedFiles == null || expectedFiles.Count == 0)
            return false;

        var normalizedExpected = NormalizeRelativePathsStrippingSingleWrapper(expectedFiles);
        var normalizedSidecar = NormalizeRelativePathsStrippingSingleWrapper(sidecar.Files);
        if (normalizedSidecar.Count != normalizedExpected.Count) return false;

        foreach (var exp in normalizedExpected)
        {
            if (string.IsNullOrWhiteSpace(exp.Crc32) || string.IsNullOrWhiteSpace(exp.Sha1)) return false;
            var match = normalizedSidecar.FirstOrDefault(f =>
                string.Equals(f.RelativePath.Replace('\\', '/'), exp.RelativePath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(match.RelativePath) || match.Size != exp.Size ||
                !string.Equals(match.Crc32, exp.Crc32, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(match.Sha1, exp.Sha1, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    public static bool IsSidecarValidForCompositeFolder(SidecarDocument? sidecar, int expectedFileCount, long expectedTotalSizeBytes)
    {
        if (sidecar == null || !sidecar.IsCompositeFolder || string.IsNullOrWhiteSpace(sidecar.CompositeSha1)) return false;
        if (sidecar.Files.Count != expectedFileCount) return false;
        if (sidecar.TotalFilesHeader > 0 && sidecar.TotalFilesHeader != expectedFileCount) return false;
        if (sidecar.TotalSizeHeader > 0 && sidecar.TotalSizeHeader != expectedTotalSizeBytes) return false;
        return sidecar.Files.Sum(f => f.Size) == expectedTotalSizeBytes &&
               sidecar.Files.All(f => !string.IsNullOrWhiteSpace(f.Crc32));
    }

    public static List<(string RelativePath, long Size, string Crc32, string Md5, string Sha1)> NormalizeRelativePathsStrippingSingleWrapper(
        IReadOnlyList<(string RelativePath, long Size, string Crc32, string Md5, string Sha1)> files)
    {
        if (files == null || files.Count == 0) return new();
        var normalized = files.Select(f => (RelativePath: f.RelativePath.Replace('\\', '/').TrimStart('/'), f.Size, f.Crc32, f.Md5, f.Sha1)).ToList();

        string? sharedWrapper = null;
        foreach (var f in normalized)
        {
            int slashIdx = f.RelativePath.IndexOf('/');
            if (slashIdx <= 0 || slashIdx == f.RelativePath.Length - 1) return normalized;
            string seg0 = f.RelativePath[..slashIdx];
            if (ReservedConsoleWrapperFolders.Contains(seg0) ||
                seg0.StartsWith("Disc ", StringComparison.OrdinalIgnoreCase) ||
                seg0.StartsWith("Disk ", StringComparison.OrdinalIgnoreCase) ||
                seg0.StartsWith("CD", StringComparison.OrdinalIgnoreCase))
                return normalized;

            if (sharedWrapper == null) sharedWrapper = seg0;
            else if (!string.Equals(sharedWrapper, seg0, StringComparison.OrdinalIgnoreCase)) return normalized;
        }

        if (string.IsNullOrEmpty(sharedWrapper)) return normalized;
        int prefixLen = sharedWrapper.Length + 1;
        return normalized.Select(f => (f.RelativePath[prefixLen..], f.Size, f.Crc32, f.Md5, f.Sha1)).ToList();
    }

    public static string ComputeCompositeFolderSha1(IEnumerable<(string RelativePath, long Size, string Crc32)> entries)
    {
        var sb = new StringBuilder();
        foreach (var e in entries.OrderBy(x => x.RelativePath.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase))
            sb.Append(e.RelativePath.Replace('\\', '/')).Append('|').Append(e.Size).Append('|').Append(e.Crc32.ToUpperInvariant()).Append('\n');
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    public static string FormatHashSidecarContent(
        string title,
        IReadOnlyList<(string RelativePath, long Size, string Crc32, string Md5, string Sha1)> files,
        string? compositeSha1 = null,
        (string FileName, long Size, string Crc32, string Md5, string Sha1)? container = null)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(compositeSha1))
        {
            long totalSize = files.Sum(f => f.Size);
            sb.Append("# Housekeeping Folder Composite Checksum\n# SchemaVersion: 1\n");
            sb.Append($"# Folder: {title}\n# TotalFiles: {files.Count}\n# Generated: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\n\n");
            sb.Append($"TOTAL_FILES: {files.Count}\nTOTAL_SIZE: {totalSize}\nCOMPOSITE_SHA1: {compositeSha1.ToUpperInvariant()}\n");
            foreach (var f in files.OrderBy(x => x.RelativePath.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase))
                sb.Append($"ENTRY: {f.RelativePath.Replace('\\', '/')} | SIZE: {f.Size} | CRC32: {f.Crc32.ToUpperInvariant()}\n");
        }
        else
        {
            var normalized = files.Count > 1 ? NormalizeRelativePathsStrippingSingleWrapper(files) : files.ToList();
            sb.Append("# Housekeeping Checksum Sidecar\n# SchemaVersion: 1\n");
            sb.Append($"# Title: {title}\n");
            if (normalized.Count > 1) sb.Append($"# TotalFiles: {normalized.Count}\n");
            if (container.HasValue && !string.IsNullOrWhiteSpace(container.Value.Sha1))
            {
                var c = container.Value;
                sb.Append($"# Container: {c.FileName} | SIZE: {c.Size} | CRC32: {c.Crc32.ToUpperInvariant()} | MD5: {c.Md5.ToUpperInvariant()} | SHA1: {c.Sha1.ToUpperInvariant()}\n");
            }
            sb.Append($"# Generated: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\n\n");
            foreach (var f in normalized)
                sb.Append($"FILE: {f.RelativePath.Replace('\\', '/')} | SIZE: {f.Size} | CRC32: {f.Crc32.ToUpperInvariant()} | MD5: {f.Md5.ToUpperInvariant()} | SHA1: {f.Sha1.ToUpperInvariant()}\n");
        }
        return sb.ToString();
    }

    public static bool IsCompositeArchiveEntries(IReadOnlyList<InspectedArchiveEntry>? entries)
    {
        if (entries == null || entries.Count == 0) return false;
        return entries.Any(e =>
        {
            string p = e.Path.Replace('\\', '/');
            return p.Contains("PS3_GAME/", StringComparison.OrdinalIgnoreCase) ||
                   p.EndsWith("PS3_GAME", StringComparison.OrdinalIgnoreCase) ||
                   p.Contains("sce_sys/param.sfo", StringComparison.OrdinalIgnoreCase) ||
                   p.Contains("sce_sys/param.json", StringComparison.OrdinalIgnoreCase) ||
                   p.Contains("meta/meta.xml", StringComparison.OrdinalIgnoreCase) ||
                   p.EndsWith("/default.xex", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(p, "default.xex", StringComparison.OrdinalIgnoreCase) ||
                   p.EndsWith("/default.xbe", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(p, "default.xbe", StringComparison.OrdinalIgnoreCase);
        });
    }
}
