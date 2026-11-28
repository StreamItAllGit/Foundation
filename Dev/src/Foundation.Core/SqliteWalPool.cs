// @module: Foundation.Core.Database
// @purpose: Provides standardized SQLite WAL mode connection initialization, pragma enforcement, and online VACUUM INTO hot backups.
// @depends-on: System, System.IO, System.Threading, System.Threading.Tasks, Microsoft.Data.Sqlite
// @exports: SqliteWalPool

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Foundation.Core;

/// <summary>
/// Standardized SQLite connection factory enforcing WAL mode, busy_timeout = 15000, Cache = Default (non-blocking WAL MVCC snapshot isolation),
/// synchronous = NORMAL, foreign_keys = ON, and online atomic VACUUM INTO backups.
/// </summary>
public sealed class SqliteWalPool
{
    private const string DefaultStaticPragmaSql = """
        PRAGMA journal_mode = WAL;
        PRAGMA busy_timeout = 15000;
        PRAGMA synchronous = NORMAL;
        PRAGMA foreign_keys = ON;
        PRAGMA temp_store = MEMORY;
        PRAGMA cache_size = -65536;
        """;

    private readonly string _walPragmaSql;
    private readonly string _fastPragmaSql;
    private readonly object _walInitGate = new();
    private long _walInitializedCreationTicksUtc;

    public string DatabasePath { get; }
    public string ConnectionString { get; }
    public int BusyTimeoutMs { get; }

    public SqliteWalPool(string databasePath, int busyTimeoutMs = 15000, bool pooling = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
        BusyTimeoutMs = Math.Max(1000, busyTimeoutMs);

        string? parentDir = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        _walPragmaSql = BuildPragmaSql(BusyTimeoutMs, includeJournalModeWal: true);
        _fastPragmaSql = BuildPragmaSql(BusyTimeoutMs, includeJournalModeWal: false);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = pooling,
            DefaultTimeout = Math.Max(5, BusyTimeoutMs / 1000)
        };
        ConnectionString = builder.ToString();
    }

    /// <summary>
    /// Clears the cached WAL initialization state so the next connection re-enforces PRAGMA journal_mode = WAL.
    /// </summary>
    public void ResetJournalModeCache() => Volatile.Write(ref _walInitializedCreationTicksUtc, 0);

    public SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        ApplyPoolPragmas(conn);
        return conn;
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        ApplyPoolPragmas(conn);
        return conn;
    }

    private void ApplyPoolPragmas(SqliteConnection connection)
    {
        long cachedTicks = Volatile.Read(ref _walInitializedCreationTicksUtc);
        bool skipJournalPragma = cachedTicks != 0 &&
                                 TryGetNonEmptyCreationTicksUtc(DatabasePath, out long currentTicks) &&
                                 currentTicks == cachedTicks;

        using var cmd = connection.CreateCommand();
        if (skipJournalPragma)
        {
            cmd.CommandText = _fastPragmaSql;
            cmd.ExecuteNonQuery();
            return;
        }

        cmd.CommandText = _walPragmaSql;
        cmd.ExecuteNonQuery();

        lock (_walInitGate)
        {
            if (TryGetNonEmptyCreationTicksUtc(DatabasePath, out long verifiedTicks))
            {
                Volatile.Write(ref _walInitializedCreationTicksUtc, verifiedTicks);
            }
        }
    }

    private static bool TryGetNonEmptyCreationTicksUtc(string path, out long creationTicksUtc)
    {
        try
        {
            var fi = new FileInfo(path);
            if (fi.Exists && fi.Length > 0)
            {
                creationTicksUtc = fi.CreationTimeUtc.Ticks;
                return creationTicksUtc != 0;
            }
        }
        catch
        {
            // Fall through to full WAL pragma enforcement.
        }

        creationTicksUtc = 0;
        return false;
    }

    private static string BuildPragmaSql(int busyTimeoutMs, bool includeJournalModeWal)
    {
        int clamped = Math.Max(1000, busyTimeoutMs);
        if (includeJournalModeWal && clamped == 15000)
        {
            return DefaultStaticPragmaSql;
        }

        return includeJournalModeWal
            ? $"PRAGMA journal_mode = WAL;\nPRAGMA busy_timeout = {clamped};\nPRAGMA synchronous = NORMAL;\nPRAGMA foreign_keys = ON;\nPRAGMA temp_store = MEMORY;\nPRAGMA cache_size = -65536;"
            : $"PRAGMA busy_timeout = {clamped};\nPRAGMA synchronous = NORMAL;\nPRAGMA foreign_keys = ON;\nPRAGMA temp_store = MEMORY;\nPRAGMA cache_size = -65536;";
    }

    public static void ApplyStandardPragmas(SqliteConnection connection, int busyTimeoutMs = 15000)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = busyTimeoutMs == 15000
            ? DefaultStaticPragmaSql
            : BuildPragmaSql(busyTimeoutMs, includeJournalModeWal: true);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Executes an online atomic SQLite backup using VACUUM INTO via a temporary staging file.
    /// </summary>
    public async Task<string> VacuumIntoAsync(string destinationDbPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDbPath);
        string fullDest = Path.GetFullPath(destinationDbPath);
        string? destDir = Path.GetDirectoryName(fullDest);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        string tempPath = $"{fullDest}.{Guid.NewGuid():N}.tmp";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        try
        {
            await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            string escapedTemp = tempPath.Replace("'", "''", StringComparison.Ordinal);
            cmd.CommandText = $"VACUUM INTO '{escapedTemp}';";
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            File.Move(tempPath, fullDest, overwrite: true);
            return fullDest;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* ignore cleanup failure */ }
            }
        }
    }
}
