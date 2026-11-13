// @module: Foundation.Core.Timing
// @purpose: High-resolution Stopwatch frame pacer with Windows timeBeginPeriod(1), sleep+spinwait pacing, and 120-frame rolling FPS/P99 telemetry.
// @depends-on: System, System.Diagnostics, System.Runtime.InteropServices, System.Threading
// @exports: HighResFramePacer

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Foundation.Core;

/// <summary>
/// High-resolution frame pacer supporting locked FPS targets or unlocked pacing (targetFps &lt;= 0)
/// with rolling 120-frame FPS, average frame time, and P99 frame time telemetry.
/// </summary>
public sealed class HighResFramePacer : IDisposable
{
    public const int TelemetryWindowFrames = 120;

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint timeEndPeriod(uint uPeriod);

    private readonly double[] _frameSamplesMs = new double[TelemetryWindowFrames];
    private readonly object _sync = new();
    private double _targetFps;
    private long _lastFrameStartTicks;
    private long _nextDeadlineTicks;
    private long _totalFrames;
    private int _sampleCursor;
    private bool _timerPeriodAcquired;
    private bool _disposed;

    public double TargetFps
    {
        get => Volatile.Read(ref _targetFps);
        set
        {
            double normalized = double.IsNaN(value) || double.IsInfinity(value) || value <= 0.0 ? 0.0 : value;
            Volatile.Write(ref _targetFps, normalized);
        }
    }

    public bool IsHighResolutionTimerActive => _timerPeriodAcquired && !_disposed;

    public long TotalFrames => Interlocked.Read(ref _totalFrames);

    public double AverageFrameMs
    {
        get
        {
            lock (_sync)
            {
                int count = (int)Math.Min(_totalFrames, TelemetryWindowFrames);
                if (count == 0) return 0.0;
                double sum = 0.0;
                for (int i = 0; i < count; i++) sum += _frameSamplesMs[i];
                return sum / count;
            }
        }
    }

    public double CurrentFps
    {
        get
        {
            double avgMs = AverageFrameMs;
            return avgMs > 0.0001 ? 1000.0 / avgMs : 0.0;
        }
    }

    public double P99FrameMs
    {
        get
        {
            lock (_sync)
            {
                int count = (int)Math.Min(_totalFrames, TelemetryWindowFrames);
                if (count == 0) return 0.0;
                Span<double> sorted = stackalloc double[count];
                _frameSamplesMs.AsSpan(0, count).CopyTo(sorted);
                sorted.Sort();
                int index = Math.Clamp((int)Math.Ceiling(0.99 * count) - 1, 0, count - 1);
                return sorted[index];
            }
        }
    }

    public HighResFramePacer(double targetFps = 60.0)
    {
        TargetFps = targetFps;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                _timerPeriodAcquired = timeBeginPeriod(1) == 0;
            }
            catch (DllNotFoundException)
            {
                _timerPeriodAcquired = false;
            }
            catch (EntryPointNotFoundException)
            {
                _timerPeriodAcquired = false;
            }
        }

        long now = Stopwatch.GetTimestamp();
        _lastFrameStartTicks = now;
        _nextDeadlineTicks = now;
    }

    public void Reset()
    {
        long now = Stopwatch.GetTimestamp();
        _lastFrameStartTicks = now;
        _nextDeadlineTicks = now;
    }

    /// <summary>
    /// Waits until the next target frame deadline (unless TargetFps &lt;= 0) and records frame timing telemetry.
    /// </summary>
    public double WaitForNextFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        double fps = TargetFps;
        if (fps > 0.0)
        {
            long frameIntervalTicks = (long)Math.Max(1.0, Stopwatch.Frequency / fps);
            _nextDeadlineTicks += frameIntervalTicks;

            long now = Stopwatch.GetTimestamp();
            if (now - _nextDeadlineTicks > frameIntervalTicks * 2)
            {
                _nextDeadlineTicks = now;
            }

            while (true)
            {
                now = Stopwatch.GetTimestamp();
                long remainingTicks = _nextDeadlineTicks - now;
                if (remainingTicks <= 0)
                {
                    break;
                }

                double remainingMs = remainingTicks * 1000.0 / Stopwatch.Frequency;
                if (remainingMs > 2.2)
                {
                    Thread.Sleep(1);
                }
                else
                {
                    Thread.SpinWait(16);
                }
            }
        }
        else
        {
            _nextDeadlineTicks = Stopwatch.GetTimestamp();
        }

        long frameEnd = Stopwatch.GetTimestamp();
        double elapsedMs = Math.Max(0.0, (frameEnd - _lastFrameStartTicks) * 1000.0 / Stopwatch.Frequency);
        _lastFrameStartTicks = frameEnd;

        lock (_sync)
        {
            _frameSamplesMs[_sampleCursor] = elapsedMs;
            _sampleCursor = (_sampleCursor + 1) % TelemetryWindowFrames;
            Interlocked.Increment(ref _totalFrames);
        }

        return elapsedMs;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_timerPeriodAcquired && OperatingSystem.IsWindows())
        {
            try
            {
                timeEndPeriod(1);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            _timerPeriodAcquired = false;
        }
    }
}
