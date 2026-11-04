#!/usr/bin/env python3
"""Run the integration tests: start the MCP stack with test backends, then run the smoke test
and the admin UI end-to-end test against it.

The script writes the root .env and config/<service>/service.env, so it refuses to run when they
already exist unless --force is given. It is meant for CI and for a clean checkout.
"""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
TEST_ROOT = REPO_ROOT / "tests" / "integration"
CONFIG_ROOT = REPO_ROOT / "config"
ADMIN_PASSWORD = "integration-admin-password"


def compose(args: argparse.Namespace) -> list[str]:
    files = ["-f", "compose.yml", "-f", str(TEST_ROOT / "compose.test.yml")]
    for extra in args.compose_file:
        files += ["-f", extra]
    return ["docker", "compose", "--profile", "docker", *files]


def run(command: list[str], **kwargs) -> None:
    print(f"[integration] {' '.join(command)}", flush=True)
    subprocess.run(command, cwd=REPO_ROOT, check=True, **kwargs)


def prepare(args: argparse.Namespace) -> None:
    env_path = REPO_ROOT / ".env"
    existing = [path for path in (env_path, *CONFIG_ROOT.glob("*/service.env")) if path.exists()]
    if existing and not args.force:
        sys.exit(
            "Refusing to overwrite existing settings: "
            + ", ".join(str(path.relative_to(REPO_ROOT)) for path in existing[:3])
            + ". Run in a clean checkout or pass --force."
        )

    lines = [
        "COMPOSE_PROJECT_NAME=mcp-lab",
        "TZ=UTC",
        f"MCPLAB_RUNTIME_UID={os.getuid()}",
        f"MCPLAB_RUNTIME_GID={os.getgid()}",
        f"DOCUMENT_HOST_MOUNT_ROOT={TEST_ROOT / 'docs'}",
        f"MCPLAB_ADMIN_PASSWORD={ADMIN_PASSWORD}",
        "MCP_SMOKE_MAILPIT_SMTP_HOST=127.0.0.1",
        "MCP_SMOKE_MAILPIT_SMTP_PORT=1025",
        "MCP_SMOKE_READER_SAMPLES=.",
        "MCP_SMOKE_TEST_BACKENDS=1",
        *args.env,
    ]
    env_path.write_text("\n".join(lines) + "\n", encoding="utf-8")

    for source in sorted((TEST_ROOT / "config").glob("*/service.env")):
        target = CONFIG_ROOT / source.parent.name / "service.env"
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
        target.chmod(0o600)

    make_certificate()

    # Start from an empty admin history so the end-to-end test sees only its own changes.
    shutil.rmtree(CONFIG_ROOT / ".admin", ignore_errors=True)


def make_certificate() -> None:
    """Creates the self-signed certificate that the TLS listeners of the test brokers use."""
    certs = TEST_ROOT / ".certs"
    certs.mkdir(exist_ok=True)
    run([
        "openssl", "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "2",
        "-subj", "/CN=mcp-lab-test",
        "-addext", "subjectAltName=DNS:test-redis,DNS:test-mqtt,DNS:test-rabbit",
        "-keyout", str(certs / "server.key"), "-out", str(certs / "server.crt"),
    ], capture_output=True)
    # The brokers run as their own users; the key is throwaway, so let them read it.
    for path in certs.iterdir():
        path.chmod(0o644)


def main() -> int:
    parser = argparse.ArgumentParser(description="Run MCP Lab integration tests against throwaway test backends.")
    parser.add_argument("--force", action="store_true", help="Overwrite an existing .env and config/<service>/service.env.")
    parser.add_argument("--no-build", action="store_true", help="Use images that are already built.")
    parser.add_argument("--keep", action="store_true", help="Leave the containers running afterwards.")
    parser.add_argument("--skip-e2e", action="store_true", help="Skip the admin UI end-to-end test.")
    parser.add_argument("--compose-file", action="append", default=[], help="Extra compose file, for local overrides.")
    parser.add_argument("--env", action="append", default=[], help="Extra KEY=VALUE line for the root .env.")
    args = parser.parse_args()

    prepare(args)
    failed = False

    try:
        up = [*compose(args), "up", "-d", "--wait", "--wait-timeout", "600"]
        run(up if args.no_build else [*up, "--build"])
        run([sys.executable, "scripts/smoke-test.py", "--check-hot-reload"])

        if not args.skip_e2e:
            e2e = TEST_ROOT.parent / "admin-e2e"
            run(
                ["node", str(e2e / "admin.e2e.cjs")],
                env={**os.environ, "MCPLAB_ADMIN_URL": "http://127.0.0.1:9890", "MCPLAB_ADMIN_PASSWORD": ADMIN_PASSWORD},
            )
    except subprocess.CalledProcessError:
        failed = True
        subprocess.run([*compose(args), "ps", "-a"], cwd=REPO_ROOT, check=False)
        subprocess.run([*compose(args), "logs", "--tail", "40"], cwd=REPO_ROOT, check=False)
    finally:
        if not args.keep:
            subprocess.run([*compose(args), "down", "-v", "--remove-orphans"], cwd=REPO_ROOT, check=False)

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
