# @module: foundation_core.sandbox_seam
# @purpose: Process-env-only 4-drive Stage 2 sandbox resolution, known-folder mapping, and fail-closed ExitCode=3 containment guards (Standards 02 & 25).
# @depends-on: ctypes, dataclasses, os, pathlib, string, subprocess, tempfile, typing
# @exports: CONTAINMENT_EXIT_CODE, SandboxContainmentError, is_containment_violation, SandboxMountSpec, SandboxResolution, get_subst_mappings, resolve_sandbox_environment, resolve_known_folders, get_known_folder, is_path_within_sandbox, assert_sandbox_path_if_active, map_host_path_to_sandbox

from __future__ import annotations

import ctypes
from dataclasses import dataclass
import os
from pathlib import Path
import string
import subprocess
import tempfile
from typing import Mapping

CONTAINMENT_EXIT_CODE: int = 3
_DRIVE_FIXED: int = 3
_TRUTHY: frozenset[str] = frozenset({"1", "true", "yes", "on"})
_DRIVE_ROLES: tuple[tuple[str, str], ...] = (
    ("SANDBOX_C_DRIVE", "system_drive_root"),
    ("SANDBOX_D_DRIVE", "storage_drive_root"),
    ("SANDBOX_REMOVABLE_DRIVE", "removable_drive_root"),
    ("SANDBOX_DISC_DRIVE", "disc_drive_root"),
)


class SandboxContainmentError(ValueError):
    """Raised on Stage 2 sandbox containment or host-collision violations (ExitCode=3)."""

    def __init__(self, message: str, exit_code: int = CONTAINMENT_EXIT_CODE) -> None:
        super().__init__(message)
        self.exit_code = exit_code


def is_containment_violation(exc: BaseException) -> bool:
    """Returns True when exc represents a sandbox containment violation (ExitCode=3)."""
    return isinstance(exc, SandboxContainmentError) or getattr(exc, "exit_code", None) == CONTAINMENT_EXIT_CODE


@dataclass(slots=True, frozen=True)
class SandboxMountSpec:
    drive_role: str
    drive_mount: str
    directory_path: Path | None
    is_enabled: bool

    def to_dict(self) -> dict[str, object]:
        return {
            "driveRole": self.drive_role,
            "driveMount": self.drive_mount,
            "directoryPath": str(self.directory_path) if self.directory_path is not None else None,
            "isEnabled": self.is_enabled,
        }


@dataclass(slots=True, frozen=True)
class SandboxResolution:
    is_sandbox_active: bool
    sandbox_root: Path | None = None
    system_drive_root: Path | None = None
    storage_drive_root: Path | None = None
    removable_drive_root: Path | None = None
    disc_drive_root: Path | None = None
    sandbox_profile_dir: Path | None = None
    registry_hive_path: Path | None = None
    registry_baseline_path: Path | None = None
    registry_lock_path: Path | None = None
    mounts: tuple[SandboxMountSpec, ...] = ()
    backing_roots: tuple[Path, ...] = ()


def _lookup_env(env: Mapping[str, str | None], key: str) -> str | None:
    upper = key.upper()
    return next((v.strip() for k, v in env.items() if k.upper() == upper and isinstance(v, str) and v.strip()), None)


def _normalize_path(raw: str | Path) -> Path:
    text = str(raw).strip().strip('"')
    if text.upper().startswith("\\\\?\\UNC\\"):
        text = "\\\\" + text[8:]
    elif text.startswith("\\\\?\\"):
        text = text[4:]
    if not text:
        raise SandboxContainmentError("Fail-Closed Containment Violation: empty path is not permitted.")
    if os.name == "nt" and len(text) == 2 and text[1] == ":" and text[0].isalpha():
        text = f"{text[0].upper()}:{os.sep}"
    return Path(os.path.abspath(text))


def _norm_key(path: Path) -> Path:
    return Path(os.path.normcase(os.path.abspath(str(path))))


def _is_same_or_child(candidate: Path, root: Path) -> bool:
    cand_k, root_k = _norm_key(candidate), _norm_key(root)
    return cand_k == root_k or root_k in cand_k.parents


def get_subst_mappings() -> dict[str, Path]:
    """Queries active Windows `subst` drive mappings (`X:` -> target Path) without shell=True."""
    if os.name != "nt":
        return {}
    try:
        proc = subprocess.run(["subst"], capture_output=True, text=True, timeout=3, check=False)
    except (OSError, subprocess.SubprocessError):
        return {}
    mappings: dict[str, Path] = {}
    for line in proc.stdout.splitlines() if proc.returncode == 0 else ():
        left, sep, right = line.partition("=>")
        token = left.strip().rstrip(":\\/").upper()
        if sep and len(token) == 1 and token.isalpha() and right.strip():
            mappings[f"{token}:"] = _normalize_path(right.strip())
    return mappings


def _resolve_backing_path(path: Path, subst_map: Mapping[str, Path]) -> Path:
    norm = _normalize_path(path)
    if os.name == "nt":
        drive = os.path.splitdrive(str(norm))[0].upper()
        if drive in subst_map:
            rel = str(norm)[len(drive) :].lstrip("\\/")
            return _normalize_path(subst_map[drive] / rel) if rel else subst_map[drive]
    try:
        return _normalize_path(norm.resolve())
    except OSError:
        return norm


def _collect_host_collision_roots(subst_map: Mapping[str, Path]) -> tuple[list[Path], list[Path], Path | None]:
    forbidden = [_normalize_path(v) for var in ("SystemRoot", "WINDIR", "ProgramFiles", "ProgramFiles(x86)", "ProgramData", "APPDATA") if (v := os.environ.get(var))]
    user_homes = [_normalize_path(local_app)] if (local_app := os.environ.get("LOCALAPPDATA")) else []
    if user_homes:
        forbidden.append(user_homes[0] / "Programs")
    for home_raw in (Path.home(), os.environ.get("USERPROFILE"), os.environ.get("HOME")):
        if home_raw:
            h = _normalize_path(home_raw)
            user_homes.append(h)
            forbidden.extend((h / "Desktop", h / "Documents"))
    if os.name == "nt":
        k32 = ctypes.WinDLL("kernel32", use_last_error=True)
        mask = int(k32.GetLogicalDrives())
        for idx, ch in enumerate(string.ascii_uppercase):
            tag = f"{ch}:"
            drv = Path(f"{tag}{os.sep}")
            if (mask & (1 << idx)) and tag not in subst_map and k32.GetDriveTypeW(str(drv)) == _DRIVE_FIXED:
                forbidden.extend(_normalize_path(c) for f in ("Programs", "Projects") if (c := drv / f).exists())
    try:
        temp_root: Path | None = _normalize_path(tempfile.gettempdir())
    except OSError:
        temp_root = None
    if temp_root is not None and any(_is_same_or_child(temp_root, f) for f in forbidden):
        temp_root = None
    return forbidden, user_homes, temp_root


def _validate_not_host_collision(
    label: str, candidate: Path, subst_map: Mapping[str, Path], host_roots: tuple[list[Path], list[Path], Path | None]
) -> Path:
    forbidden, user_homes, temp_root = host_roots
    backing = _resolve_backing_path(candidate, subst_map)
    for probe in (candidate, backing):
        for host_root in forbidden:
            if _is_same_or_child(probe, host_root) or _is_same_or_child(host_root, probe):
                raise SandboxContainmentError(f"Fail-Closed Sandbox Host Collision ({label}): '{probe}' collides with '{host_root}'.")
        for user_root in user_homes:
            if _is_same_or_child(user_root, probe) or (_is_same_or_child(probe, user_root) and (temp_root is None or not _is_same_or_child(probe, temp_root))):
                raise SandboxContainmentError(f"Fail-Closed Sandbox Host Collision ({label}): '{probe}' collides with user root '{user_root}'.")
    return backing


def resolve_sandbox_environment(env_override: Mapping[str, str | None] | None = None) -> SandboxResolution:
    """Resolves Stage 2 4-drive sandbox state strictly from process env (never auto-reading .secrets/.env)."""
    env: Mapping[str, str | None] = os.environ if env_override is None else env_override
    iso_val, raw_root = _lookup_env(env, "APP_ISOLATED_TEST"), _lookup_env(env, "FOUNDATION_SANDBOX_ROOT")
    if (iso_val is not None and iso_val.lower() in _TRUTHY) or not raw_root:
        return SandboxResolution(is_sandbox_active=False)
    subst_map = get_subst_mappings()
    host_roots = _collect_host_collision_roots(subst_map)
    sandbox_root = _normalize_path(raw_root)
    backings = [_validate_not_host_collision("FOUNDATION_SANDBOX_ROOT", sandbox_root, subst_map, host_roots)]
    drives: dict[str, Path | None] = {}
    mounts: list[SandboxMountSpec] = []
    for env_key, field_name in _DRIVE_ROLES:
        raw_drive = _lookup_env(env, env_key)
        norm_drive = _normalize_path(raw_drive) if raw_drive else None
        backing = _validate_not_host_collision(env_key, norm_drive, subst_map, host_roots) if norm_drive else None
        if backing is not None:
            backings.append(backing)
        drives[field_name] = norm_drive
        mounts.append(SandboxMountSpec(env_key, (os.path.splitdrive(str(norm_drive))[0] or str(norm_drive)) if norm_drive else "", backing, norm_drive is not None))
    c_drive = drives["system_drive_root"]
    raw_profile = _lookup_env(env, "FOUNDATION_SANDBOX_PROFILE_DIR")
    profile_dir = _normalize_path(raw_profile) if raw_profile else ((c_drive or (sandbox_root / "C")) / "Users" / "Sandbox")
    backings.append(_validate_not_host_collision("FOUNDATION_SANDBOX_PROFILE_DIR", profile_dir, subst_map, host_roots))
    return SandboxResolution(
        is_sandbox_active=True, sandbox_root=sandbox_root, system_drive_root=c_drive,
        storage_drive_root=drives["storage_drive_root"], removable_drive_root=drives["removable_drive_root"],
        disc_drive_root=drives["disc_drive_root"], sandbox_profile_dir=profile_dir,
        registry_hive_path=sandbox_root / "registry" / "registry.hive.json",
        registry_baseline_path=sandbox_root / "registry" / "registry.baseline.json",
        registry_lock_path=sandbox_root / "locks" / "registry.hive.lock",
        mounts=tuple(mounts), backing_roots=tuple(dict.fromkeys(backings)),
    )


def _build_known_folder_map(
    profile: Path, local_app: Path, roaming_app: Path, prog_files: Path, prog_x86: Path, common_app: Path, windows: Path
) -> dict[str, Path]:
    start_menu = roaming_app / "Microsoft" / "Windows" / "Start Menu"
    programs = start_menu / "Programs"
    return {
        "UserProfile": profile, "LocalApplicationData": local_app, "ApplicationData": roaming_app,
        "Desktop": profile / "Desktop", "DesktopDirectory": profile / "Desktop",
        "MyDocuments": profile / "Documents", "Personal": profile / "Documents",
        "StartMenu": start_menu, "Programs": programs, "Startup": programs / "Startup",
        "ProgramFiles": prog_files, "ProgramFilesX86": prog_x86, "CommonApplicationData": common_app, "Windows": windows,
    }


def _host_known_folders(env: Mapping[str, str | None]) -> dict[str, Path]:
    profile = _normalize_path(_lookup_env(env, "USERPROFILE") or _lookup_env(env, "HOME") or Path.home())
    sys_drive = _normalize_path(_lookup_env(env, "SystemDrive") or profile.anchor or os.sep)
    return _build_known_folder_map(
        profile, _normalize_path(_lookup_env(env, "LOCALAPPDATA") or (profile / "AppData" / "Local")),
        _normalize_path(_lookup_env(env, "APPDATA") or (profile / "AppData" / "Roaming")),
        _normalize_path(_lookup_env(env, "ProgramFiles") or (sys_drive / "Program Files")),
        _normalize_path(_lookup_env(env, "ProgramFiles(x86)") or (sys_drive / "Program Files (x86)")),
        _normalize_path(_lookup_env(env, "ProgramData") or (sys_drive / "ProgramData")),
        _normalize_path(_lookup_env(env, "SystemRoot") or _lookup_env(env, "WINDIR") or (sys_drive / "Windows")),
    )


def resolve_known_folders(
    resolution: SandboxResolution | None = None, env_override: Mapping[str, str | None] | None = None
) -> dict[str, Path]:
    """Resolves Windows SpecialFolder-equivalent paths into the sandbox when active, or host OS paths otherwise."""
    res = resolution if resolution is not None else resolve_sandbox_environment(env_override)
    if not res.is_sandbox_active or res.sandbox_root is None:
        return _host_known_folders(os.environ if env_override is None else env_override)
    c_root = res.system_drive_root or (res.sandbox_root / "C")
    profile = res.sandbox_profile_dir or (c_root / "Users" / "Sandbox")
    return _build_known_folder_map(
        profile, profile / "AppData" / "Local", profile / "AppData" / "Roaming",
        c_root / "Program Files", c_root / "Program Files (x86)", c_root / "ProgramData", c_root / "Windows",
    )


def get_known_folder(
    folder_name: str, resolution: SandboxResolution | None = None, env_override: Mapping[str, str | None] | None = None
) -> Path:
    for key, path in resolve_known_folders(resolution, env_override).items():
        if key.lower() == folder_name.strip().lower():
            return path
    raise KeyError(f"Unknown folder name '{folder_name}'.")


def is_path_within_sandbox(
    candidate_path: str | Path, resolution: SandboxResolution | None = None, env_override: Mapping[str, str | None] | None = None
) -> bool:
    """Returns True iff sandbox is active and both logical and resolved paths stay within sandbox/drive roots."""
    res = resolution if resolution is not None else resolve_sandbox_environment(env_override)
    if not res.is_sandbox_active:
        return False
    allowed = tuple(dict.fromkeys(r for r in (
        res.sandbox_root, res.system_drive_root, res.storage_drive_root,
        res.removable_drive_root, res.disc_drive_root, res.sandbox_profile_dir, *res.backing_roots,
    ) if r is not None))
    logical = _normalize_path(candidate_path)
    if not any(_is_same_or_child(logical, root) for root in allowed):
        return False
    return any(_is_same_or_child(_resolve_backing_path(logical, get_subst_mappings()), root) for root in allowed)


def assert_sandbox_path_if_active(
    candidate_path: str | Path, resolution: SandboxResolution | None = None, env_override: Mapping[str, str | None] | None = None
) -> Path:
    """When sandbox is active, fails closed (exit_code=3) if candidate_path escapes sandbox roots."""
    res = resolution if resolution is not None else resolve_sandbox_environment(env_override)
    normalized = _normalize_path(candidate_path)
    if res.is_sandbox_active and not is_path_within_sandbox(normalized, res):
        raise SandboxContainmentError(f"Fail-Closed Sandbox Containment Violation: '{normalized}' escapes '{res.sandbox_root}'.")
    return normalized


def map_host_path_to_sandbox(
    host_path: str | Path, resolution: SandboxResolution | None = None, env_override: Mapping[str, str | None] | None = None
) -> Path:
    """Maps a host known-folder path into the active sandbox, or returns it normalized when inactive."""
    res = resolution if resolution is not None else resolve_sandbox_environment(env_override)
    normalized = _normalize_path(host_path)
    if not res.is_sandbox_active or is_path_within_sandbox(normalized, res):
        return normalized
    host_folders = _host_known_folders(os.environ if env_override is None else env_override)
    sandbox_folders = resolve_known_folders(res, env_override)
    for key in ("Startup", "Programs", "StartMenu", "LocalApplicationData", "ApplicationData", "DesktopDirectory", "MyDocuments", "ProgramFilesX86", "ProgramFiles", "CommonApplicationData", "Windows", "UserProfile"):
        host_base = host_folders[key]
        if _is_same_or_child(normalized, host_base):
            rel_parts = normalized.parts[len(host_base.parts) :]
            return sandbox_folders[key].joinpath(*rel_parts) if rel_parts else sandbox_folders[key]
    raise SandboxContainmentError(f"Fail-Closed Sandbox Containment Violation: cannot map '{normalized}' into active sandbox.")
