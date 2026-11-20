// @module: Foundation.Core.Timing
// @purpose: High-resolution Stopwatch frame pacer with Windows timeBeginPeriod(1), sleep+spinwait pacing, VRR late-latch wait timing, and 120-frame rolling FPS/P99 telemetry.
// @depends-on: System, System.Diagnostics, System.Runtime.InteropServices, System.Threading, System.Threading.Tasks
// @exports: HighResFramePacer

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

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
    private readonly double[] _lateLatchWaitSamplesMs = new double[TelemetryWindowFrames];
    private readonly object _sync = new();
    private double _targetFps;
    private double _lateLatchMarginMs = 1.5;
    private double _lastLateLatchWaitMs;
    private long _lastFrameStartTicks;
    private long _nextDeadlineTicks;
    private long _lateLatchPresentDeadlineTicks;
    private long _totalFrames;
    private long _totalLateLatchWaits;
    private int _sampleCursor;
    private int _lateLatchCursor;
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

    public double LateLatchMarginMs
    {
        get => Volatile.Read(ref _lateLatchMarginMs);
        set
        {
            double normalized = double.IsNaN(value) || double.IsInfinity(value) ? 1.5 : Math.Clamp(value, 0.0, 10.0);
            Volatile.Write(ref _lateLatchMarginMs, normalized);
        }
    }

    public double LastLateLatchWaitMs => Volatile.Read(ref _lastLateLatchWaitMs);

    public double AverageLateLatchWaitMs
    {
        get
        {
            lock (_sync)
            {
                int count = (int)Math.Min(_totalLateLatchWaits, TelemetryWindowFrames);
                if (count == 0) return 0.0;
                double sum = 0.0;
                for (int i = 0; i < count; i++) sum += _lateLatchWaitSamplesMs[i];
                return sum / count;
            }
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
        _lateLatchPresentDeadlineTicks = now;
    }

    /// <summary>
    /// Marks the start of a frame and establishes the target presentation deadline for VRR late-latch input sampling.
    /// </summary>
    public long BeginLateLatchFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        long now = Stopwatch.GetTimestamp();
        double fps = TargetFps;
        if (fps > 0.0)
        {
            long frameIntervalTicks = (long)Math.Max(1.0, Stopwatch.Frequency / fps);
            _lateLatchPresentDeadlineTicks = _lateLatchPresentDeadlineTicks == 0
                ? now + frameIntervalTicks
                : _lateLatchPresentDeadlineTicks + frameIntervalTicks;
            if (now - _lateLatchPresentDeadlineTicks > frameIntervalTicks * 2 || _lateLatchPresentDeadlineTicks < now)
            {
                _lateLatchPresentDeadlineTicks = now + frameIntervalTicks;
            }
        }
        else
        {
            _lateLatchPresentDeadlineTicks = now;
        }
        return now;
    }

    /// <summary>
    /// Sleeps/spinwaits until <c>marginMs</c> before the active frame's present deadline so late-latch input stays fresh with minimal VRR jitter.
    /// </summary>
    public double WaitBeforePresent(double? lateLatchMarginMs = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (TargetFps <= 0.0)
        {
            RecordLateLatchTelemetry(0.0);
            return 0.0;
        }

        long waitStart = Stopwatch.GetTimestamp();
        long targetTicks = ComputeLateLatchWaitDeadlineTicks(lateLatchMarginMs);
        while (true)
        {
            long remainingTicks = targetTicks - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0) break;

            double remainingMs = remainingTicks * 1000.0 / Stopwatch.Frequency;
            if (remainingMs > 2.2) Thread.Sleep(1);
            else Thread.SpinWait(16);
        }

        double waitedMs = Math.Max(0.0, (Stopwatch.GetTimestamp() - waitStart) * 1000.0 / Stopwatch.Frequency);
        RecordLateLatchTelemetry(waitedMs);
        return waitedMs;
    }

    /// <summary>
    /// Asynchronously awaits until <c>marginMs</c> before the active frame's present deadline for VRR late-latch pacing.
    /// </summary>
    public async Task<double> WaitBeforePresentAsync(double? lateLatchMarginMs = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (TargetFps <= 0.0)
        {
            RecordLateLatchTelemetry(0.0);
            return 0.0;
        }

        long waitStart = Stopwatch.GetTimestamp();
        long targetTicks = ComputeLateLatchWaitDeadlineTicks(lateLatchMarginMs);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long remainingTicks = targetTicks - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0) break;

            double remainingMs = remainingTicks * 1000.0 / Stopwatch.Frequency;
            if (remainingMs > 2.5) await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            else Thread.SpinWait(16);
        }

        double waitedMs = Math.Max(0.0, (Stopwatch.GetTimestamp() - waitStart) * 1000.0 / Stopwatch.Frequency);
        RecordLateLatchTelemetry(waitedMs);
        return waitedMs;
    }

    private long ComputeLateLatchWaitDeadlineTicks(double? lateLatchMarginMs)
    {
        double marginMs = lateLatchMarginMs.HasValue
            ? Math.Clamp(double.IsNaN(lateLatchMarginMs.Value) || double.IsInfinity(lateLatchMarginMs.Value) ? 0.0 : lateLatchMarginMs.Value, 0.0, 10.0)
            : LateLatchMarginMs;
        long marginTicks = (long)(marginMs * Stopwatch.Frequency / 1000.0);
        return _lateLatchPresentDeadlineTicks - marginTicks;
    }

    private void RecordLateLatchTelemetry(double waitedMs)
    {
        Volatile.Write(ref _lastLateLatchWaitMs, waitedMs);
        lock (_sync)
        {
            _lateLatchWaitSamplesMs[_lateLatchCursor] = waitedMs;
            _lateLatchCursor = (_lateLatchCursor + 1) % TelemetryWindowFrames;
            Interlocked.Increment(ref _totalLateLatchWaits);
        }
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
