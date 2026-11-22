# @module: foundation_core.db
# @purpose: SQLite WAL/read-only URI connections, PRAGMA user_version atomic migrations, VACUUM INTO live snapshots, and corrupt DB recovery (Standard 10).
# @depends-on: pathlib, shutil, sqlite3, time, typing
# @exports: open_wal_sqlite, recover_corrupt_sqlite, split_sql_statements, verify_snapshot_integrity, vacuum_backup_sqlite, SqliteSchemaMigrationRunner

from __future__ import annotations

from pathlib import Path
import shutil
import sqlite3
import time
from typing import Callable


def open_wal_sqlite(
    db_path: str | Path,
    busy_timeout_ms: int = 15000,
    *,
    read_only: bool = False,
) -> sqlite3.Connection:
    """Opens a SQLite connection in WAL write mode or Standard 10 ?mode=ro query_only mode."""
    target = Path(db_path).resolve()
    timeout_s = max(5.0, busy_timeout_ms / 1000.0)
    if read_only:
        conn = sqlite3.connect(f"{target.as_uri()}?mode=ro", uri=True, timeout=timeout_s)
        conn.execute("PRAGMA query_only = ON;")
        conn.execute("PRAGMA foreign_keys = ON;")
        conn.execute(f"PRAGMA busy_timeout = {max(1000, int(busy_timeout_ms))};")
        return conn

    target.parent.mkdir(parents=True, exist_ok=True)
    conn = sqlite3.connect(str(target), timeout=timeout_s)
    conn.execute("PRAGMA journal_mode = WAL;")
    conn.execute(f"PRAGMA busy_timeout = {max(1000, int(busy_timeout_ms))};")
    conn.execute("PRAGMA synchronous = NORMAL;")
    conn.execute("PRAGMA foreign_keys = ON;")
    conn.execute("PRAGMA cache_size = -65536;")
    return conn


def recover_corrupt_sqlite(db_path: str | Path) -> Path | None:
    """Quarantines a corrupt SQLite database file as .corrupt.<timestamp> so a fresh schema can be initialized."""
    target = Path(db_path).resolve()
    if not target.exists():
        return None
    stamp = time.strftime("%Y%m%d_%H%M%S", time.gmtime())
    quarantined = target.with_name(f"{target.name}.corrupt.{stamp}")
    shutil.move(str(target), str(quarantined))
    for suffix in ("-wal", "-shm"):
        side = target.with_name(target.name + suffix)
        if side.exists():
            side.unlink(missing_ok=True)
    return quarantined


def _strip_sql_comments(fragment: str) -> str:
    out: list[str] = []
    i, n = 0, len(fragment)
    while i < n:
        if fragment[i : i + 2] == "--":
            nl = fragment.find("\n", i + 2)
            i = n if nl == -1 else nl + 1
        elif fragment[i : i + 2] == "/*":
            end = fragment.find("*/", i + 2)
            i = n if end == -1 else end + 2
        else:
            out.append(fragment[i])
            i += 1
    return "".join(out)


def split_sql_statements(sql_script: str) -> list[str]:
    """Splits a SQL migration script into individual complete statements without calling executescript()."""
    statements: list[str] = []
    buffer = ""
    for line in sql_script.splitlines(keepends=True):
        buffer += line
        if sqlite3.complete_statement(buffer):
            cleaned = buffer.strip()
            if cleaned and _strip_sql_comments(cleaned).strip():
                statements.append(cleaned)
            buffer = ""
    if buffer.strip() and _strip_sql_comments(buffer).strip():
        raise ValueError(f"Incomplete trailing SQL statement in migration: {buffer.strip()}")
    return statements


def verify_snapshot_integrity(snapshot_path: str | Path) -> None:
    """Opens a standalone SQLite snapshot read-only and verifies PRAGMA integrity_check == 'ok'."""
    resolved = Path(snapshot_path).resolve()
    conn = open_wal_sqlite(resolved, read_only=True)
    try:
        row = conn.execute("PRAGMA integrity_check;").fetchone()
    finally:
        conn.close()
    result = row[0] if row else None
    if result != "ok":
        raise RuntimeError(f"Snapshot integrity check failed for '{resolved}': {result!r}")


def vacuum_backup_sqlite(
    conn: sqlite3.Connection,
    snapshot_path: str | Path,
    *,
    verify: bool = True,
) -> Path:
    """Creates a live defragmented snapshot via VACUUM INTO and verifies its integrity read-only."""
    target = Path(snapshot_path).resolve()
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists():
        target.unlink()
    escaped = str(target).replace("'", "''")
    conn.execute(f"VACUUM INTO '{escaped}';")
    if verify:
        verify_snapshot_integrity(target)
    return target


class SqliteSchemaMigrationRunner:
    """Monotonic PRAGMA user_version migration runner with explicit BEGIN IMMEDIATE rollback safety."""

    def __init__(self, conn: sqlite3.Connection) -> None:
        self.conn = conn
        self._bridge_legacy_version_if_needed()

    def _legacy_table_max_version(self) -> int:
        row = self.conn.execute(
            "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = '_foundation_schema_migrations';"
        ).fetchone()
        if not row:
            return 0
        ver_row = self.conn.execute(
            "SELECT COALESCE(MAX(version), 0) FROM _foundation_schema_migrations;"
        ).fetchone()
        return int(ver_row[0]) if ver_row and ver_row[0] is not None else 0

    def _bridge_legacy_version_if_needed(self) -> None:
        user_ver = int(self.conn.execute("PRAGMA user_version;").fetchone()[0])
        if user_ver == 0 and (legacy_ver := self._legacy_table_max_version()) > 0:
            self.conn.execute(f"PRAGMA user_version = {int(legacy_ver)};")

    def current_version(self) -> int:
        user_ver = int(self.conn.execute("PRAGMA user_version;").fetchone()[0])
        if user_ver > 0:
            return user_ver
        return self._legacy_table_max_version()

    def apply_migrations(
        self,
        migrations: list[tuple[int, str, Callable[[sqlite3.Connection], None]]],
    ) -> int:
        current = self.current_version()
        if int(self.conn.execute("PRAGMA user_version;").fetchone()[0]) < current:
            self.conn.execute(f"PRAGMA user_version = {int(current)};")
        for version, _description, fn in sorted(migrations, key=lambda m: m[0]):
            if version <= current:
                continue
            self._run_atomic_step(version, lambda _c, callback=fn: callback(self.conn))
            current = version
        return current

    def apply_sql_migrations(self, migrations_dir: str | Path) -> int:
        """Applies forward-only NNNN_*.sql migration files using PRAGMA user_version without executescript()."""
        folder = Path(migrations_dir).resolve()
        if not folder.exists():
            return self.current_version()

        current = self.current_version()
        if int(self.conn.execute("PRAGMA user_version;").fetchone()[0]) < current:
            self.conn.execute(f"PRAGMA user_version = {int(current)};")

        candidates: list[tuple[int, Path]] = []
        for sql_file in sorted(folder.glob("*.sql")):
            prefix = sql_file.name.split("_", 1)[0]
            if prefix.isdigit():
                candidates.append((int(prefix), sql_file))

        for version, sql_file in sorted(candidates, key=lambda item: item[0]):
            if version <= current:
                continue
            statements = split_sql_statements(sql_file.read_text(encoding="utf-8"))

            def _exec_file(c: sqlite3.Connection, stmts: list[str] = statements) -> None:
                for stmt in stmts:
                    c.execute(stmt)

            self._run_atomic_step(version, _exec_file)
            current = version
        return current

    def _run_atomic_step(self, version: int, step_fn: Callable[[sqlite3.Connection], None]) -> None:
        if self.conn.in_transaction:
            self.conn.commit()
        try:
            self.conn.execute("BEGIN IMMEDIATE;")
            step_fn(self.conn)
            self.conn.execute(f"PRAGMA user_version = {int(version)};")
            self.conn.commit()
        except Exception:
            try:
                self.conn.execute("ROLLBACK;")
            except sqlite3.Error:
                if self.conn.in_transaction:
                    self.conn.rollback()
            raise
