// @module: Foundation.Retro.SaveStateFixtureRunner
// @purpose: Deterministic multi-scene save-state fixture runner capturing optional RGBA PNGs and PCM WAVs and verifying framebuffer/audio hashes.
// @depends-on: System, System.Collections.Generic, System.IO, System.Security.Cryptography, System.Threading, System.Threading.Tasks, Foundation.Core
// @exports: SaveStateFixtureFrameOutput, SaveStateFixtureResult, SaveStateFixtureRunner

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;

namespace Foundation.Retro;

public sealed record SaveStateFixtureFrameOutput(
    byte[] RgbaPixels,
    int Width,
    int Height,
    short[]? InterleavedAudioPcm = null,
    int AudioSampleRate = 44100,
    short AudioChannels = 2
);

public sealed record SaveStateFixtureResult(
    string FixtureId,
    string SceneName,
    bool Passed,
    int FramesExecuted,
    string? ActualFramebufferSha256,
    string? ActualAudioWavSha256,
    int MaxChannelDelta,
    string? ScreenshotPngPath,
    string? AudioWavPath,
    string DiagnosticMessage
);

/// <summary>
/// Executes deterministic multi-scene save-state fixture runs, optionally capturing final-frame PNGs
/// (RgbaPngWriter) and PCM audio (WavCaptureWriter) and asserting SHA-256 or per-channel pixel tolerances.
/// </summary>
public static class SaveStateFixtureRunner
{
    public static async Task<SaveStateFixtureResult> RunFixtureAsync(
        SaveStateFixtureSpec spec,
        Func<SaveStateFixtureSpec, int, CancellationToken, Task<SaveStateFixtureFrameOutput>> stepFrameAsync,
        string? artifactOutputDir = null,
        byte[]? referenceRgbaPixels = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(stepFrameAsync);
        if (spec.FrameCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(spec), "FrameCount must be positive.");

        string? outDir = !string.IsNullOrWhiteSpace(artifactOutputDir) ? Path.GetFullPath(artifactOutputDir) : null;
        if (outDir is not null)
        {
            Directory.CreateDirectory(outDir);
        }

        string? wavPath = null;
        WavCaptureWriter? wavWriter = null;
        SaveStateFixtureFrameOutput? lastFrame = null;
        int executed = 0;

        try
        {
            for (int frame = 0; frame < spec.FrameCount; frame++)
            {
                ct.ThrowIfCancellationRequested();
                lastFrame = await stepFrameAsync(spec, frame, ct).ConfigureAwait(false);
                executed++;

                if (lastFrame.InterleavedAudioPcm is { Length: > 0 } pcm)
                {
                    if (wavWriter is null)
                    {
                        wavPath = outDir is not null
                            ? Path.Combine(outDir, $"{SanitizeFileSegment(spec.FixtureId)}.wav")
                            : Path.Combine(Path.GetTempPath(), $"fixture_{Guid.NewGuid():N}.wav");
                        wavWriter = new WavCaptureWriter(wavPath, lastFrame.AudioSampleRate, lastFrame.AudioChannels);
                    }
                    wavWriter.WriteInterleavedSamples(pcm);
                }
            }
        }
        finally
        {
            wavWriter?.Dispose();
        }

        if (lastFrame is null || lastFrame.RgbaPixels.Length == 0)
        {
            CleanupTempWavIfNeeded(outDir, wavPath);
            return new SaveStateFixtureResult(spec.FixtureId, spec.SceneName, false, executed, null, null, int.MaxValue, null, null, "Fixture produced zero video frames.");
        }

        string actualFbSha = Convert.ToHexString(SHA256.HashData(lastFrame.RgbaPixels)).ToLowerInvariant();
        string? pngPath = null;
        if (outDir is not null)
        {
            pngPath = Path.Combine(outDir, $"{SanitizeFileSegment(spec.FixtureId)}.png");
            RgbaPngWriter.WritePng(pngPath, lastFrame.Width, lastFrame.Height, lastFrame.RgbaPixels);
        }

        string? actualWavSha = null;
        if (wavPath is not null && File.Exists(wavPath))
        {
            byte[] wavBytes = await File.ReadAllBytesAsync(wavPath, ct).ConfigureAwait(false);
            actualWavSha = Convert.ToHexString(SHA256.HashData(wavBytes)).ToLowerInvariant();
            if (outDir is null)
            {
                CleanupTempWavIfNeeded(outDir, wavPath);
                wavPath = null;
            }
        }

        int maxDelta = 0;
        if (referenceRgbaPixels is not null)
        {
            if (referenceRgbaPixels.Length != lastFrame.RgbaPixels.Length)
            {
                return new SaveStateFixtureResult(spec.FixtureId, spec.SceneName, false, executed, actualFbSha, actualWavSha, int.MaxValue, pngPath, wavPath, "Reference framebuffer byte length mismatch.");
            }

            for (int i = 0; i < referenceRgbaPixels.Length; i++)
            {
                int diff = Math.Abs(lastFrame.RgbaPixels[i] - referenceRgbaPixels[i]);
                if (diff > maxDelta) maxDelta = diff;
            }

            if (maxDelta > spec.AllowedPixelDelta)
            {
                return new SaveStateFixtureResult(spec.FixtureId, spec.SceneName, false, executed, actualFbSha, actualWavSha, maxDelta, pngPath, wavPath, $"Max pixel delta {maxDelta} exceeded tolerance {spec.AllowedPixelDelta}.");
            }
        }

        if (!string.IsNullOrWhiteSpace(spec.ExpectedFramebufferSha256) &&
            !string.Equals(actualFbSha, spec.ExpectedFramebufferSha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return new SaveStateFixtureResult(spec.FixtureId, spec.SceneName, false, executed, actualFbSha, actualWavSha, maxDelta, pngPath, wavPath, $"Framebuffer SHA-256 mismatch: expected {spec.ExpectedFramebufferSha256}, actual {actualFbSha}.");
        }

        if (!string.IsNullOrWhiteSpace(spec.ExpectedAudioWavSha256) &&
            !string.Equals(actualWavSha, spec.ExpectedAudioWavSha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return new SaveStateFixtureResult(spec.FixtureId, spec.SceneName, false, executed, actualFbSha, actualWavSha, maxDelta, pngPath, wavPath, $"Audio WAV SHA-256 mismatch: expected {spec.ExpectedAudioWavSha256}, actual {actualWavSha}.");
        }

        return new SaveStateFixtureResult(spec.FixtureId, spec.SceneName, true, executed, actualFbSha, actualWavSha, maxDelta, pngPath, wavPath, $"Fixture '{spec.FixtureId}' ({spec.SceneName}) passed ({executed} frames).");
    }

    public static async Task<IReadOnlyList<SaveStateFixtureResult>> RunAllFixturesAsync(
        IEnumerable<SaveStateFixtureSpec> specs,
        Func<SaveStateFixtureSpec, int, CancellationToken, Task<SaveStateFixtureFrameOutput>> stepFrameAsync,
        string? artifactOutputDir = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(specs);
        var results = new List<SaveStateFixtureResult>();
        foreach (SaveStateFixtureSpec spec in specs)
        {
            results.Add(await RunFixtureAsync(spec, stepFrameAsync, artifactOutputDir, null, ct).ConfigureAwait(false));
        }
        return results;
    }

    private static string SanitizeFileSegment(string input)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            input = input.Replace(invalid, '_');
        }
        return string.IsNullOrWhiteSpace(input) ? "fixture" : input;
    }

    private static void CleanupTempWavIfNeeded(string? outDir, string? wavPath)
    {
        if (outDir is null && wavPath is not null && File.Exists(wavPath))
        {
            try { File.Delete(wavPath); } catch { }
        }
    }
}
