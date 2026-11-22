# @module: foundation_core
# @purpose: Python 3.12+ multi-module SDK parity with Foundation.Core (path containment, 4-drive sandbox seam, atomic JSON pseudo-registry, Standard 19 settings, SQLite WAL/user_version, PID-safe lockfiles, Standard 35/43 logs/crashes, opt-in loopback automation).
# @depends-on: .automation, .cli, .crash_dumper, .db, .locks, .logging, .pseudo_registry, .sandbox_seam, .settings
# @exports: SDK_VERSION, MODULE_VERSION, AUTOMATION_DISABLED_EXIT_CODE, AutomationDisabledError, AutomationServerOptions, encode_rgba_png, parse_automation_cli_args, AutomationLoopbackServer, StandardCliOptions, build_standard_cli_parser, parse_standard_cli_flags, assert_path_contained, open_wal_sqlite, SqliteSchemaMigrationRunner, recover_corrupt_sqlite, split_sql_statements, verify_snapshot_integrity, vacuum_backup_sqlite, emit_heartbeat, RollingLogSink, ThreeFileSettingsStore, SettingsLoadResult, GranularSettingsStore, sanitize_pii_text, CrashDumper, utc_iso_ms, parse_iso_utc, get_process_state, get_process_start_time_utc, PidSentinelLock, CONTAINMENT_EXIT_CODE, SandboxContainmentError, is_containment_violation, SandboxMountSpec, SandboxResolution, get_subst_mappings, resolve_sandbox_environment, resolve_known_folders, get_known_folder, is_path_within_sandbox, assert_sandbox_path_if_active, map_host_path_to_sandbox, REGISTRY_HIVE_SCHEMA_URI, REGISTRY_HIVE_SCHEMA_VERSION, PlatformRegistryHive, PlatformRegistryValueKind, JsonHivePlatformRegistry

from __future__ import annotations

from .automation import (
    AUTOMATION_DISABLED_EXIT_CODE,
    AutomationDisabledError,
    AutomationLoopbackServer,
    AutomationServerOptions,
    encode_rgba_png,
    parse_automation_cli_args,
)
from .cli import (
    StandardCliOptions,
    assert_path_contained,
    build_standard_cli_parser,
    parse_standard_cli_flags,
)
from .crash_dumper import CrashDumper, sanitize_pii_text
from .db import (
    SqliteSchemaMigrationRunner,
    open_wal_sqlite,
    recover_corrupt_sqlite,
    split_sql_statements,
    vacuum_backup_sqlite,
    verify_snapshot_integrity,
)
from .locks import (
    PidSentinelLock,
    get_process_start_time_utc,
    get_process_state,
    parse_iso_utc,
    utc_iso_ms,
)
from .logging import RollingLogSink, emit_heartbeat
from .pseudo_registry import (
    REGISTRY_HIVE_SCHEMA_URI,
    REGISTRY_HIVE_SCHEMA_VERSION,
    JsonHivePlatformRegistry,
    PlatformRegistryHive,
    PlatformRegistryValueKind,
)
from .sandbox_seam import (
    CONTAINMENT_EXIT_CODE,
    SandboxContainmentError,
    SandboxMountSpec,
    SandboxResolution,
    assert_sandbox_path_if_active,
    get_known_folder,
    get_subst_mappings,
    is_containment_violation,
    is_path_within_sandbox,
    map_host_path_to_sandbox,
    resolve_known_folders,
    resolve_sandbox_environment,
)
from .settings import GranularSettingsStore, SettingsLoadResult, ThreeFileSettingsStore

SDK_VERSION: str = "0.5.4"
MODULE_VERSION: str = "0.1.0"

__all__ = [
    "SDK_VERSION",
    "MODULE_VERSION",
    "AUTOMATION_DISABLED_EXIT_CODE",
    "AutomationDisabledError",
    "AutomationServerOptions",
    "encode_rgba_png",
    "parse_automation_cli_args",
    "AutomationLoopbackServer",
    "assert_path_contained",
    "StandardCliOptions",
    "build_standard_cli_parser",
    "parse_standard_cli_flags",
    "open_wal_sqlite",
    "SqliteSchemaMigrationRunner",
    "recover_corrupt_sqlite",
    "split_sql_statements",
    "verify_snapshot_integrity",
    "vacuum_backup_sqlite",
    "emit_heartbeat",
    "RollingLogSink",
    "ThreeFileSettingsStore",
    "SettingsLoadResult",
    "GranularSettingsStore",
    "sanitize_pii_text",
    "CrashDumper",
    "utc_iso_ms",
    "parse_iso_utc",
    "get_process_state",
    "get_process_start_time_utc",
    "PidSentinelLock",
    "CONTAINMENT_EXIT_CODE",
    "SandboxContainmentError",
    "is_containment_violation",
    "SandboxMountSpec",
    "SandboxResolution",
    "get_subst_mappings",
    "resolve_sandbox_environment",
    "resolve_known_folders",
    "get_known_folder",
    "is_path_within_sandbox",
    "assert_sandbox_path_if_active",
    "map_host_path_to_sandbox",
    "REGISTRY_HIVE_SCHEMA_URI",
    "REGISTRY_HIVE_SCHEMA_VERSION",
    "PlatformRegistryHive",
    "PlatformRegistryValueKind",
    "JsonHivePlatformRegistry",
]
