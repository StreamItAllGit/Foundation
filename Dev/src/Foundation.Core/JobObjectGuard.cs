// @module: Foundation.Core.ProcessSupervision
// @purpose: Supervises child processes via Windows Job Objects and coordinates Dual-Token (StopCurrentJob vs StopAllJobs) cancellation.
// @depends-on: System, System.Diagnostics, System.Runtime.InteropServices, System.Threading
// @exports: JobObjectGuard, DualJobCancellationCoordinator

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Foundation.Core;

/// <summary>
/// Coordinates dual-scope job cancellation per Standard 09 §4.5 / SIS-05:
/// - StopCurrentJob cancels ONLY the active job while allowing queued jobs to proceed.
/// - StopAllJobs cancels the active job AND signals the queue to drain/abort all pending jobs.
/// </summary>
public sealed class DualJobCancellationCoordinator : IDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource _allJobsCts = new();
    private CancellationTokenSource? _currentJobCts;
    private CancellationTokenSource? _linkedCurrentCts;
    private bool _disposed;

    public string? ActiveJobId { get; private set; }
    public bool IsAllJobsStopRequested { get; private set; }

    public CancellationToken BeginJobScope(string jobId, CancellationToken hostShutdownToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        lock (_sync)
        {
            DisposeCurrentJobTokensUnsynchronized();
            if (_allJobsCts.IsCancellationRequested)
            {
                _allJobsCts.Dispose();
                _allJobsCts = new CancellationTokenSource();
            }

            IsAllJobsStopRequested = false;
            ActiveJobId = jobId;
            _currentJobCts = new CancellationTokenSource();
            _linkedCurrentCts = hostShutdownToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(_currentJobCts.Token, _allJobsCts.Token, hostShutdownToken)
                : CancellationTokenSource.CreateLinkedTokenSource(_currentJobCts.Token, _allJobsCts.Token);

            return _linkedCurrentCts.Token;
        }
    }

    /// <summary>
    /// Cancels only the currently executing job so the next queued job can still run.
    /// </summary>
    public bool StopCurrentJob()
    {
        lock (_sync)
        {
            if (_currentJobCts is null || _currentJobCts.IsCancellationRequested)
            {
                return false;
            }
            _currentJobCts.Cancel();
            return true;
        }
    }

    /// <summary>
    /// Cancels the currently executing job AND signals the job queue to clear all pending jobs.
    /// </summary>
    public void StopAllJobs()
    {
        lock (_sync)
        {
            IsAllJobsStopRequested = true;
            _currentJobCts?.Cancel();
            _allJobsCts.Cancel();
        }
    }

    public void EndJobScope(string jobId)
    {
        lock (_sync)
        {
            if (string.Equals(ActiveJobId, jobId, StringComparison.Ordinal))
            {
                ActiveJobId = null;
                DisposeCurrentJobTokensUnsynchronized();
            }
        }
    }

    private void DisposeCurrentJobTokensUnsynchronized()
    {
        try { _linkedCurrentCts?.Dispose(); } catch { /* ignore */ }
        try { _currentJobCts?.Dispose(); } catch { /* ignore */ }
        _linkedCurrentCts = null;
        _currentJobCts = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        lock (_sync)
        {
            DisposeCurrentJobTokensUnsynchronized();
            try { _allJobsCts.Dispose(); } catch { /* ignore */ }
        }
    }
}

/// <summary>
/// Wraps a Windows Job Object configured with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE so child tools (7z, chdman, rclone)
/// never survive if the parent process crashes or terminates.
/// </summary>
public sealed class JobObjectGuard : IDisposable
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int infoType, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private IntPtr _jobHandle;
    private bool _disposed;

    public bool IsSupported => OperatingSystem.IsWindows() && _jobHandle != IntPtr.Zero;

    public JobObjectGuard(string? jobName = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        _jobHandle = CreateJobObject(IntPtr.Zero, jobName);
        if (_jobHandle == IntPtr.Zero)
        {
            return;
        }

        var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        extendedInfo.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        int length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        SetInformationJobObject(_jobHandle, JobObjectExtendedLimitInformation, ref extendedInfo, (uint)length);
    }

    public bool TryAttachProcess(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (_disposed || !IsSupported)
        {
            return false;
        }

        try
        {
            return AssignProcessToJobObject(_jobHandle, process.Handle);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_jobHandle != IntPtr.Zero)
        {
            CloseHandle(_jobHandle);
            _jobHandle = IntPtr.Zero;
        }
    }
}
