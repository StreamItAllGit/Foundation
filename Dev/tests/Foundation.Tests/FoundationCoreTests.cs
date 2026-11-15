// @module: Foundation.Tests.Core
// @purpose: Unit tests for AppEnvironment, PidSafeLockfile, SqliteWalPool, CrashBundleService, HeartbeatLogger, HybridModuleLoader, and DualJobCancellationCoordinator.
// @depends-on: System, System.Collections.Generic, System.IO, System.Threading, System.Threading.Tasks, Foundation.Core, Foundation.Core.Contracts, Xunit
// @exports: SampleFolderSliceModule, FoundationCoreTests

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;
using Foundation.Core.Contracts;
using Xunit;

namespace Foundation.Tests;

public sealed class SampleFolderSliceModule : IFoundationModule
{
    public string ModuleId => "sample-slice";
    public string DisplayName => "Sample Folder Slice";
    public ModuleType ModuleType => ModuleType.FolderSlice;
    public string Category => "Verification";
    public string IconName => "shield-check";
    public IReadOnlyList<SchemaSettingDescriptor> SettingsSchema => new[]
    {
        new SchemaSettingDescriptor("sample.enabled", "Enable sample slice", "boolean", true, "Sample Slice", "Enables the sample test module.")
    };

    public Task<int> ExecuteCliAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default)
        => Task.FromResult(0);
}

public sealed class FoundationCoreTests : IDisposable
{
    private readonly string _tempDir;

    public FoundationCoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FoundationTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void AppEnvironment_ResolvesFlagsAndEnforcesFailClosedContainment()
    {
        string dataDir = Path.Combine(_tempDir, "isolated_data");
        var env = new AppEnvironment("shadowlibrary", new[]
        {
            "--data-dir", dataDir,
            "--port=5179",
            "--suite-tab"
        });

        Assert.Equal("shadowlibrary", env.AppId);
        Assert.Equal(Path.GetFullPath(dataDir), env.DataDirectory);
        Assert.Equal(5179, env.PortOverride);
        Assert.Equal(UiPresentationMode.SuiteTab, env.PresentationMode);

        string validChild = Path.Combine(dataDir, "vaults", "nes");
        Assert.Equal(Path.GetFullPath(validChild), AppEnvironment.AssertPathContained(validChild, dataDir));

        string escapedPath = Path.Combine(_tempDir, "outside_folder");
        Assert.Throws<InvalidOperationException>(() => AppEnvironment.AssertPathContained(escapedPath, dataDir));
    }

    [Fact]
    public void PidSafeLockfile_BlocksLiveProcessAndBreaksDeadOrphanLocks()
    {
        string lockPath = Path.Combine(_tempDir, "vault.hklock");

        using (PidSafeLockfile activeLock = PidSafeLockfile.Acquire(lockPath, "shadowlibrary", "bitrot-scrub"))
        {
            Assert.True(PidSafeLockfile.IsLockedByLiveProcess(lockPath, out PidSafeLockEnvelope? holder));
            Assert.NotNull(holder);
            Assert.Equal(Environment.ProcessId, holder!.Pid);

            bool secondAttempt = PidSafeLockfile.TryAcquire(lockPath, "housekeeping", "scan", out PidSafeLockfile? secondHandle, out _);
            Assert.False(secondAttempt);
            Assert.Null(secondHandle);
        }

        Assert.False(File.Exists(lockPath));

        // Write an orphan lockfile referencing a non-existent PID
        var deadEnvelope = new PidSafeLockEnvelope
        {
            SchemaVersion = 1,
            AppId = "housekeeping",
            Pid = 999999,
            ProcessStartTimeUtc = "2020-01-01T00:00:00.000Z",
            HostName = Environment.MachineName,
            Operation = "crashed-scan",
            AcquiredAtUtc = DateTime.UtcNow.AddMinutes(-5).ToString("O"),
            HeartbeatAtUtc = DateTime.UtcNow.AddMinutes(-5).ToString("O"),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10).ToString("O")
        };
        File.WriteAllText(lockPath, JsonSerializer.Serialize(deadEnvelope), Encoding.UTF8);

        Assert.False(PidSafeLockfile.IsLockedByLiveProcess(lockPath, out _));

        using PidSafeLockfile recovered = PidSafeLockfile.Acquire(lockPath, "shadowlibrary", "recovery-scan");
        Assert.Equal(Environment.ProcessId, recovered.Envelope.Pid);
    }

    [Fact]
    public async Task SqliteWalPool_EnforcesWalPragmasAndRunsAtomicVacuumInto()
    {
        string dbPath = Path.Combine(_tempDir, "test.db");
        var pool = new SqliteWalPool(dbPath);

        await using (var conn = await pool.OpenConnectionAsync())
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE items (id INTEGER PRIMARY KEY, title TEXT NOT NULL); INSERT INTO items (title) VALUES ('Chrono Trigger');";
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = "PRAGMA journal_mode;";
            string? mode = (await cmd.ExecuteScalarAsync())?.ToString();
            Assert.Equal("wal", mode?.ToLowerInvariant());

            cmd.CommandText = "PRAGMA cache_size;";
            long cacheSize = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            Assert.Equal(-65536L, cacheSize);
        }

        string backupPath = Path.Combine(_tempDir, "backups", "test_backup.db");
        string finalBackup = await pool.VacuumIntoAsync(backupPath);
        Assert.True(File.Exists(finalBackup));
    }

    [Fact]
    public async Task SqliteWalPool_UsesDefaultCacheAndSupportsConcurrentReadersDuringWriteTransaction()
    {
        string dbPath = Path.Combine(_tempDir, "concurrent_wal.db");
        var pool = new SqliteWalPool(dbPath, busyTimeoutMs: 15000);
        Assert.Contains("Cache=Default", pool.ConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cache=Shared", pool.ConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(15000, pool.BusyTimeoutMs);

        await using (var setupConn = await pool.OpenConnectionAsync())
        await using (var setupCmd = setupConn.CreateCommand())
        {
            setupCmd.CommandText = "CREATE TABLE entry_files (id INTEGER PRIMARY KEY, path TEXT NOT NULL); INSERT INTO entry_files (path) VALUES ('/rom/1.7z');";
            await setupCmd.ExecuteNonQueryAsync();
        }

        // Open a writer transaction and run concurrent readers while the writer holds an active transaction on entry_files
        await using var writerConn = await pool.OpenConnectionAsync();
        await using var tx = writerConn.BeginTransaction();
        await using (var writeCmd = writerConn.CreateCommand())
        {
            writeCmd.Transaction = tx;
            writeCmd.CommandText = "DELETE FROM entry_files WHERE id = 1; INSERT INTO entry_files (id, path) VALUES (1, '/rom/1_updated.7z'), (2, '/rom/2.7z');";
            await writeCmd.ExecuteNonQueryAsync();
        }

        // In Cache=Shared, a concurrent reader on entry_files would throw SQLite Error 6 (SQLITE_LOCKED: database table is locked: entry_files).
        // With Cache=Default + WAL mode, readers read the pre-transaction WAL snapshot without blocking or throwing Error 6.
        var readerTasks = new List<Task<long>>();
        for (int i = 0; i < 6; i++)
        {
            readerTasks.Add(Task.Run(async () =>
            {
                await using var readerConn = await pool.OpenConnectionAsync();
                await using var readCmd = readerConn.CreateCommand();
                readCmd.CommandText = "SELECT COUNT(*) FROM entry_files;";
                return Convert.ToInt64(await readCmd.ExecuteScalarAsync());
            }));
        }

        long[] snapshotCounts = await Task.WhenAll(readerTasks);
        Assert.All(snapshotCounts, count => Assert.Equal(1L, count));

        await tx.CommitAsync();

        await using var postCommitConn = await pool.OpenConnectionAsync();
        await using var postCmd = postCommitConn.CreateCommand();
        postCmd.CommandText = "SELECT COUNT(*) FROM entry_files;";
        Assert.Equal(2L, Convert.ToInt64(await postCmd.ExecuteScalarAsync()));
    }

    [Fact]
    public void HybridModuleLoader_And_DualJobCancellationCoordinator_WorkAsExpected()
    {
        var loader = new HybridModuleLoader();
        int count = loader.DiscoverFromAssemblies(new[] { typeof(FoundationCoreTests).Assembly });
        Assert.True(count >= 1);
        Assert.NotNull(loader.TryGetModule("sample-slice"));
        Assert.Single(loader.GetAggregatedSettingsSchema());

        using var coordinator = new DualJobCancellationCoordinator();
        CancellationToken job1Token = coordinator.BeginJobScope("job-1");
        Assert.False(job1Token.IsCancellationRequested);
        Assert.True(coordinator.StopCurrentJob());
        Assert.True(job1Token.IsCancellationRequested);
        Assert.False(coordinator.IsAllJobsStopRequested);

        CancellationToken job2Token = coordinator.BeginJobScope("job-2");
        Assert.False(job2Token.IsCancellationRequested);
        coordinator.StopAllJobs();
        Assert.True(job2Token.IsCancellationRequested);
        Assert.True(coordinator.IsAllJobsStopRequested);
    }

    [Fact]
    public void CrashBundleService_And_HeartbeatLogger_ProduceExpectedOutputs()
    {
        string crashPath = CrashBundleService.WriteCrashBundle(
            _tempDir,
            "shadowlibrary",
            "1.0.0-rc.14",
            new InvalidOperationException("Simulated fault"),
            new[] { "Line 1", "Line 2" });
        Assert.True(File.Exists(crashPath));

        var emitted = new List<string>();
        using var sw = new StringWriter();
        var hb = new HeartbeatLogger("BitRotScrub", msg => emitted.Add(msg), sw, enableInPlaceAnsi: true);
        HeartbeatSnapshot snap = hb.ReportProgress(5, 10, "Super Metroid.7z", forceEmit: true);
        Assert.Equal(50.0, snap.PercentComplete);
        Assert.Single(emitted);
        Assert.Contains("\r\x1b[K[HEARTBEAT] BitRotScrub", sw.ToString());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore locked SQLite WAL handles during temp folder cleanup
        }
    }
}
