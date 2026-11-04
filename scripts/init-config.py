#!/usr/bin/env python3
"""Create per-service runtime config files under config/<service>/service.env.

Each MCP container mounts only its own config/<service> directory, so a service
cannot read another service's credentials. Settings are taken from the root .env
(migration) or, when .env has none for a service, from config/services.env.example.
"""

from __future__ import annotations

import argparse
import os
import re
import shutil
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
CONFIG_ROOT = REPO_ROOT / "config"
EXAMPLE_PATH = CONFIG_ROOT / "services.env.example"
ROOT_ENV_PATH = REPO_ROOT / ".env"
SERVICE_FILE_NAME = "service.env"

# Keys copied into every service file.
SHARED_KEYS = re.compile(r"^MCPLAB_MCP_TOKEN$")

SERVICE_KEYS: dict[str, re.Pattern[str]] = {
    "mcp-api-contract": re.compile(r"^API_CONTRACT_"),
    "mcp-sql-server": re.compile(r"^(MSSQL_|QUERY_TIMEOUT$|MAX_ROWS$)"),
    "mcp-oracle": re.compile(r"^(ORACLE_|QUERY_TIMEOUT$|MAX_ROWS$)"),
    "mcp-elasticsearch": re.compile(r"^(ES_|MAX_ROWS$)"),
    "mcp-redis": re.compile(r"^(REDIS_|MAX_ROWS$)"),
    "mcp-oidc": re.compile(r"^OIDC_"),
    "mcp-mosquitto": re.compile(r"^MQTT_"),
    "mcp-rabbitmq": re.compile(r"^RABBITMQ_"),
    "mcp-mailpit": re.compile(r"^MAILPIT_"),
    "mcp-reader": re.compile(r"^(DOCUMENT_(?!HOST_MOUNT_)|MAX_IMAGES$|MAX_TEXT_MB$)"),
    "mcp-docker": re.compile(r"^$"),
}

KEY_LINE = re.compile(r"^\s*(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=")


def parse_lines(path: Path) -> list[str]:
    if not path.exists():
        return []

    return path.read_text(encoding="utf-8-sig").splitlines()


def line_key(line: str) -> str | None:
    match = KEY_LINE.match(line)
    return match.group(1) if match else None


def belongs_to(service: str, key: str) -> bool:
    return bool(SERVICE_KEYS[service].search(key) or SHARED_KEYS.search(key))


def is_service_key(key: str) -> bool:
    return any(belongs_to(service, key) for service in SERVICE_KEYS)


def select_lines(lines: list[str], service: str) -> list[str]:
    """Keeps the service's key lines plus the comments directly above them."""
    selected: list[str] = []
    pending_comments: list[str] = []

    for line in lines:
        stripped = line.strip()

        if not stripped:
            pending_comments = []
            continue

        if stripped.startswith("#"):
            commented_key = line_key(stripped.lstrip("#").strip())
            if commented_key and belongs_to(service, commented_key):
                selected.extend(pending_comments)
                pending_comments = []
                selected.append(line)
            else:
                pending_comments.append(line)
            continue

        key = line_key(line)
        if key and belongs_to(service, key):
            if pending_comments and selected:
                selected.append("")
            selected.extend(pending_comments)
            selected.append(line)

        pending_comments = []

    return selected


def write_private(path: Path, lines: list[str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp_path = path.with_name(f".{path.name}.tmp")
    temp_path.write_text("\n".join(lines).rstrip("\n") + "\n", encoding="utf-8")

    try:
        os.chmod(temp_path, 0o600)
    except OSError:
        pass

    os.replace(temp_path, path)


def strip_root_env() -> list[str]:
    lines = parse_lines(ROOT_ENV_PATH)
    kept: list[str] = []
    removed: list[str] = []

    for line in lines:
        key = line_key(line)
        if key and is_service_key(key):
            removed.append(key)
        else:
            kept.append(line)

    if removed:
        shutil.copy2(ROOT_ENV_PATH, ROOT_ENV_PATH.with_name(".env.bak"))
        write_private(ROOT_ENV_PATH, kept)

    return removed


def main() -> int:
    parser = argparse.ArgumentParser(description="Create config/<service>/service.env files for each MCP service.")
    parser.add_argument("--force", action="store_true", help="Overwrite existing service.env files.")
    parser.add_argument(
        "--strip-env",
        action="store_true",
        help="Remove migrated service settings from the root .env (a copy is kept as .env.bak).",
    )
    parser.add_argument("--dry-run", action="store_true", help="Show what would be written without changing files.")
    args = parser.parse_args()

    root_lines = parse_lines(ROOT_ENV_PATH)
    example_lines = parse_lines(EXAMPLE_PATH)

    for service in SERVICE_KEYS:
        target = CONFIG_ROOT / service / SERVICE_FILE_NAME
        from_env = [line for line in select_lines(root_lines, service) if not line.lstrip().startswith("#")]
        source_name, lines = (".env", select_lines(root_lines, service)) if from_env else (
            EXAMPLE_PATH.name,
            select_lines(example_lines, service),
        )

        if target.exists() and not args.force:
            print(f"[skip] {target.relative_to(REPO_ROOT)} already exists")
            continue

        print(f"[write] {target.relative_to(REPO_ROOT)} from {source_name} ({sum(1 for l in lines if line_key(l))} keys)")
        if not args.dry_run:
            write_private(target, lines)

    if args.strip_env and not args.dry_run:
        removed = strip_root_env()
        print(f"[strip] removed {len(removed)} service keys from .env (backup: .env.bak)")

    return 0


if __name__ == "__main__":
    sys.exit(main())
