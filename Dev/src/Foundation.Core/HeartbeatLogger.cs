// @module: Foundation.Core.Telemetry
// @purpose: Enforces the <= 15s progress heartbeat invariant and TTY-aware in-place \r\x1b[K terminal status line updates.
// @depends-on: System, System.Diagnostics, System.IO
// @exports: HeartbeatSnapshot, HeartbeatLogger

using System;
using System.Diagnostics;
using System.IO;

namespace Foundation.Core;

public sealed record HeartbeatSnapshot(
    string OperationName,
    long CompletedUnits,
    long TotalUnits,
    double PercentComplete,
    double ElapsedSeconds,
    string CurrentItem,
    string FormattedLine
);

/// <summary>
/// Emits structured [HEARTBEAT] telemetry at least once every 15 seconds for long-running operations
/// and renders in-place \r\x1b[K terminal status lines when attached to an interactive console (Standard 13).
/// </summary>
public sealed class HeartbeatLogger
{
    public const int MaxHeartbeatIntervalSeconds = 15;

    private readonly string _operationName;
    private readonly Action<string>? _logSink;
    private readonly TextWriter? _terminalWriter;
    private readonly bool _enableInPlaceAnsi;
    private readonly TimeSpan _heartbeatInterval;
    private readonly Stopwatch _operationClock;
    private long _lastHeartbeatTickMs;
    private readonly object _sync = new();

    public int HeartbeatEmitCount { get; private set; }
    public HeartbeatSnapshot? LastSnapshot { get; private set; }

    public HeartbeatLogger(
        string operationName,
        Action<string>? logSink = null,
        TextWriter? terminalWriter = null,
        bool? enableInPlaceAnsi = null,
        TimeSpan? heartbeatInterval = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        _operationName = operationName.Trim();
        _logSink = logSink;
        _terminalWriter = terminalWriter;
        _enableInPlaceAnsi = enableInPlaceAnsi ?? (!Console.IsOutputRedirected);
        TimeSpan requested = heartbeatInterval ?? TimeSpan.FromSeconds(MaxHeartbeatIntervalSeconds);
        _heartbeatInterval = requested > TimeSpan.FromSeconds(MaxHeartbeatIntervalSeconds)
            ? TimeSpan.FromSeconds(MaxHeartbeatIntervalSeconds)
            : (requested <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : requested);
        _operationClock = Stopwatch.StartNew();
        _lastHeartbeatTickMs = -999_999;
    }

    public HeartbeatSnapshot ReportProgress(
        long completedUnits,
        long totalUnits,
        string? currentItem = null,
        bool forceEmit = false)
    {
        lock (_sync)
        {
            double pct = totalUnits > 0
                ? Math.Clamp((double)completedUnits / totalUnits * 100.0, 0.0, 100.0)
                : 0.0;
            double elapsedSec = _operationClock.Elapsed.TotalSeconds;
            string safeItem = string.IsNullOrWhiteSpace(currentItem) ? "working" : currentItem.Trim();
            string formatted = $"[HEARTBEAT] {_operationName}: {completedUnits}/{totalUnits} ({pct:F1}%) | {elapsedSec:F1}s | {safeItem}";

            var snapshot = new HeartbeatSnapshot(
                _operationName,
                completedUnits,
                totalUnits,
                pct,
                elapsedSec,
                safeItem,
                formatted);
            LastSnapshot = snapshot;

            if (_terminalWriter is not null)
            {
                if (_enableInPlaceAnsi)
                {
                    _terminalWriter.Write($"\r\x1b[K{formatted}");
                }
                else if (forceEmit)
                {
                    _terminalWriter.WriteLine(formatted);
                }
                _terminalWriter.Flush();
            }

            long nowMs = _operationClock.ElapsedMilliseconds;
            if (forceEmit || (nowMs - _lastHeartbeatTickMs) >= _heartbeatInterval.TotalMilliseconds)
            {
                _lastHeartbeatTickMs = nowMs;
                HeartbeatEmitCount++;
                _logSink?.Invoke(formatted);
            }

            return snapshot;
        }
    }

    public void Complete(long totalUnits, string summaryMessage = "Completed")
    {
        lock (_sync)
        {
            ReportProgress(totalUnits, totalUnits, summaryMessage, forceEmit: true);
            if (_terminalWriter is not null && _enableInPlaceAnsi)
            {
                _terminalWriter.WriteLine();
                _terminalWriter.Flush();
            }
        }
    }
}
