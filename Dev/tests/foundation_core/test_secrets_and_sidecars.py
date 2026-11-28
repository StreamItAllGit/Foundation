# @module: Foundation.Tests.Python.SecretsAndSidecars
# @purpose: Unit and C#<->Python interop tests for foundation_core.secrets (DPAPI) and foundation_core.sidecars (Standard 44).
# @depends-on: hashlib, json, os, pathlib, tempfile, unittest, zlib, foundation_core
# @exports: SecretsAndSidecarsTests

from __future__ import annotations

import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
import zlib

SRC_DIR = Path(__file__).resolve().parents[2] / "src"
if str(SRC_DIR) not in sys.path:
    sys.path.insert(0, str(SRC_DIR))

from foundation_core import (  # noqa: E402
    DPAPI_ENTROPY,
    DirectoryHkLock,
    DpapiDecryptError,
    DpapiSecretStore,
    HashFilePlacement,
    HashSidecarEntry,
    compute_composite_folder_sha1,
    compute_crc32_hex,
    compute_file_hashes,
    format_hash_sidecar,
    get_hash_sidecar_path,
    has_active_foreign_lock_in_directory,
    is_active_foreign_hklock,
    is_temporary_or_partial_filename,
    normalize_relative_paths_stripping_single_wrapper,
    parse_hash_sidecar,
    read_archive_hashes_json,
    read_hash_sidecar,
    release_directory_hklock,
    try_acquire_directory_hklock,
    utc_iso_ms,
    write_archive_hashes_json_atomic,
    write_hash_sidecar_atomic,
)


class SecretsAndSidecarsTests(unittest.TestCase):
    def test_dpapi_secret_store_round_trip_case_insensitive_and_no_clobber_on_corrupt(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            data_dir = Path(tmp).resolve()
            store = DpapiSecretStore(data_dir)
            self.assertEqual(DPAPI_ENTROPY, b"Foundation.Core.DpapiSecretStore.v1")

            store.set_secret("DiscordWebhook", "https://example.invalid/hook")
            self.assertTrue(store.secrets_file_path.exists())
            raw_bytes = store.secrets_file_path.read_bytes()
            if os.name == "nt":
                self.assertNotIn(b"DiscordWebhook", raw_bytes)
                self.assertNotIn(b"example.invalid", raw_bytes)

            self.assertEqual(store.get_secret("discordwebhook"), "https://example.invalid/hook")
            self.assertEqual(store.get_secret_keys(), ["DiscordWebhook"])
            self.assertTrue(store.remove_secret("DISCORDWEBHOOK"))
            self.assertIsNone(store.get_secret("DiscordWebhook"))

            corrupt_bytes = b"\x01\x02\x03not-a-valid-dpapi-blob"
            store.secrets_file_path.write_bytes(corrupt_bytes)
            with self.assertRaises(DpapiDecryptError):
                _ = store.get_secret("DiscordWebhook")
            self.assertEqual(store.secrets_file_path.read_bytes(), corrupt_bytes)

    def test_crc32_and_single_pass_hashes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp).resolve()
            sample = root / "vector.bin"
            sample.write_bytes(b"123456789")
            self.assertEqual(compute_crc32_hex(sample), "CBF43926")

            digest = compute_file_hashes(sample)
            self.assertEqual(digest.size_bytes, 9)
            self.assertEqual(digest.crc32, "CBF43926")
            self.assertEqual(digest.md5, "25F9E794323B453885F5181F1B624D0B")
            self.assertEqual(digest.sha1, "F7C3BC1D808E04732ADF679965CCC34CA7AE3441")
            self.assertEqual(digest.sha256, "15E2B0D3C33891EBB0F1EF609EC419420C20E320CE94C65FBC8C3312448EB225")

    def test_hash_sidecar_format_parse_wrapper_strip_and_archive_hashes_json(self) -> None:
        self.assertEqual(int(HashFilePlacement.SidecarBeside), 1)
        entries = [
            HashSidecarEntry("GameFolder/disc.cue", 42, "11223344", "AA" * 16, "BB" * 20),
            HashSidecarEntry("GameFolder/track01.bin", 1024, "55667788", "CC" * 16, "DD" * 20),
        ]
        stripped = normalize_relative_paths_stripping_single_wrapper(entries)
        self.assertEqual([e.relative_path for e in stripped], ["disc.cue", "track01.bin"])

        ps3_entries = [
            HashSidecarEntry("PS3_GAME/USRDIR/EBOOT.BIN", 100, "AAAAAAAA"),
            HashSidecarEntry("PS3_GAME/PARAM.SFO", 200, "BBBBBBBB"),
        ]
        self.assertEqual(
            [e.relative_path for e in normalize_relative_paths_stripping_single_wrapper(ps3_entries)],
            ["PS3_GAME/USRDIR/EBOOT.BIN", "PS3_GAME/PARAM.SFO"],
        )

        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp).resolve()
            content = format_hash_sidecar(
                "Sample Game",
                entries,
                container=("Sample.zip", 500, "DEADBEEF", "11" * 16, "22" * 20),
            )
            sidecar_path = get_hash_sidecar_path(root / "Sample.zip")
            write_hash_sidecar_atomic(sidecar_path, content)
            parsed = read_hash_sidecar(sidecar_path, "Sample.zip")
            self.assertIsNotNone(parsed)
            assert parsed is not None
            self.assertEqual(parsed.container_crc32, "DEADBEEF")
            self.assertEqual(len(parsed.files), 2)
            self.assertEqual(parsed.files[0].relative_path, "disc.cue")

            comp_sha = compute_composite_folder_sha1(ps3_entries)
            comp_txt = format_hash_sidecar("PS3 Game", ps3_entries, composite_sha1=comp_sha)
            comp_doc = parse_hash_sidecar(comp_txt)
            self.assertIsNotNone(comp_doc)
            assert comp_doc is not None
            self.assertTrue(comp_doc.is_composite_folder)
            self.assertEqual(comp_doc.composite_sha1, comp_sha)

            json_path = root / ".archive_hashes.json"
            write_archive_hashes_json_atomic(json_path, {"Sample.zip": {"crc32": "DEADBEEF"}})
            self.assertEqual(read_archive_hashes_json(json_path), {"Sample.zip": {"crc32": "DEADBEEF"}})

    def test_hklock_canonical_write_legacy_csharp_read_and_temp_filters(self) -> None:
        self.assertTrue(is_temporary_or_partial_filename("game.zip.part"))
        self.assertTrue(is_temporary_or_partial_filename("movie.partial_1234"))
        self.assertTrue(is_temporary_or_partial_filename("~$notes.docx"))
        self.assertFalse(is_temporary_or_partial_filename("ChronoTrigger.sfc"))

        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp).resolve()
            with DirectoryHkLock(root, "Redroom") as lock_ctx:
                self.assertIsNotNone(lock_ctx.lock_path)
                assert lock_ctx.lock_path is not None
                envelope = json.loads(lock_ctx.lock_path.read_text(encoding="utf-8"))
                # Must write canonical Standard 18/31 fields via PidSentinelLock.
                self.assertEqual(envelope["appId"], "redroom")
                self.assertEqual(envelope["pid"], os.getpid())
                self.assertIn("hostName", envelope)
                self.assertIn("targetPath", envelope)

                # Same process does not treat its own lock as foreign.
                active_self, _, _ = is_active_foreign_hklock(lock_ctx.lock_path)
                self.assertFalse(active_self)

            # Simulate legacy C# HkLockEnvelope from another dead/recycled PID vs remote machine.
            foreign_lock = root / ".housekeeping_999999.hklock"
            foreign_lock.write_text(
                json.dumps(
                    {
                        "SchemaVersion": 1,
                        "MachineName": "REMOTE-HOST-OTHER",
                        "ProcessId": 4242,
                        "StartedAtUtc": utc_iso_ms(),
                        "HeartbeatAtUtc": utc_iso_ms(),
                        "TargetFilePath": str(root),
                    }
                ),
                encoding="utf-8",
            )
            has_foreign, reason, parsed = has_active_foreign_lock_in_directory(root, honor_hk_locks=True)
            self.assertTrue(has_foreign, reason)
            self.assertIsNotNone(parsed)
            assert parsed is not None
            self.assertEqual(parsed["hostName"], "REMOTE-HOST-OTHER")
            self.assertEqual(parsed["pid"], 4242)

            acquired, _, conflict = try_acquire_directory_hklock(root, "redroom", honor_hk_locks=True)
            self.assertFalse(acquired)
            self.assertIsNotNone(conflict)
            release_directory_hklock(foreign_lock)
            self.assertFalse(foreign_lock.exists())


if __name__ == "__main__":
    unittest.main()
