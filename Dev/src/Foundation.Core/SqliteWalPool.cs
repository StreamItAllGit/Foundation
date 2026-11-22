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
    public string DatabasePath { get; }
    public string ConnectionString { get; }
    public int BusyTimeoutMs { get; }
    private readonly object _pragmaGate = new();
    private bool _journalModeInitialized;

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

    public SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        lock (_pragmaGate)
        {
            ApplyStandardPragmas(conn, BusyTimeoutMs, initializeJournalMode: !_journalModeInitialized);
            _journalModeInitialized = true;
        }
        return conn;
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        lock (_pragmaGate)
        {
            ApplyStandardPragmas(conn, BusyTimeoutMs, initializeJournalMode: !_journalModeInitialized);
            _journalModeInitialized = true;
        }
        return conn;
    }

    public static void ApplyStandardPragmas(SqliteConnection connection, int busyTimeoutMs = 15000)
    {
        ApplyStandardPragmas(connection, busyTimeoutMs, initializeJournalMode: true);
    }

    private static void ApplyStandardPragmas(
        SqliteConnection connection,
        int busyTimeoutMs,
        bool initializeJournalMode)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using var cmd = connection.CreateCommand();
        string journalMode = initializeJournalMode ? "PRAGMA journal_mode = WAL;" : "";
        cmd.CommandText = $"""
            {journalMode}
            PRAGMA busy_timeout = {Math.Max(1000, busyTimeoutMs)};
            PRAGMA synchronous = NORMAL;
            PRAGMA foreign_keys = ON;
            PRAGMA temp_store = MEMORY;
            PRAGMA cache_size = -65536;
            """;
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
