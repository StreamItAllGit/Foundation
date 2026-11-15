# @module: foundation_core.cli
# @purpose: Standard 02 & Standard 10 CLI flag parser (--data-dir, --config, --log-dir, --test-mode, --version, --help) with fail-closed sandbox checks.
# @depends-on: argparse, dataclasses, pathlib
# @exports: StandardCliOptions, parse_standard_cli_flags, assert_path_contained

from __future__ import annotations

import argparse
from dataclasses import dataclass
import os
from pathlib import Path
from typing import Sequence


def assert_path_contained(candidate_path: str | Path, allowed_root: str | Path) -> Path:
    """Fail-closed containment check ensuring candidate_path resolves strictly inside allowed_root."""
    resolved_candidate = Path(candidate_path).resolve()
    resolved_root = Path(allowed_root).resolve()
    if resolved_candidate != resolved_root and resolved_root not in resolved_candidate.parents:
        raise ValueError(
            f"Fail-Closed Sandbox Violation: '{resolved_candidate}' escapes allowed root '{resolved_root}'."
        )
    return resolved_candidate


@dataclass(frozen=True)
class StandardCliOptions:
    data_dir: Path
    config_path: Path
    log_dir: Path
    test_mode: bool
    extra_args: list[str]
    headless: bool = False
    visible_test: bool = False
    effective_visible_test: bool = False


def _is_truthy_env(name: str) -> bool:
    raw = os.environ.get(name, "").strip().lower()
    return raw in {"1", "true", "yes", "on"}


def parse_standard_cli_flags(
    argv: Sequence[str] | None = None,
    default_root: str | Path = ".",
    app_name: str = "Application",
    app_version: str = "0.1.0",
) -> StandardCliOptions:
    base = Path(default_root).resolve()
    parser = argparse.ArgumentParser(prog=app_name, add_help=True)
    parser.add_argument("--version", action="version", version=f"{app_name} v{app_version}")
    parser.add_argument("--data-dir", type=Path, default=base / "data", help="Isolated data directory")
    parser.add_argument("--config", type=Path, default=None, help="Explicit primary config file path")
    parser.add_argument("--log-dir", type=Path, default=base / "logs", help="Isolated log directory")
    parser.add_argument("--test-mode", action="store_true", help="Enable fail-closed sandbox test mode")
    parser.add_argument("--headless", action="store_true", help="Force headless execution without visible GUI")
    parser.add_argument("--visible-test", action="store_true", help="Show visible foreground test window")

    known, unknown = parser.parse_known_args(argv)
    data_dir = Path(known.data_dir).resolve()
    log_dir = Path(known.log_dir).resolve()
    config_path = (
        Path(known.config).resolve()
        if known.config is not None
        else (data_dir / "app.settings.json").resolve()
    )

    prefix = "".join(ch if ch.isalnum() else "_" for ch in app_name.upper()).strip("_")
    test_mode = bool(known.test_mode) or (bool(prefix) and _is_truthy_env(f"{prefix}_TEST_MODE")) or _is_truthy_env("TEST_MODE")
    headless = bool(known.headless) or (bool(prefix) and _is_truthy_env(f"{prefix}_HEADLESS")) or _is_truthy_env("HEADLESS")
    explicit_visible = bool(known.visible_test) or (bool(prefix) and _is_truthy_env(f"{prefix}_VISIBLE_TEST")) or _is_truthy_env("VISIBLE_TEST")
    visible_test = explicit_visible or (test_mode and not headless)
    effective_visible_test = visible_test and not headless

    return StandardCliOptions(
        data_dir=data_dir,
        config_path=config_path,
        log_dir=log_dir,
        test_mode=test_mode,
        extra_args=list(unknown),
        headless=headless,
        visible_test=visible_test,
        effective_visible_test=effective_visible_test,
    )
