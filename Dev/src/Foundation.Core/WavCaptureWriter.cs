// @module: Foundation.Core.Audio
// @purpose: Deterministic 16-bit PCM RIFF/WAV file capture writer that patches RIFF and data chunk sizes cleanly on Dispose.
// @depends-on: System, System.Buffers.Binary, System.IO, System.Text
// @exports: WavCaptureWriter

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Foundation.Core;

/// <summary>
/// Writes deterministic uncompressed 16-bit PCM RIFF/WAV audio captures and patches RIFF/data byte lengths on Dispose.
/// </summary>
public sealed class WavCaptureWriter : IDisposable
{
    private const int HeaderSizeBytes = 44;
    private readonly FileStream _stream;
    private bool _disposed;

    public string OutputPath { get; }
    public int SampleRate { get; }
    public short Channels { get; }
    public long TotalSamplesWritten { get; private set; }
    public uint DataBytesWritten { get; private set; }

    public WavCaptureWriter(string outputPath, int sampleRate = 44100, short channels = 2)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));

        OutputPath = Path.GetFullPath(outputPath);
        SampleRate = sampleRate;
        Channels = channels;

        string? parent = Path.GetDirectoryName(OutputPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        _stream = new FileStream(OutputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        WriteHeaderPlaceholder();
    }

    private void WriteHeaderPlaceholder()
    {
        Span<byte> header = stackalloc byte[HeaderSizeBytes];
        Encoding.ASCII.GetBytes("RIFF", header[..4]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..8], 36u);
        Encoding.ASCII.GetBytes("WAVE", header[8..12]);
        Encoding.ASCII.GetBytes("fmt ", header[12..16]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..20], 16u);
        BinaryPrimitives.WriteUInt16LittleEndian(header[20..22], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header[22..24], (ushort)Channels);
        BinaryPrimitives.WriteUInt32LittleEndian(header[24..28], (uint)SampleRate);

        short blockAlign = (short)(Channels * 2);
        int byteRate = SampleRate * blockAlign;
        BinaryPrimitives.WriteUInt32LittleEndian(header[28..32], (uint)byteRate);
        BinaryPrimitives.WriteUInt16LittleEndian(header[32..34], (ushort)blockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(header[34..36], 16);
        Encoding.ASCII.GetBytes("data", header[36..40]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[40..44], 0u);

        _stream.Write(header);
    }

    public void WriteInterleavedSamples(ReadOnlySpan<short> samples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (samples.IsEmpty) return;

        Span<byte> scratch = samples.Length <= 512
            ? stackalloc byte[samples.Length * 2]
            : new byte[samples.Length * 2];

        for (int i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(scratch[(i * 2)..], samples[i]);
        }

        _stream.Write(scratch);
        TotalSamplesWritten += samples.Length;
        DataBytesWritten = checked(DataBytesWritten + (uint)(samples.Length * 2));
    }

    public void WriteInterleavedSamples(ReadOnlySpan<float> samples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (samples.IsEmpty) return;

        short[] pcm = new short[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            float clamped = Math.Clamp(samples[i], -1.0f, 1.0f);
            pcm[i] = (short)Math.Clamp((int)MathF.Round(clamped * 32767.0f), short.MinValue, short.MaxValue);
        }

        WriteInterleavedSamples(pcm);
    }

    public void Flush()
    {
        if (!_disposed)
        {
            _stream.Flush();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            Span<byte> dword = stackalloc byte[4];
            _stream.Seek(4, SeekOrigin.Begin);
            BinaryPrimitives.WriteUInt32LittleEndian(dword, 36u + DataBytesWritten);
            _stream.Write(dword);

            _stream.Seek(40, SeekOrigin.Begin);
            BinaryPrimitives.WriteUInt32LittleEndian(dword, DataBytesWritten);
            _stream.Write(dword);

            _stream.Flush();
        }
        finally
        {
            _stream.Dispose();
        }
    }
}
