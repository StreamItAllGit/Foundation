# @module: foundation_core.settings
# @purpose: Standard 19 GranularSettingsStore (data/config/settings*.json, atomic .tmp->fsync->.bak->replace, schema migrations, unknown-key preservation, 20-snapshot config_history/, quarantine/) plus legacy ThreeFileSettingsStore.
# @depends-on: contextlib, dataclasses, datetime, json, os, pathlib, shutil, threading, time, typing, .cli
# @exports: ThreeFileSettingsStore, SettingsLoadResult, GranularSettingsStore

from __future__ import annotations

from contextlib import suppress
from dataclasses import dataclass, field
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import shutil
import threading
import time
from typing import Any, Callable, Iterable, Mapping

from .cli import assert_path_contained


def _utc_iso_ms() -> str:
    now = datetime.now(timezone.utc)
    return now.strftime("%Y-%m-%dT%H:%M:%S.") + f"{now.microsecond // 1000:03d}Z"


class ThreeFileSettingsStore:
    """Manages legacy 3-file JSON settings persistence and config_history/ rotation."""

    FILE_NAMES = (
        "app.settings.json",
        "paths.settings.json",
        "integrations.settings.json",
    )

    def __init__(self, data_dir: str | Path, max_snapshots: int = 10) -> None:
        self.data_dir = Path(data_dir).resolve()
        self.history_dir = self.data_dir / "config_history"
        self.max_snapshots = max(1, max_snapshots)

    def ensure_directories(self) -> None:
        self.data_dir.mkdir(parents=True, exist_ok=True)
        self.history_dir.mkdir(parents=True, exist_ok=True)

    def load_file_with_recovery(self, filename: str, default_payload: dict[str, Any]) -> dict[str, Any]:
        self.ensure_directories()
        target = self.data_dir / filename
        bak = self.data_dir / f"{filename}.bak"

        for candidate in (target, bak):
            if candidate.exists():
                try:
                    data = json.loads(candidate.read_text(encoding="utf-8"))
                    if isinstance(data, dict):
                        if candidate == bak:
                            shutil.copy2(bak, target)
                        return data
                except Exception:
                    continue

        self.save_file_atomic(filename, default_payload, create_snapshot=False)
        return dict(default_payload)

    def save_file_atomic(
        self,
        filename: str,
        payload: dict[str, Any],
        create_snapshot: bool = True,
    ) -> Path:
        self.ensure_directories()
        target = self.data_dir / filename
        tmp = self.data_dir / f"{filename}.tmp"
        bak = self.data_dir / f"{filename}.bak"

        serialized = json.dumps(payload, indent=2, sort_keys=True) + "\n"
        tmp.write_text(serialized, encoding="utf-8")

        if target.exists():
            shutil.copy2(target, bak)
            if create_snapshot:
                stamp = time.strftime("%Y%m%d_%H%M%S", time.gmtime())
                snap = self.history_dir / f"{filename}.{stamp}.json"
                shutil.copy2(target, snap)
                self._prune_snapshots(filename)

        tmp.replace(target)
        return target

    def _prune_snapshots(self, filename: str) -> None:
        snaps = sorted(
            self.history_dir.glob(f"{filename}.*.json"),
            key=lambda p: p.stat().st_mtime,
            reverse=True,
        )
        for stale in snaps[self.max_snapshots :]:
            with suppress(OSError):
                stale.unlink()


@dataclass(frozen=True, slots=True)
class SettingsLoadResult:
    """Outcome of Standard 19 GranularSettingsStore.load_or_self_heal."""

    value: dict[str, Any]
    loaded_from_primary: bool
    healed_from_backup: bool
    created_default: bool
    quarantined_path: Path | None = None
    migrated_from_version: int | None = None
    diagnostic_warning: str | None = None
    extra: dict[str, Any] = field(default_factory=dict)


class GranularSettingsStore:
    """Standard 19 atomic settings store for data/config/settings*.json with quarantine & 20-snapshot history."""

    DEFAULT_MAX_SNAPSHOTS = 20

    def __init__(
        self,
        data_dir: str | Path,
        max_snapshots: int = DEFAULT_MAX_SNAPSHOTS,
        history_dir: str | Path | None = None,
        quarantine_dir: str | Path | None = None,
    ) -> None:
        self.data_dir = Path(data_dir).resolve()
        self.config_dir = self.data_dir / "config"
        self.history_dir = Path(history_dir).resolve() if history_dir is not None else (self.data_dir / "config_history")
        self.quarantine_dir = (
            Path(quarantine_dir).resolve() if quarantine_dir is not None else (self.data_dir / "quarantine")
        )
        self.max_snapshots = max(1, int(max_snapshots))
        self._last_extra: dict[Path, dict[str, Any]] = {}

    def ensure_directories(self) -> None:
        self.data_dir.mkdir(parents=True, exist_ok=True)
        self.config_dir.mkdir(parents=True, exist_ok=True)
        self.history_dir.mkdir(parents=True, exist_ok=True)
        self.quarantine_dir.mkdir(parents=True, exist_ok=True)

    def resolve_settings_path(self, filename: str | Path = "settings.json") -> Path:
        raw = str(filename).strip()
        if not raw:
            raise ValueError("Settings filename must be non-empty.")
        cand = Path(raw)
        if cand.is_absolute():
            return assert_path_contained(cand, self.data_dir)
        return assert_path_contained(self.config_dir / cand, self.config_dir)

    def list_history_snapshots(self, filename: str | Path = "settings.json") -> list[Path]:
        if not self.history_dir.exists():
            return []
        stem = Path(self.resolve_settings_path(filename)).stem
        return sorted(self.history_dir.glob(f"{stem}_*.json"), key=lambda p: p.name, reverse=True)

    def save_atomic(
        self,
        filename: str | Path,
        payload: Mapping[str, Any],
        *,
        extra: Mapping[str, Any] | None = None,
        create_snapshot: bool = True,
    ) -> Path:
        self.ensure_directories()
        target = self.resolve_settings_path(filename)
        target.parent.mkdir(parents=True, exist_ok=True)

        merged: dict[str, Any] = dict(self._last_extra.get(target, {}))
        if isinstance(payload.get("_extra"), Mapping):
            for ek, ev in payload["_extra"].items():
                merged[str(ek)] = ev
        if extra is not None:
            for ek, ev in extra.items():
                merged[str(ek)] = ev
        for k, v in payload.items():
            if k != "_extra":
                merged[str(k)] = v

        serialized = json.dumps(merged, indent=2, ensure_ascii=False) + "\n"
        tmp = target.with_name(f"{target.name}.{os.getpid()}.{threading.get_ident()}.tmp")
        bak = target.with_name(f"{target.name}.bak")
        try:
            with tmp.open("w", encoding="utf-8", newline="\n") as fh:
                fh.write(serialized)
                fh.flush()
                os.fsync(fh.fileno())
            if target.exists():
                with suppress(OSError):
                    shutil.copy2(target, bak)
            os.replace(tmp, target)
            if create_snapshot:
                self._write_snapshot_and_prune(target.stem, serialized)
        finally:
            tmp.unlink(missing_ok=True)
        return target

    def load_or_self_heal(
        self,
        filename: str | Path = "settings.json",
        default_factory: Callable[[], dict[str, Any]] | None = None,
        *,
        current_schema_version: int = 2,
        migrations: Mapping[int, Callable[[dict[str, Any]], dict[str, Any]]] | None = None,
        known_keys: Iterable[str] | None = None,
    ) -> SettingsLoadResult:
        self.ensure_directories()
        target = self.resolve_settings_path(filename)
        bak = target.with_name(f"{target.name}.bak")

        doc: dict[str, Any] | None = None
        loaded_primary = False
        healed = False
        created_default = False
        quarantined_path: Path | None = None
        warning: str | None = None

        if target.exists():
            doc = self._try_read_json_dict(target)
            if doc is not None:
                loaded_primary = True
            else:
                quarantined_path = self._quarantine_corrupt_file(target)

        if doc is None and bak.exists():
            if (bak_doc := self._try_read_json_dict(bak)) is not None:
                doc = bak_doc
                healed = True
                shutil.copy2(bak, target)
                warning = f"Primary settings '{target.name}' quarantined ({quarantined_path.name if quarantined_path else 'missing'}); recovered from '.bak'."

        if doc is None:
            for snap in self.list_history_snapshots(filename):
                if (snap_doc := self._try_read_json_dict(snap)) is not None:
                    doc = snap_doc
                    healed = True
                    shutil.copy2(snap, target)
                    warning = f"Primary settings '{target.name}' recovered from history snapshot '{snap.name}'."
                    break

        if doc is None:
            factory = default_factory or (lambda: {"schemaVersion": current_schema_version, "updatedAtUtc": _utc_iso_ms()})
            doc = dict(factory())
            doc.setdefault("schemaVersion", current_schema_version)
            created_default = True
            if quarantined_path is not None:
                warning = f"Primary settings '{target.name}' quarantined to '{quarantined_path.name}'; initialized defaults."
            self.save_atomic(filename, doc, create_snapshot=False)

        migrated_from: int | None = None
        raw_ver = doc.get("schemaVersion", 1)
        if not isinstance(raw_ver, bool) and isinstance(raw_ver, int) and raw_ver < current_schema_version:
            migrated_from = raw_ver
            self._write_snapshot_and_prune(target.stem, json.dumps(doc, indent=2, ensure_ascii=False) + "\n")
            ver = raw_ver
            while ver < current_schema_version:
                if migrations and ver in migrations:
                    doc = dict(migrations[ver](dict(doc)))
                ver += 1
            doc["schemaVersion"] = current_schema_version
            doc["updatedAtUtc"] = _utc_iso_ms()
            self.save_atomic(filename, doc, create_snapshot=True)

        extra: dict[str, Any] = {}
        if known_keys is not None:
            known_set = set(known_keys) | {"schemaVersion", "$schema", "appId", "updatedAtUtc"}
            extra = {k: doc[k] for k in list(doc.keys()) if k not in known_set}
            for k in extra:
                doc.pop(k, None)
            self._last_extra[target] = dict(extra)
        else:
            self._last_extra.pop(target, None)

        return SettingsLoadResult(
            value=doc,
            loaded_from_primary=loaded_primary,
            healed_from_backup=healed,
            created_default=created_default,
            quarantined_path=quarantined_path,
            migrated_from_version=migrated_from,
            diagnostic_warning=warning,
            extra=extra,
        )

    @staticmethod
    def _try_read_json_dict(path: Path) -> dict[str, Any] | None:
        with suppress(OSError, json.JSONDecodeError, ValueError):
            raw = path.read_text(encoding="utf-8").strip()
            if not raw:
                return None
            parsed = json.loads(raw)
            return dict(parsed) if isinstance(parsed, dict) else None
        return None

    def _quarantine_corrupt_file(self, corrupt_path: Path) -> Path | None:
        if not corrupt_path.exists():
            return None
        self.quarantine_dir.mkdir(parents=True, exist_ok=True)
        stamp = datetime.now(timezone.utc).strftime("%Y%m%d_%H%M%S")
        dest = self.quarantine_dir / f"{corrupt_path.stem}.corrupt_{stamp}.json"
        if dest.exists():
            dest = self.quarantine_dir / f"{corrupt_path.stem}.corrupt_{stamp}_{int((time.time() % 1) * 1000):03d}.json"
        shutil.move(str(corrupt_path), str(dest))
        return dest

    def _write_snapshot_and_prune(self, stem: str, serialized: str) -> None:
        self.history_dir.mkdir(parents=True, exist_ok=True)
        now = datetime.now(timezone.utc)
        base_stamp = f"{stem}_{now.strftime('%Y%m%d_%H%M%S')}_{now.microsecond // 1000:03d}"
        snap_path = self.history_dir / f"{base_stamp}.json"
        seq = 1
        while snap_path.exists():
            snap_path = self.history_dir / f"{base_stamp}_{seq:02d}.json"
            seq += 1
        snap_path.write_text(serialized, encoding="utf-8", newline="\n")

        snaps = sorted(self.history_dir.glob(f"{stem}_*.json"), key=lambda p: p.name, reverse=True)
        for stale in snaps[self.max_snapshots :]:
            with suppress(OSError):
                stale.unlink()
