// @module: Foundation.Core.Audio
// @purpose: Lock-free SPSC ring buffer for interleaved short/float PCM audio with smooth ramp-to-zero underrun attenuation.
// @depends-on: System, System.Threading
// @exports: AudioRingBuffer

using System;
using System.Threading;

namespace Foundation.Core;

/// <summary>
/// Lock-free Single-Producer Single-Consumer (SPSC) audio ring buffer for interleaved PCM samples.
/// On underrun, smoothly ramps remaining output samples toward zero to eliminate audible clicks/pops.
/// </summary>
public sealed class AudioRingBuffer
{
    private const int DefaultUnderrunRampFrames = 32;

    private readonly float[] _buffer;
    private readonly float[] _lastChannelSamples;
    private long _writeCursor;
    private long _readCursor;
    private long _underrunCount;

    public int CapacitySamples { get; }
    public int Channels { get; }

    public int BufferedSamples
    {
        get
        {
            long w = Volatile.Read(ref _writeCursor);
            long r = Volatile.Read(ref _readCursor);
            return (int)Math.Clamp(w - r, 0, CapacitySamples);
        }
    }

    public int AvailableWriteSamples => CapacitySamples - BufferedSamples;

    public double FillRatio => CapacitySamples > 0 ? (double)BufferedSamples / CapacitySamples : 0.0;

    public long UnderrunCount => Volatile.Read(ref _underrunCount);

    public AudioRingBuffer(int capacitySamples, int channels = 2)
    {
        if (capacitySamples <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacitySamples), "Capacity must be positive.");
        if (channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(channels), "Channels must be positive.");

        CapacitySamples = capacitySamples;
        Channels = channels;
        _buffer = new float[capacitySamples];
        _lastChannelSamples = new float[channels];
    }

    public void Clear()
    {
        long w = Volatile.Read(ref _writeCursor);
        Volatile.Write(ref _readCursor, w);
        Array.Clear(_lastChannelSamples);
    }

    public int Write(ReadOnlySpan<float> interleavedSamples)
    {
        if (interleavedSamples.IsEmpty) return 0;

        long readPos = Volatile.Read(ref _readCursor);
        long writePos = Volatile.Read(ref _writeCursor);
        int free = (int)Math.Max(0, CapacitySamples - (writePos - readPos));
        int toWrite = Math.Min(interleavedSamples.Length, free);

        for (int i = 0; i < toWrite; i++)
        {
            _buffer[(int)((writePos + i) % CapacitySamples)] = Math.Clamp(interleavedSamples[i], -1.0f, 1.0f);
        }

        Volatile.Write(ref _writeCursor, writePos + toWrite);
        return toWrite;
    }

    public int Write(ReadOnlySpan<short> interleavedSamples)
    {
        if (interleavedSamples.IsEmpty) return 0;

        long readPos = Volatile.Read(ref _readCursor);
        long writePos = Volatile.Read(ref _writeCursor);
        int free = (int)Math.Max(0, CapacitySamples - (writePos - readPos));
        int toWrite = Math.Min(interleavedSamples.Length, free);

        for (int i = 0; i < toWrite; i++)
        {
            _buffer[(int)((writePos + i) % CapacitySamples)] = interleavedSamples[i] / 32768.0f;
        }

        Volatile.Write(ref _writeCursor, writePos + toWrite);
        return toWrite;
    }

    public int Read(Span<float> destination)
    {
        if (destination.IsEmpty) return 0;

        long writePos = Volatile.Read(ref _writeCursor);
        long readPos = Volatile.Read(ref _readCursor);
        int available = (int)Math.Clamp(writePos - readPos, 0, destination.Length);

        for (int i = 0; i < available; i++)
        {
            float sample = _buffer[(int)((readPos + i) % CapacitySamples)];
            destination[i] = sample;
            _lastChannelSamples[i % Channels] = sample;
        }

        if (available > 0)
        {
            Volatile.Write(ref _readCursor, readPos + available);
        }

        if (available < destination.Length)
        {
            Interlocked.Increment(ref _underrunCount);
            FillUnderrunRamp(destination[available..]);
        }

        return available;
    }

    public int Read(Span<short> destination)
    {
        if (destination.IsEmpty) return 0;

        long writePos = Volatile.Read(ref _writeCursor);
        long readPos = Volatile.Read(ref _readCursor);
        int available = (int)Math.Clamp(writePos - readPos, 0, destination.Length);

        for (int i = 0; i < available; i++)
        {
            float sample = _buffer[(int)((readPos + i) % CapacitySamples)];
            _lastChannelSamples[i % Channels] = sample;
            destination[i] = FloatToInt16(sample);
        }

        if (available > 0)
        {
            Volatile.Write(ref _readCursor, readPos + available);
        }

        if (available < destination.Length)
        {
            Interlocked.Increment(ref _underrunCount);
            FillUnderrunRamp(destination[available..]);
        }

        return available;
    }

    private void FillUnderrunRamp(Span<float> tail)
    {
        int totalFrames = Math.Max(1, (tail.Length + Channels - 1) / Channels);
        int rampFrames = Math.Min(totalFrames, DefaultUnderrunRampFrames);

        for (int i = 0; i < tail.Length; i++)
        {
            int frameIdx = i / Channels;
            int ch = i % Channels;
            if (frameIdx < rampFrames)
            {
                float gain = 1.0f - ((float)(frameIdx + 1) / rampFrames);
                float ramped = _lastChannelSamples[ch] * gain;
                tail[i] = ramped;
                if (frameIdx == rampFrames - 1)
                {
                    _lastChannelSamples[ch] = 0.0f;
                }
            }
            else
            {
                tail[i] = 0.0f;
                _lastChannelSamples[ch] = 0.0f;
            }
        }

        if (rampFrames == 0)
        {
            Array.Clear(_lastChannelSamples);
        }
    }

    private void FillUnderrunRamp(Span<short> tail)
    {
        int totalFrames = Math.Max(1, (tail.Length + Channels - 1) / Channels);
        int rampFrames = Math.Min(totalFrames, DefaultUnderrunRampFrames);

        for (int i = 0; i < tail.Length; i++)
        {
            int frameIdx = i / Channels;
            int ch = i % Channels;
            if (frameIdx < rampFrames)
            {
                float gain = 1.0f - ((float)(frameIdx + 1) / rampFrames);
                float ramped = _lastChannelSamples[ch] * gain;
                tail[i] = FloatToInt16(ramped);
                if (frameIdx == rampFrames - 1)
                {
                    _lastChannelSamples[ch] = 0.0f;
                }
            }
            else
            {
                tail[i] = 0;
                _lastChannelSamples[ch] = 0.0f;
            }
        }
    }

    private static short FloatToInt16(float sample)
    {
        float clamped = Math.Clamp(sample, -1.0f, 1.0f);
        int scaled = (int)MathF.Round(clamped * 32767.0f);
        return (short)Math.Clamp(scaled, short.MinValue, short.MaxValue);
    }
}
