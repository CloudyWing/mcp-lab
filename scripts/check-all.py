#!/usr/bin/env python3
"""Run layered project checks."""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]


def main() -> int:
    parser = argparse.ArgumentParser(description="Run MCP Lab checks by layer.")
    parser.add_argument(
        "--skip-dotnet",
        action="store_true",
        help="Skip `dotnet test`.",
    )
    parser.add_argument(
        "--smoke",
        action="store_true",
        help="Run Docker-backed smoke tests after unit tests.",
    )
    parser.add_argument(
        "--include-oracle",
        action="store_true",
        help="Pass --include-oracle to the smoke test.",
    )
    parser.add_argument(
        "--build-services",
        action="store_true",
        help="Build Docker services before smoke tests.",
    )
    args = parser.parse_args()

    commands: list[list[str]] = []

    if not args.skip_dotnet:
        commands.append(["dotnet", "test"])

    if args.build_services:
        commands.append(["docker", "compose", "build"])

    if args.smoke:
        smoke = [sys.executable, "scripts/smoke-test.py"]

        if args.include_oracle:
            smoke.append("--include-oracle")

        commands.append(smoke)

    for command in commands:
        print(f"[check] {' '.join(command)}", flush=True)
        completed = subprocess.run(command, cwd=REPO_ROOT, check=False)

        if completed.returncode != 0:
            return completed.returncode

    return 0


if __name__ == "__main__":
    sys.exit(main())
