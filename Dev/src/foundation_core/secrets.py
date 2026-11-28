# @module: foundation_core.secrets
# @purpose: Pure-stdlib Windows DPAPI secret vault wire-compatible with C# Foundation.Core.DpapiSecretStore (data/secrets.dpapi).
# @depends-on: contextlib, ctypes, json, os, pathlib, threading, typing
# @exports: DPAPI_ENTROPY, DpapiDecryptError, DpapiSecretStore

from __future__ import annotations

from contextlib import suppress
import ctypes
from ctypes import wintypes
import json
import os
from pathlib import Path
import threading
from typing import Any

DPAPI_ENTROPY: bytes = b"Foundation.Core.DpapiSecretStore.v1"


class DpapiDecryptError(RuntimeError):
    """Raised when secrets.dpapi cannot be decrypted by the current Windows user or contains corrupt cipher/JSON."""


if os.name == "nt":
    class _DataBlob(ctypes.Structure):
        _fields_ = [
            ("cbData", wintypes.DWORD),
            ("pbData", ctypes.POINTER(ctypes.c_ubyte)),
        ]


def _protect_bytes(plain: bytes) -> bytes:
    if os.name != "nt":
        return plain

    crypt32 = ctypes.WinDLL("crypt32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.LocalFree.argtypes = [ctypes.c_void_p]
    kernel32.LocalFree.restype = ctypes.c_void_p

    in_buf = ctypes.create_string_buffer(plain, len(plain))
    ent_buf = ctypes.create_string_buffer(DPAPI_ENTROPY, len(DPAPI_ENTROPY))
    in_blob = _DataBlob(len(plain), ctypes.cast(in_buf, ctypes.POINTER(ctypes.c_ubyte)))
    ent_blob = _DataBlob(len(DPAPI_ENTROPY), ctypes.cast(ent_buf, ctypes.POINTER(ctypes.c_ubyte)))
    out_blob = _DataBlob()

    ok = crypt32.CryptProtectData(
        ctypes.byref(in_blob),
        None,
        ctypes.byref(ent_blob),
        None,
        None,
        0,  # CurrentUser scope (matches C# DataProtectionScope.CurrentUser)
        ctypes.byref(out_blob),
    )
    if not ok:
        err = ctypes.get_last_error()
        raise OSError(err, f"CryptProtectData failed (Win32 error {err}).")
    try:
        return ctypes.string_at(out_blob.pbData, out_blob.cbData)
    finally:
        if out_blob.pbData:
            kernel32.LocalFree(out_blob.pbData)


def _unprotect_bytes(cipher: bytes) -> bytes:
    if os.name != "nt":
        return cipher

    crypt32 = ctypes.WinDLL("crypt32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.LocalFree.argtypes = [ctypes.c_void_p]
    kernel32.LocalFree.restype = ctypes.c_void_p

    in_buf = ctypes.create_string_buffer(cipher, len(cipher))
    ent_buf = ctypes.create_string_buffer(DPAPI_ENTROPY, len(DPAPI_ENTROPY))
    in_blob = _DataBlob(len(cipher), ctypes.cast(in_buf, ctypes.POINTER(ctypes.c_ubyte)))
    ent_blob = _DataBlob(len(DPAPI_ENTROPY), ctypes.cast(ent_buf, ctypes.POINTER(ctypes.c_ubyte)))
    out_blob = _DataBlob()

    ok = crypt32.CryptUnprotectData(
        ctypes.byref(in_blob),
        None,
        ctypes.byref(ent_blob),
        None,
        None,
        0,
        ctypes.byref(out_blob),
    )
    if not ok:
        err = ctypes.get_last_error()
        raise DpapiDecryptError(
            f"Unable to decrypt secrets vault with current Windows user credentials (Win32 error {err})."
        )
    try:
        return ctypes.string_at(out_blob.pbData, out_blob.cbData)
    finally:
        if out_blob.pbData:
            kernel32.LocalFree(out_blob.pbData)


class DpapiSecretStore:
    """Thread-safe DPAPI-encrypted key-value store stored atomically in data/secrets.dpapi."""

    def __init__(self, data_dir: str | Path, filename: str = "secrets.dpapi") -> None:
        raw_dir = str(data_dir).strip() if data_dir is not None else ""
        if not raw_dir:
            raise ValueError("data_dir must not be empty.")
        if not filename or not filename.strip():
            raise ValueError("filename must not be empty.")
        full_dir = Path(raw_dir).resolve()
        full_dir.mkdir(parents=True, exist_ok=True)
        self.secrets_file_path: Path = full_dir / filename.strip()
        self._lock = threading.Lock()

    def set_secret(self, key: str, secret_value: str | None) -> None:
        norm_key = (key or "").strip()
        if not norm_key:
            raise ValueError("key must not be empty.")
        with self._lock:
            secrets_map = self._load_dict_unsynced()
            self._remove_ci(secrets_map, norm_key)
            if secret_value is not None and str(secret_value).strip():
                secrets_map[norm_key] = str(secret_value)
            self._save_dict_unsynced(secrets_map)

    def get_secret(self, key: str | None) -> str | None:
        if not key or not key.strip():
            return None
        target = key.strip().lower()
        with self._lock:
            secrets_map = self._load_dict_unsynced()
            for k, v in secrets_map.items():
                if k.lower() == target:
                    return v
            return None

    def remove_secret(self, key: str | None) -> bool:
        if not key or not key.strip():
            return False
        norm_key = key.strip()
        with self._lock:
            secrets_map = self._load_dict_unsynced()
            removed = self._remove_ci(secrets_map, norm_key)
            if removed:
                self._save_dict_unsynced(secrets_map)
            return removed

    def get_secret_keys(self) -> list[str]:
        with self._lock:
            secrets_map = self._load_dict_unsynced()
            return list(secrets_map.keys())

    @staticmethod
    def _remove_ci(secrets_map: dict[str, str], target_key: str) -> bool:
        lower = target_key.lower()
        matches = [k for k in secrets_map if k.lower() == lower]
        for k in matches:
            secrets_map.pop(k, None)
        return len(matches) > 0

    def _load_dict_unsynced(self) -> dict[str, str]:
        if not self.secrets_file_path.exists():
            return {}
        cipher = self.secrets_file_path.read_bytes()
        if not cipher:
            return {}
        plain = _unprotect_bytes(cipher)
        try:
            decoded: Any = json.loads(plain.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as exc:
            raise DpapiDecryptError("Decrypted secrets vault is not valid UTF-8 JSON.") from exc
        if not isinstance(decoded, dict):
            raise DpapiDecryptError("Decrypted secrets vault must decode to a JSON object.")
        return {str(k): str(v) for k, v in decoded.items() if v is not None}

    def _save_dict_unsynced(self, secrets_map: dict[str, str]) -> None:
        payload_json = json.dumps(secrets_map, ensure_ascii=False)
        cipher = _protect_bytes(payload_json.encode("utf-8"))
        tmp_path = self.secrets_file_path.with_name(
            f"{self.secrets_file_path.name}.{os.getpid()}.{threading.get_ident()}.tmp"
        )
        try:
            with tmp_path.open("wb") as fh:
                fh.write(cipher)
                fh.flush()
                os.fsync(fh.fileno())
            os.replace(tmp_path, self.secrets_file_path)
        finally:
            with suppress(OSError):
                tmp_path.unlink(missing_ok=True)
