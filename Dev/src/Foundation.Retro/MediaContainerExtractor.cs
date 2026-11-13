// @module: Foundation.Retro.MediaContainerExtractor
// @purpose: Fail-closed BYO optical disc and archive extractor (.iso, .bin+.cue, .chd, .7z, .zip) with ToolResolver discovery and SHA-256 verification.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.IO.Compression, System.Security.Cryptography, System.Text.RegularExpressions, System.Threading, System.Threading.Tasks, Foundation.Core
// @exports: MediaContainerFormat, MediaExtractionOptions, MediaExtractionResult, MediaContainerExtractor

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;

namespace Foundation.Retro;

public enum MediaContainerFormat { Unknown, Iso, BinCue, Chd, SevenZip, Zip }

public sealed record MediaExtractionOptions(
    string? ChdmanOverridePath = null,
    string? SevenZipOverridePath = null,
    string? ToolsBaseDirectory = null,
    bool CopyDirectMediaToDestination = false,
    bool ComputeSha256 = true,
    IReadOnlyList<DiscChecksumEntry>? ExpectedChecksums = null);

public sealed record MediaExtractionResult(
    bool Success,
    MediaContainerFormat Format,
    string PrimaryMediaPath,
    string? CueSheetPath,
    IReadOnlyList<string> ExtractedFiles,
    IReadOnlyDictionary<string, string> Sha256ByRelativePath,
    string DiagnosticMessage,
    bool IsToolMissingFallback = false);

/// <summary>
/// Extracts and verifies BYO optical disc images and archives (.iso, .bin+.cue, .chd, .7z, .zip)
/// using fail-closed path containment and ToolResolver 4-tier external binary discovery.
/// </summary>
public static partial class MediaContainerExtractor
{
    [GeneratedRegex(@"^\s*FILE\s+(?:""(?<quoted>[^""]+)""|(?<bare>\S+))", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CueFileLineRegex();

    public static MediaContainerFormat DetectFormat(string path) =>
        Path.GetExtension(path ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            ".iso" => MediaContainerFormat.Iso,
            ".cue" or ".bin" => MediaContainerFormat.BinCue,
            ".chd" => MediaContainerFormat.Chd,
            ".7z" => MediaContainerFormat.SevenZip,
            ".zip" => MediaContainerFormat.Zip,
            _ => MediaContainerFormat.Unknown
        };

    public static async Task<MediaExtractionResult> ExtractOrVerifyAsync(
        string sourceMediaPath, string destinationDirectory, MediaExtractionOptions? options = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceMediaPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        options ??= new MediaExtractionOptions();

        string fullSource = Path.GetFullPath(sourceMediaPath);
        string fullDest = Path.GetFullPath(destinationDirectory);
        MediaContainerFormat format = DetectFormat(fullSource);

        if (!File.Exists(fullSource))
            return Failure(format, $"Source media file was not found: '{fullSource}'.");
        if (format == MediaContainerFormat.Unknown)
            return Failure(format, $"Unsupported media container extension '{Path.GetExtension(fullSource)}'.");

        return format switch
        {
            MediaContainerFormat.Iso => await HandleDirectMediaAsync(MediaContainerFormat.Iso, new[] { fullSource }, null, Path.GetDirectoryName(fullSource)!, fullDest, options, $"Verified ISO image '{Path.GetFileName(fullSource)}'.", ct).ConfigureAwait(false),
            MediaContainerFormat.BinCue => await HandleBinCueAsync(fullSource, fullDest, options, ct).ConfigureAwait(false),
            MediaContainerFormat.Zip => await HandleZipAsync(fullSource, fullDest, options, ct).ConfigureAwait(false),
            MediaContainerFormat.SevenZip => await HandleSevenZipAsync(fullSource, fullDest, options, ct).ConfigureAwait(false),
            MediaContainerFormat.Chd => await HandleChdAsync(fullSource, fullDest, options, ct).ConfigureAwait(false),
            _ => Failure(format, "Unsupported media container format.")
        };
    }

    private static async Task<MediaExtractionResult> HandleBinCueAsync(
        string sourcePath, string destDir, MediaExtractionOptions options, CancellationToken ct)
    {
        string srcDir = Path.GetDirectoryName(sourcePath)!;
        string? cuePath = null;
        var files = new List<string>();

        if (sourcePath.EndsWith(".cue", StringComparison.OrdinalIgnoreCase))
        {
            cuePath = sourcePath;
            files.Add(cuePath);
            foreach (string trackRel in ParseCueReferencedFiles(cuePath))
            {
                string trackFull = SandboxEnvironment.AssertPathContained(Path.Combine(srcDir, trackRel), srcDir);
                if (!File.Exists(trackFull))
                    return Failure(MediaContainerFormat.BinCue, $"CUE sheet references missing track file '{trackRel}'.");
                if (!files.Contains(trackFull, StringComparer.OrdinalIgnoreCase)) files.Add(trackFull);
            }
        }
        else
        {
            files.Add(sourcePath);
            string siblingCue = Path.ChangeExtension(sourcePath, ".cue");
            if (File.Exists(siblingCue))
            {
                cuePath = siblingCue;
                files.Add(siblingCue);
            }
        }

        return await HandleDirectMediaAsync(MediaContainerFormat.BinCue, files, cuePath, srcDir, destDir, options, $"Verified BIN/CUE container ({files.Count} file(s)).", ct).ConfigureAwait(false);
    }

    private static async Task<MediaExtractionResult> HandleDirectMediaAsync(
        MediaContainerFormat format, IReadOnlyList<string> sourceFiles, string? cuePath, string srcDir, string destDir,
        MediaExtractionOptions options, string okMessage, CancellationToken ct)
    {
        var activeFiles = new List<string>(sourceFiles);
        string rootForRel = srcDir;
        string? activeCue = cuePath;

        if (options.CopyDirectMediaToDestination)
        {
            Directory.CreateDirectory(destDir);
            activeFiles.Clear();
            foreach (string file in sourceFiles)
            {
                string target = SandboxEnvironment.AssertPathContained(Path.Combine(destDir, Path.GetRelativePath(srcDir, file)), destDir);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (!string.Equals(file, target, StringComparison.OrdinalIgnoreCase))
                    await CopyFileAsync(file, target, ct).ConfigureAwait(false);
                activeFiles.Add(target);
            }
            if (cuePath is not null)
                activeCue = SandboxEnvironment.AssertPathContained(Path.Combine(destDir, Path.GetRelativePath(srcDir, cuePath)), destDir);
            rootForRel = destDir;
        }

        string primary = SelectPrimaryMedia(activeFiles, activeCue);
        return await FinalizeResultAsync(format, primary, activeCue, activeFiles, rootForRel, options, okMessage, ct).ConfigureAwait(false);
    }

    private static async Task<MediaExtractionResult> HandleZipAsync(
        string sourceZip, string destDir, MediaExtractionOptions options, CancellationToken ct)
    {
        Directory.CreateDirectory(destDir);
        var extracted = new List<string>();

        using (ZipArchive archive = ZipFile.OpenRead(sourceZip))
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                string rel = entry.FullName.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                if (string.IsNullOrWhiteSpace(rel)) continue;

                string destPath = SandboxEnvironment.AssertPathContained(Path.Combine(destDir, rel), destDir);
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                await using Stream srcStream = entry.Open();
                await using var dstStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await srcStream.CopyToAsync(dstStream, ct).ConfigureAwait(false);
                extracted.Add(destPath);
            }
        }

        if (extracted.Count == 0)
            return Failure(MediaContainerFormat.Zip, "ZIP container did not contain any extractable files.");

        string? cue = extracted.FirstOrDefault(f => f.EndsWith(".cue", StringComparison.OrdinalIgnoreCase));
        return await FinalizeResultAsync(MediaContainerFormat.Zip, SelectPrimaryMedia(extracted, cue), cue, extracted, destDir, options, $"Extracted {extracted.Count} file(s) from ZIP.", ct).ConfigureAwait(false);
    }

    private static async Task<MediaExtractionResult> HandleSevenZipAsync(
        string source7z, string destDir, MediaExtractionOptions options, CancellationToken ct)
    {
        ToolResolutionResult tool = ToolResolver.ResolveTool(
            "7z", new[] { "7z.exe", "7za.exe", "7z", "7za" }, options.SevenZipOverridePath, options.ToolsBaseDirectory,
            new[] { @"C:\Program Files\7-Zip", @"C:\Program Files (x86)\7-Zip" });
        if (!tool.IsAvailable || string.IsNullOrWhiteSpace(tool.ResolvedPath))
            return MissingTool(MediaContainerFormat.SevenZip, tool.DiagnosticMessage);

        Directory.CreateDirectory(destDir);
        var (exitCode, stderr) = await RunExternalToolAsync(tool.ResolvedPath, new[] { "x", "-y", $"-o{destDir}", source7z }, destDir, ct).ConfigureAwait(false);
        if (exitCode != 0)
            return Failure(MediaContainerFormat.SevenZip, $"7z extraction failed (exit {exitCode}): {stderr}");

        var extracted = Directory.GetFiles(destDir, "*", SearchOption.AllDirectories)
            .Select(f => SandboxEnvironment.AssertPathContained(f, destDir)).ToList();
        string? cue = extracted.FirstOrDefault(f => f.EndsWith(".cue", StringComparison.OrdinalIgnoreCase));
        return await FinalizeResultAsync(MediaContainerFormat.SevenZip, SelectPrimaryMedia(extracted, cue), cue, extracted, destDir, options, $"Extracted {extracted.Count} file(s) via 7z.", ct).ConfigureAwait(false);
    }

    private static async Task<MediaExtractionResult> HandleChdAsync(
        string sourceChd, string destDir, MediaExtractionOptions options, CancellationToken ct)
    {
        ToolResolutionResult tool = ToolResolver.ResolveTool(
            "chdman", new[] { "chdman.exe", "chdman" }, options.ChdmanOverridePath, options.ToolsBaseDirectory,
            new[] { @"C:\Program Files\MAME", @"C:\MAME", @"C:\Program Files\RetroArch", @"C:\RetroArch" });
        if (!tool.IsAvailable || string.IsNullOrWhiteSpace(tool.ResolvedPath))
            return MissingTool(MediaContainerFormat.Chd, tool.DiagnosticMessage);

        Directory.CreateDirectory(destDir);
        string stem = Path.GetFileNameWithoutExtension(sourceChd);
        string outCue = SandboxEnvironment.AssertPathContained(Path.Combine(destDir, stem + ".cue"), destDir);
        string outBin = SandboxEnvironment.AssertPathContained(Path.Combine(destDir, stem + ".bin"), destDir);

        var (exitCode, stderr) = await RunExternalToolAsync(tool.ResolvedPath, new[] { "extractcd", "-f", "-i", sourceChd, "-o", outCue, "-ob", outBin }, destDir, ct).ConfigureAwait(false);
        if (exitCode != 0 || !File.Exists(outCue))
            return Failure(MediaContainerFormat.Chd, $"chdman extractcd failed (exit {exitCode}): {stderr}");

        var extracted = new List<string> { outCue };
        if (File.Exists(outBin)) extracted.Add(outBin);
        return await FinalizeResultAsync(MediaContainerFormat.Chd, SelectPrimaryMedia(extracted, outCue), outCue, extracted, destDir, options, $"Extracted CHD via chdman into '{stem}.cue/.bin'.", ct).ConfigureAwait(false);
    }

    private static async Task<MediaExtractionResult> FinalizeResultAsync(
        MediaContainerFormat format, string primaryPath, string? cuePath, IReadOnlyList<string> files,
        string relativeBaseDir, MediaExtractionOptions options, string successMessage, CancellationToken ct)
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (options.ComputeSha256 || options.ExpectedChecksums is { Count: > 0 })
        {
            foreach (string file in files)
            {
                string rel = Path.GetRelativePath(relativeBaseDir, file).Replace('\\', '/');
                hashes[rel] = await ComputeFileSha256Async(file, ct).ConfigureAwait(false);
            }
        }

        if (options.ExpectedChecksums is { Count: > 0 })
        {
            foreach (DiscChecksumEntry expected in options.ExpectedChecksums)
            {
                string normRel = expected.RelativePath.Replace('\\', '/').TrimStart('/');
                if (!hashes.TryGetValue(normRel, out string? actualSha))
                {
                    if (expected.IsRequired)
                        return new MediaExtractionResult(false, format, primaryPath, cuePath, files, hashes, $"Missing required disc image file '{normRel}'.");
                    continue;
                }
                if (!string.Equals(actualSha, expected.ExpectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                    return new MediaExtractionResult(false, format, primaryPath, cuePath, files, hashes, $"SHA-256 mismatch for '{normRel}': expected {expected.ExpectedSha256}, actual {actualSha}.");

                if (expected.ExpectedSizeBytes.HasValue)
                {
                    string matchedFile = files.First(f => string.Equals(Path.GetRelativePath(relativeBaseDir, f).Replace('\\', '/'), normRel, StringComparison.OrdinalIgnoreCase));
                    long actualLen = new FileInfo(matchedFile).Length;
                    if (actualLen != expected.ExpectedSizeBytes.Value)
                        return new MediaExtractionResult(false, format, primaryPath, cuePath, files, hashes, $"Size mismatch for '{normRel}': expected {expected.ExpectedSizeBytes.Value}, actual {actualLen}.");
                }
            }
        }

        return new MediaExtractionResult(true, format, primaryPath, cuePath, files, hashes, successMessage);
    }

    private static string SelectPrimaryMedia(IReadOnlyList<string> files, string? cuePath) =>
        files.FirstOrDefault(f => f.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) ??
        files.FirstOrDefault(f => f.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)) ??
        cuePath ?? files.FirstOrDefault() ?? string.Empty;

    private static IReadOnlyList<string> ParseCueReferencedFiles(string cuePath)
    {
        var list = new List<string>();
        foreach (string line in File.ReadLines(cuePath))
        {
            Match m = CueFileLineRegex().Match(line);
            if (!m.Success) continue;
            string name = m.Groups["quoted"].Success ? m.Groups["quoted"].Value : m.Groups["bare"].Value;
            if (!string.IsNullOrWhiteSpace(name)) list.Add(name.Trim());
        }
        return list;
    }

    private static async Task CopyFileAsync(string src, string dst, CancellationToken ct)
    {
        await using var s = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var d = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None);
        await s.CopyToAsync(d, ct).ConfigureAwait(false);
    }

    private static async Task<string> ComputeFileSha256Async(string filePath, CancellationToken ct)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false)).ToLowerInvariant();
    }

    private static async Task<(int ExitCode, string StdErr)> RunExternalToolAsync(string exePath, IReadOnlyList<string> args, string workingDir, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exePath) { WorkingDirectory = workingDir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string a in args) psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to launch '{exePath}'.");
        Task<string> errTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        return (proc.ExitCode, (await errTask.ConfigureAwait(false)).Trim());
    }

    private static MediaExtractionResult Failure(MediaContainerFormat format, string message) =>
        new(false, format, string.Empty, null, Array.Empty<string>(), new Dictionary<string, string>(), message);

    private static MediaExtractionResult MissingTool(MediaContainerFormat format, string diagnostic) =>
        new(false, format, string.Empty, null, Array.Empty<string>(), new Dictionary<string, string>(), diagnostic, IsToolMissingFallback: true);
}
