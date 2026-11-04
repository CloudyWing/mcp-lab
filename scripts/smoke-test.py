#!/usr/bin/env python3
"""Run local Docker-backed smoke tests for the MCP services."""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import re
import smtplib
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid
from dataclasses import dataclass
from email.message import EmailMessage
from http.client import HTTPMessage
from pathlib import Path
from typing import Any


PROTOCOL_VERSION = "2025-06-18"
REPO_ROOT = Path(__file__).resolve().parents[1]

EXPECTED_SERVICES = [
    "mcp-api-contract",
    "mcp-sql-server",
    "mcp-oracle",
    "mcp-elasticsearch",
    "mcp-redis",
    "mcp-oidc",
    "mcp-mosquitto",
    "mcp-rabbitmq",
    "mcp-mailpit",
    "mcp-reader",
    "mcp-docker",
]
# Started only with `docker compose --profile docker up`; skipped when absent.
OPTIONAL_SERVICES = {"mcp-docker"}
# The admin UI is checked separately: it mounts the whole config directory and is not an MCP server.
ADMIN_SERVICE = "mcp-admin"


class SmokeError(Exception):
    """Represents a failed smoke-test assertion."""


class SkipCheck(Exception):
    """Represents an intentionally skipped smoke-test check."""


@dataclass(frozen=True)
class ToolResult:
    text: str
    raw: dict[str, Any]
    is_error: bool = False


class McpClient:
    def __init__(self, url: str, timeout_seconds: int) -> None:
        self.url = url
        self.timeout_seconds = timeout_seconds
        self.session_id: str | None = None
        self.next_id = 1

    def initialize(self) -> None:
        payload = {
            "jsonrpc": "2.0",
            "id": self._next_id(),
            "method": "initialize",
            "params": {
                "protocolVersion": PROTOCOL_VERSION,
                "capabilities": {},
                "clientInfo": {
                    "name": "mcp-lab-smoke-test",
                    "version": "1.0.0",
                },
            },
        }
        _, headers = self._post(payload, include_session=False)
        self.session_id = get_header(headers, "mcp-session-id")

        if not self.session_id:
            raise SmokeError(f"{self.url} did not return an MCP session id.")

        self._post(
            {
                "jsonrpc": "2.0",
                "method": "notifications/initialized",
            },
            include_session=True,
        )

    def call_tool(self, name: str, arguments: dict[str, Any] | None = None) -> ToolResult:
        message, _ = self._post(
            {
                "jsonrpc": "2.0",
                "id": self._next_id(),
                "method": "tools/call",
                "params": {
                    "name": name,
                    "arguments": arguments or {},
                },
            },
            include_session=True,
        )

        if message is None:
            raise SmokeError(f"{self.url} returned an empty response for tool {name}.")

        if "error" in message:
            raise SmokeError(f"{name} failed: {json.dumps(message['error'], ensure_ascii=False)}")

        result = message.get("result")
        if not isinstance(result, dict):
            raise SmokeError(f"{name} returned an invalid MCP result.")

        return ToolResult(text=extract_tool_text(result), raw=result, is_error=result.get("isError") is True)

    def _next_id(self) -> int:
        current_id = self.next_id
        self.next_id += 1
        return current_id

    def _post(
        self,
        payload: dict[str, Any],
        include_session: bool,
    ) -> tuple[dict[str, Any] | None, HTTPMessage]:
        headers = {
            "Accept": "application/json, text/event-stream",
            "Content-Type": "application/json",
            "Mcp-Protocol-Version": PROTOCOL_VERSION,
        }

        token = read_setting("MCPLAB_MCP_TOKEN")
        if token:
            headers["Authorization"] = f"Bearer {token}"

        if include_session:
            if not self.session_id:
                raise SmokeError("MCP session has not been initialized.")

            headers["Mcp-Session-Id"] = self.session_id

        request = urllib.request.Request(
            self.url,
            data=json.dumps(payload).encode("utf-8"),
            headers=headers,
            method="POST",
        )

        try:
            with urllib.request.urlopen(request, timeout=self.timeout_seconds) as response:
                body = response.read().decode("utf-8")
                return parse_mcp_response(body), response.headers
        except urllib.error.HTTPError as exc:
            body = exc.read().decode("utf-8", errors="replace")
            raise SmokeError(f"{self.url} returned HTTP {exc.code}: {body[:1000]}") from exc
        except urllib.error.URLError as exc:
            raise SmokeError(f"{self.url} is not reachable: {exc.reason}") from exc


class Runner:
    def __init__(self) -> None:
        self.passed = 0
        self.skipped = 0
        self.failed = 0

    def check(self, name: str, action: Any) -> None:
        try:
            action()
            self.passed += 1
            print(f"[PASS] {name}")
        except SkipCheck as exc:
            self.skipped += 1
            print(f"[SKIP] {name}: {exc}")
        except Exception as exc:
            self.failed += 1
            print(f"[FAIL] {name}: {exc}")

    def finish(self) -> int:
        print(f"Summary: passed={self.passed} skipped={self.skipped} failed={self.failed}")
        return 1 if self.failed else 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Run Docker-backed MCP smoke tests.")
    parser.add_argument(
        "--include-oracle",
        action="store_true",
        default=is_truthy(os.environ.get("MCP_SMOKE_INCLUDE_ORACLE")),
        help="Run the Oracle query checks. This requires a reachable Oracle database in .env.",
    )
    parser.add_argument(
        "--timeout-seconds",
        type=int,
        default=int(os.environ.get("MCP_SMOKE_TIMEOUT_SECONDS", "15")),
        help="Per-request timeout for MCP calls.",
    )
    parser.add_argument(
        "--check-hot-reload",
        action="store_true",
        help=(
            "Temporarily edit config/mcp-sql-server/service.env and verify list_connections reflects "
            "changed, added, and removed connections without a restart."
        ),
    )
    args = parser.parse_args()

    runner = Runner()
    status_by_service = load_compose_status()
    global EXPECTED_SERVICES
    EXPECTED_SERVICES = [
        service
        for service in EXPECTED_SERVICES
        if service not in OPTIONAL_SERVICES or service in status_by_service
    ]

    runner.check("compose services are running and healthy", lambda: check_compose_health(status_by_service))
    runner.check("admin UI requires sign-in", lambda: check_admin(status_by_service, args.timeout_seconds))
    runner.check("MCP ports bind to loopback by default", lambda: check_loopback_ports(status_by_service))
    runner.check(
        "MCP endpoints enforce MCPLAB_MCP_TOKEN when configured",
        lambda: check_access_token(status_by_service, args.timeout_seconds),
    )
    runner.check("container security policy is enforced", check_container_security)
    runner.check("containers only receive their own config", check_config_isolation)
    runner.check("HTTP health endpoints are available", lambda: check_health_endpoints(status_by_service))
    runner.check(
        "config status reports loaded settings without secrets",
        lambda: check_config_status(status_by_service, args.timeout_seconds),
    )
    runner.check(
        "API Contract MCP is reachable",
        lambda: check_api_contract_mcp(status_by_service, args.timeout_seconds),
    )
    runner.check(
        "SQL Server MCP query is read-only and connected",
        lambda: check_sql_server(status_by_service, args.timeout_seconds),
    )
    if args.check_hot_reload:
        runner.check(
            "SQL Server MCP reloads service.env changes without restart",
            lambda: check_sql_server_hot_reload(status_by_service, args.timeout_seconds),
        )
    runner.check(
        "Oracle MCP query is read-only and connected",
        lambda: check_oracle(status_by_service, args.timeout_seconds, args.include_oracle),
    )
    runner.check(
        "Elasticsearch MCP is connected",
        lambda: check_elasticsearch(status_by_service, args.timeout_seconds),
    )
    runner.check("Redis MCP is connected", lambda: check_ping(status_by_service, "mcp-redis", args.timeout_seconds))
    runner.check(
        "OIDC MCP can inspect JWT locally",
        lambda: check_oidc_mcp(status_by_service, args.timeout_seconds),
    )
    runner.check(
        "Mosquitto MCP is connected",
        lambda: check_ping(status_by_service, "mcp-mosquitto", args.timeout_seconds),
    )
    runner.check(
        "RabbitMQ MCP is connected",
        lambda: check_ping(status_by_service, "mcp-rabbitmq", args.timeout_seconds),
    )
    runner.check(
        "Mailpit MCP can find captured mail",
        lambda: check_mailpit_mcp(status_by_service, args.timeout_seconds),
    )
    runner.check(
        "write tools reject unsafe no-op inputs",
        lambda: check_write_tool_guards(status_by_service, args.timeout_seconds, args.include_oracle),
    )
    runner.check(
        "Reader MCP can inspect the mounted root",
        lambda: check_reader(status_by_service, args.timeout_seconds),
    )
    runner.check(
        "Reader MCP parses PDF, Office, image and text samples",
        lambda: check_reader_samples(status_by_service, args.timeout_seconds),
    )
    runner.check(
        "API Contract MCP reports redirects without following them",
        lambda: check_api_contract_redirect(status_by_service, args.timeout_seconds),
    )
    runner.check(
        "OIDC MCP validates signatures and caches provider metadata",
        lambda: check_oidc_validation(status_by_service, args.timeout_seconds),
    )
    runner.check(
        "Redis, MQTT and RabbitMQ connect over TLS",
        lambda: check_tls_connections(status_by_service, args.timeout_seconds),
    )
    runner.check(
        "Docker MCP can inspect compose containers",
        lambda: check_docker_mcp(status_by_service, args.timeout_seconds),
    )

    return runner.finish()


def load_compose_status() -> dict[str, dict[str, Any]]:
    completed = run_command(["docker", "compose", "ps", "--format", "json"])
    services: dict[str, dict[str, Any]] = {}

    for line in completed.stdout.splitlines():
        if not line.strip():
            continue

        item = json.loads(line)
        service = item.get("Service")
        if isinstance(service, str):
            services[service] = item

    return services


def check_compose_health(status_by_service: dict[str, dict[str, Any]]) -> None:
    missing = [service for service in EXPECTED_SERVICES if service not in status_by_service]
    if missing:
        raise SmokeError(
            "Missing compose services: "
            + ", ".join(missing)
            + ". Start the stack with `docker compose up -d --build`."
        )

    unhealthy: list[str] = []
    for service in EXPECTED_SERVICES:
        status = status_by_service[service]
        state = status.get("State")
        health = status.get("Health")

        if state != "running":
            unhealthy.append(f"{service} state={state}")
        elif health and health != "healthy":
            unhealthy.append(f"{service} health={health}")

    if unhealthy:
        raise SmokeError("; ".join(unhealthy))


def check_loopback_ports(status_by_service: dict[str, dict[str, Any]]) -> None:
    bind_address = read_setting("MCP_BIND_ADDRESS") or "127.0.0.1"
    exposed: list[str] = []

    for service in [*EXPECTED_SERVICES, ADMIN_SERVICE]:
        if service not in status_by_service:
            continue
        if service == ADMIN_SERVICE:
            bind_address = read_setting("MCP_ADMIN_BIND_ADDRESS") or "127.0.0.1"
        for publisher in status_by_service[service].get("Publishers") or []:
            if publisher.get("TargetPort") == 8080 and publisher.get("PublishedPort"):
                if publisher.get("URL") != bind_address:
                    exposed.append(f"{service} -> {publisher.get('URL')}:{publisher.get('PublishedPort')}")

    if exposed:
        raise SmokeError(f"Expected ports bound to {bind_address}: " + "; ".join(exposed))


def check_access_token(status_by_service: dict[str, dict[str, Any]], timeout_seconds: int) -> None:
    if not read_setting("MCPLAB_MCP_TOKEN"):
        raise SkipCheck("MCPLAB_MCP_TOKEN is not set")

    url = service_url(status_by_service, "mcp-sql-server")
    request = urllib.request.Request(
        url,
        data=b"{}",
        headers={"Content-Type": "application/json", "Accept": "application/json, text/event-stream"},
        method="POST",
    )

    try:
        urllib.request.urlopen(request, timeout=timeout_seconds)
    except urllib.error.HTTPError as exc:
        if exc.code == 401:
            return
        raise SmokeError(f"Expected HTTP 401 without a token, got {exc.code}") from exc

    raise SmokeError("Request without a token was accepted.")


def check_admin(status_by_service: dict[str, dict[str, Any]], timeout_seconds: int) -> None:
    status = status_by_service.get(ADMIN_SERVICE)
    if not status:
        raise SmokeError(f"{ADMIN_SERVICE} is not running.")
    if status.get("State") != "running" or (status.get("Health") and status.get("Health") != "healthy"):
        raise SmokeError(f"{ADMIN_SERVICE} state={status.get('State')} health={status.get('Health')}")

    base_url = service_url(status_by_service, ADMIN_SERVICE).removesuffix("/mcp")

    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, *args, **kwargs):
            return None

    opener = urllib.request.build_opener(NoRedirect)
    try:
        opener.open(base_url + "/", timeout=timeout_seconds)
        raise SmokeError("Admin home page was served without signing in.")
    except urllib.error.HTTPError as exc:
        if exc.code not in (301, 302) or "/login" not in (exc.headers.get("Location") or ""):
            raise SmokeError(f"Expected a redirect to /login, got HTTP {exc.code}") from exc

    with urllib.request.urlopen(base_url + "/login", timeout=timeout_seconds) as response:
        body = response.read().decode("utf-8")
    if "MCP Lab" not in body:
        raise SmokeError("Admin sign-in page did not render.")
    if not read_setting("MCPLAB_ADMIN_PASSWORD") and "MCPLAB_ADMIN_PASSWORD" not in body:
        raise SmokeError("Admin sign-in page does not explain that MCPLAB_ADMIN_PASSWORD is missing.")


def check_config_isolation() -> None:
    completed = run_command(["docker", "inspect", *EXPECTED_SERVICES])
    leaks: list[str] = []

    for container in json.loads(completed.stdout):
        name = str(container.get("Name", "")).lstrip("/")
        env_keys = [item.split("=", 1)[0] for item in container.get("Config", {}).get("Env") or []]
        leaked = [key for key in env_keys if "_CONN_" in key or key.endswith("PASSWORD") or key == "MCPLAB_MCP_TOKEN"]
        if leaked:
            leaks.append(f"{name} env: {', '.join(sorted(leaked))}")

        for mount in container.get("Mounts") or []:
            source = str(mount.get("Source", ""))
            if mount.get("Destination") == "/app/config" and not source.rstrip("/").endswith(f"/config/{name}"):
                leaks.append(f"{name} mounts {source} at /app/config")
            if source.endswith("/.env"):
                leaks.append(f"{name} mounts the root .env")

    if leaks:
        raise SmokeError("; ".join(leaks))


def check_container_security() -> None:
    completed = run_command(["docker", "inspect", *EXPECTED_SERVICES, ADMIN_SERVICE])
    containers = json.loads(completed.stdout)
    failures: list[str] = []

    for container in containers:
        name = str(container.get("Name", "")).lstrip("/")
        host_config = container.get("HostConfig", {})
        config = container.get("Config", {})

        if host_config.get("ReadonlyRootfs") is not True:
            failures.append(f"{name}: read_only is not enabled")

        cap_drop = host_config.get("CapDrop") or []
        if "ALL" not in cap_drop:
            failures.append(f"{name}: cap_drop does not include ALL")

        security_opt = host_config.get("SecurityOpt") or []
        if "no-new-privileges:true" not in security_opt:
            failures.append(f"{name}: no-new-privileges is not enabled")

        tmpfs = host_config.get("Tmpfs") or {}
        if "/tmp" not in tmpfs:
            failures.append(f"{name}: /tmp tmpfs is missing")

        user = str(config.get("User") or "")
        if not user or user in {"0", "root"}:
            failures.append(f"{name}: container is running as root")

    if failures:
        raise SmokeError("; ".join(failures))


def check_health_endpoints(status_by_service: dict[str, dict[str, Any]]) -> None:
    for service in EXPECTED_SERVICES:
        if service == "mcp-reader":
            continue

        url = service_url(status_by_service, service).removesuffix("/mcp") + "/health"
        with urllib.request.urlopen(url, timeout=10) as response:
            body = response.read().decode("utf-8")

        health = json.loads(body)
        if health.get("status") != "ok":
            raise SmokeError(f"{service} returned an invalid health response: {body}")


def check_config_status(status_by_service: dict[str, dict[str, Any]], timeout_seconds: int) -> None:
    problems: list[str] = []
    token = read_setting("MCPLAB_MCP_TOKEN")
    headers = {"Authorization": f"Bearer {token}"} if token else {}

    for service in EXPECTED_SERVICES:
        url = service_url(status_by_service, service).removesuffix("/mcp") + "/config/status"
        with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=timeout_seconds) as response:
            body = response.read().decode("utf-8")

        status = json.loads(body)
        config_file = status.get("config_file") or {}
        if status.get("service") != service:
            problems.append(f"{service}: reported service {status.get('service')!r}")
        if not config_file.get("exists"):
            problems.append(f"{service}: config file {config_file.get('path')} not found")
        if config_file.get("last_error"):
            problems.append(f"{service}: {config_file['last_error']}")
        for connection in status.get("connections") or []:
            if connection.get("state") != "loaded":
                problems.append(f"{service}: {connection.get('alias')} {connection.get('state')} {connection.get('errors')}")
        for ignored in status.get("ignored_settings") or []:
            problems.append(f"{service}: {ignored.get('key')} ignored ({ignored.get('reason')})")

        for secret in read_secret_values(service):
            if secret in body:
                problems.append(f"{service}: /config/status contains a secret value")
                break

    if problems:
        raise SmokeError("; ".join(problems))


def read_secret_values(service: str) -> list[str]:
    path = service_config_path(service)
    if not path.exists():
        return []

    secrets: list[str] = []
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        key, separator, value = line.partition("=")
        value = value.strip().strip('"').strip("'")
        if separator and len(value) >= 4 and re.search(r"(PASSWORD|TOKEN|SECRET|AUTH_HEADER_VALUE)$", key.strip()):
            secrets.append(value)

    return secrets


def check_sql_server(status_by_service: dict[str, dict[str, Any]], timeout_seconds: int) -> None:
    client = create_client(status_by_service, "mcp-sql-server", timeout_seconds)
    response = expect_tool_ok(client.call_tool("query", {"sql": "SELECT 1 AS value"}))
    data = response.get("data") or {}
    assert_contains(json.dumps(data), "value")
    assert_contains(json.dumps(data), "1")

    blocked = client.call_tool("query", {"sql": "SELECT 1 AS value; DROP TABLE dbo.SmokeTest"})
    expect_tool_kind(blocked, "blocked")


def check_sql_server_hot_reload(status_by_service: dict[str, dict[str, Any]], timeout_seconds: int) -> None:
    env_path = service_config_path("mcp-sql-server")
    if not env_path.exists():
        raise SkipCheck(f"{env_path.relative_to(REPO_ROOT)} does not exist; run scripts/init-config.py")

    original_text = env_path.read_text(encoding="utf-8-sig")
    host_match = re.search(r"^(MSSQL_CONN_[A-Z0-9]+_HOST=)([^\r\n]*)", original_text, flags=re.MULTILINE)
    if not host_match:
        raise SkipCheck(f"{env_path.relative_to(REPO_ROOT)} does not contain MSSQL_CONN_*_HOST")

    original_host = host_match.group(2)
    replacement_host = f"hot-reload-{uuid.uuid4().hex}.invalid"
    added_host = f"hot-reload-added-{uuid.uuid4().hex}.invalid"
    added_connection = (
        f"\nMSSQL_CONN_SMOKEADDED_NAME=smoke-added\nMSSQL_CONN_SMOKEADDED_HOST={added_host}\n"
        "MSSQL_CONN_SMOKEADDED_USER=smoke\nMSSQL_CONN_SMOKEADDED_PASSWORD=smoke\n"
        "MSSQL_CONN_SMOKEADDED_DATABASE=master\n"
    )
    client = create_client(status_by_service, "mcp-sql-server", timeout_seconds)

    try:
        updated_text = original_text[:host_match.start(2)] + replacement_host + original_text[host_match.end(2):]
        env_path.write_text(updated_text, encoding="utf-8")
        wait_for_sql_server_host(client, replacement_host, timeout_seconds)

        env_path.write_text(original_text.rstrip("\n") + "\n" + added_connection, encoding="utf-8")
        wait_for_sql_server_host(client, added_host, timeout_seconds)
    finally:
        env_path.write_text(original_text, encoding="utf-8")

    wait_for_sql_server_host(client, original_host, timeout_seconds)
    wait_for_sql_server_host(client, added_host, timeout_seconds, expect_present=False)


def wait_for_sql_server_host(
    client: McpClient,
    expected_host: str,
    timeout_seconds: int,
    expect_present: bool = True,
) -> None:
    deadline = time.monotonic() + max(timeout_seconds, 3)
    last_hosts: list[str] = []

    while time.monotonic() < deadline:
        response = expect_tool_ok(client.call_tool("list_connections"))
        data = response.get("data") or []
        last_hosts = [
            str(item.get("host"))
            for item in data
            if isinstance(item, dict) and item.get("host") is not None
        ]

        if (expected_host in last_hosts) == expect_present:
            return

        time.sleep(0.2)

    state = "report" if expect_present else "drop"
    raise SmokeError(f"SQL Server list_connections did not {state} host {expected_host!r}. Last hosts: {last_hosts}")


def check_oracle(status_by_service: dict[str, dict[str, Any]], timeout_seconds: int, include_oracle: bool) -> None:
    if not include_oracle:
        raise SkipCheck("pass --include-oracle when a read-only Oracle target is available")

    client = create_client(status_by_service, "mcp-oracle", timeout_seconds)
    response = expect_tool_ok(client.call_tool("query", {"sql": "SELECT 1 AS value FROM DUAL"}))
    data = response.get("data") or {}
    assert_contains(json.dumps(data), "VALUE")
    assert_contains(json.dumps(data), "1")

    blocked = client.call_tool("query", {"sql": "SELECT 1 AS value FROM DUAL; DROP TABLE SmokeTest"})
    expect_tool_kind(blocked, "blocked")


def check_elasticsearch(status_by_service: dict[str, dict[str, Any]], timeout_seconds: int) -> None:
    client = create_client(status_by_service, "mcp-elasticsearch", timeout_seconds)
    expect_tool_ok(client.call_tool("ping_connection"))

    response = expect_tool_ok(client.call_tool("get_cluster_health"))
    health = response.get("data") or {}
    status = str(health.get("status", ""))
    if status.lower() == "red":
        raise SmokeError(f"Elasticsearch cluster status is red: {json.dumps(health)}")


def check_api_contract_mcp(status_by_service: dict[str, dict[str, Any]], timeout_seconds: int) -> None:
    client = create_client(status_by_service, "mcp-api-contract", timeout_seconds)
    expect_tool_ok(client.call_tool("list_connections"))


def check_oidc_mcp(status_by_service: dict[str, dict[str, Any]], timeout_seconds: int) -> None:
    client = create_client(status_by_service, "mcp-oidc", timeout_seconds)
    expect_tool_ok(client.call_tool("list_connections"))

    response = expect_tool_ok(
        client.call_tool(
            "inspect_jwt",
            {
                "token": create_unsigned_jwt(),
                "includeClaims": True,
                "claimLimit": 20,
            },
        )
    )
    data = response.get("data") or {}
    assert_contains(json.dumps(data), "https://issuer.example.test")
    assert_contains(json.dumps(data), "subject-1")


TEST_ROOT = REPO_ROOT / "tests" / "integration"
TEST_API_STATS_URL = "http://127.0.0.1:18000/stats"


def require_test_backends() -> None:
    if not is_truthy(read_setting("MCP_SMOKE_TEST_BACKENDS")):
        raise SkipCheck("needs the test backends from tests/integration (set MCP_SMOKE_TEST_BACKENDS=1)")


def check_api_contract_redirect(status_by_service: dict[str, dict[str, Any]], timeout_seconds: int) -> None:
    require_test_backends()
    client = create_client(status_by_service, "mcp-api-contract", timeout_seconds)
    data = expect_tool_ok(client.call_tool("invoke_endpoint", {"path": "/redirect", "connection": "LAB"})).get("data") or {}

    if data.get("status_code") != 302:
        raise SmokeError(f"invoke_endpoint followed or lost the redirect: {data}")

    if not str(data.get("redirect_location", "")).endswith("/items"):
        raise SmokeError(f"invoke_endpoint did not report the redirect location: {data}")


def sign_test_jwt(payload: dict[str, Any], kid: str) -> str:
    """Signs an RS256 JWT with the throwaway key in tests/integration/oidc-test-key.json."""
    key = json.loads((TEST_ROOT / "oidc-test-key.json").read_text(encoding="utf-8"))

    def to_int(value: str) -> int:
        return int.from_bytes(base64.urlsafe_b64decode(value + "=" * (-len(value) % 4)), "big")

    n, d = to_int(key["n"]), to_int(key["d"])
    signing_input = f"{base64url_json({'alg': 'RS256', 'typ': 'JWT', 'kid': kid})}.{base64url_json(payload)}"
    # EMSA-PKCS1-v1_5 encoding of the SHA-256 digest (RFC 8017, section 9.2).
    digest_info = bytes.fromhex("3031300d060960864801650304020105000420") + hashlib.sha256(signing_input.encode()).digest()
    size = (n.bit_length() + 7) // 8
    encoded = b"\x00\x01" + b"\xff" * (size - len(digest_info) - 3) + b"\x00" + digest_info
    signature = pow(int.from_bytes(encoded, "big"), d, n).to_bytes(size, "big")
    return f"{signing_input}.{base64.urlsafe_b64encode(signature).decode('ascii').rstrip('=')}"


def read_test_api_stats() -> dict[str, int]:
    with urllib.request.urlopen(TEST_API_STATS_URL, timeout=5) as response:
        return json.loads(response.read())


def check_oidc_validation(status_by_service: dict[str, dict[str, Any]], timeout_seconds: int) -> None:
    require_test_backends()
    client = create_client(status_by_service, "mcp-oidc", timeout_seconds)
    now = int(time.time())
    claims = {"iss": "http://test-api:8000", "sub": "smoke", "iat": now, "exp": now + 600}

    def validate(token: str) -> dict[str, Any]:
        return parse_tool_response(client.call_tool("validate_jwt", {"token": token, "connection": "LAB"}))

    valid = validate(sign_test_jwt(claims, "test-key"))
    if valid.get("ok") is not True or (valid.get("data") or {}).get("valid") is not True:
        raise SmokeError(f"validate_jwt rejected a correctly signed token: {valid}")

    tampered = sign_test_jwt(claims, "test-key").rsplit(".", 1)[0] + "." + base64url_json({"forged": True})
    if (validate(tampered).get("data") or {}).get("valid") is True:
        raise SmokeError("validate_jwt accepted a token with a forged signature.")

    expired = validate(sign_test_jwt({**claims, "exp": now - 3600}, "test-key"))
    if (expired.get("data") or {}).get("valid") is True:
        raise SmokeError("validate_jwt accepted an expired token.")

    before = read_test_api_stats().get("/jwks", 0)
    validate(sign_test_jwt(claims, "test-key"))
    if read_test_api_stats().get("/jwks", 0) != before:
        raise SmokeError("validate_jwt fetched the JWKS again although the key id is cached.")

    validate(sign_test_jwt(claims, "rotated-key"))
    if read_test_api_stats().get("/jwks", 0) != before + 1:
        raise SmokeError("validate_jwt did not refetch the JWKS once for an unknown key id.")


def check_tls_connections(status_by_service: dict[str, dict[str, Any]], timeout_seconds: int) -> None:
    require_test_backends()
    for service in ("mcp-redis", "mcp-mosquitto", "mcp-rabbitmq"):
        client = create_client(status_by_service, service, timeout_seconds)
        tls = parse_tool_response(client.call_tool("ping_connection", {"connection": "TLS"}))
        if tls.get("ok") is not True:
            raise SmokeError(f"{service} could not connect over TLS: {tls.get('message')}")

        # The certificate is self-signed, so a connection that verifies it must fail.
        strict = parse_tool_response(client.call_tool("ping_connection", {"connection": "STRICT"}))
        if strict.get("ok") is True:
            raise SmokeError(f"{service} accepted an untrusted certificate without SSL_SKIP_VERIFY.")


def check_ping(status_by_service: dict[str, dict[str, Any]], service: str, timeout_seconds: int) -> None:
    client = create_client(status_by_service, service, timeout_seconds)
    expect_tool_ok(client.call_tool("ping_connection"))


def check_write_tool_guards(
    status_by_service: dict[str, dict[str, Any]],
    timeout_seconds: int,
    include_oracle: bool,
) -> None:
    sql_server = create_client(status_by_service, "mcp-sql-server", timeout_seconds)
    sql_connection = first_connection_name(sql_server)
    expect_tool_kind(sql_server.call_tool("execute", {"sql": "UPDATE dbo.SmokeTest SET Id = 1 WHERE Id = 2"}), "blocked")
    expect_tool_kind(
        sql_server.call_tool("execute", {"sql": "DROP TABLE dbo.SmokeTest", "connection": sql_connection}),
        "blocked",
    )
    expect_tool_kind(sql_server.call_tool("query", {"sql": "SELECT * INTO dbo.SmokeCopy FROM sys.objects"}), "blocked")

    redis = create_client(status_by_service, "mcp-redis", timeout_seconds)
    redis_connection = first_connection_name(redis)
    expect_tool_kind(redis.call_tool("delete_keys", {"keys": "smoke-missing"}), "blocked")
    expect_tool_kind(redis.call_tool("delete_keys", {"keys": "", "connection": redis_connection}), "error")
    expect_tool_kind(
        redis.call_tool("set_key_ttl", {"key": "smoke-missing", "ttlSeconds": 0, "connection": redis_connection}),
        "error",
    )

    elasticsearch = create_client(status_by_service, "mcp-elasticsearch", timeout_seconds)
    es_connection = first_connection_name(elasticsearch)
    for query_body in (
        "{\"query\":{\"match_all\":{}}}",
        "{\"query\":{\"bool\":{\"must\":{\"match_all\":{}}}}}",
    ):
        expect_tool_kind(
            elasticsearch.call_tool(
                "delete_by_query",
                {"index": "smoke-test", "queryBody": query_body, "connection": es_connection},
            ),
            "blocked",
        )
    expect_tool_kind(
        elasticsearch.call_tool(
            "delete_by_query",
            {"index": "smoke-*", "queryBody": "{\"query\":{\"term\":{\"id\":1}}}", "connection": es_connection},
        ),
        "blocked",
    )
    expect_tool_kind(
        elasticsearch.call_tool(
            "index_document",
            {
                "index": "smoke-test",
                "document": "{",
                "connection": es_connection,
            },
        ),
        "error",
    )

    if include_oracle:
        oracle = create_client(status_by_service, "mcp-oracle", timeout_seconds)
        expect_tool_kind(
            oracle.call_tool("execute", {"sql": "DROP TABLE SmokeTest", "connection": first_connection_name(oracle)}),
            "blocked",
        )


def first_connection_name(client: McpClient) -> str:
    response = expect_tool_ok(client.call_tool("list_connections"))
    connections = response.get("data") or []
    if not connections:
        raise SmokeError(f"{client.url} has no configured connections.")

    return str(connections[0]["name"])


def check_reader(status_by_service: dict[str, dict[str, Any]], timeout_seconds: int) -> None:
    client = create_client(status_by_service, "mcp-reader", timeout_seconds)
    root_text = client.call_tool("get_document_root").text
    root = json.loads(root_text)

    if root.get("document_root") != "/host_root":
        raise SmokeError(f"Unexpected reader document root: {root_text}")

    path_usage = root.get("path_usage") or {}
    if path_usage.get("requires_under_mount_root") is not True:
        raise SmokeError("Reader path usage does not require paths under the mount root.")

    listing_text = client.call_tool(
        "list_documents",
        {
            "subpath": ".",
            "recursive": False,
            "limit": 5,
        },
    ).text
    listing = json.loads(listing_text)

    if listing.get("exists") is not True:
        raise SmokeError(f"Reader mounted root does not exist: {listing_text}")

    if int(listing.get("returned_count", 0)) > 5:
        raise SmokeError(f"Reader list_documents ignored the limit: {listing_text}")

    missing = client.call_tool("read_document", {"path": "does-not-exist.pdf"})
    if not missing.is_error or "error" not in json.loads(missing.text):
        raise SmokeError(f"Reader did not flag a missing file with isError: {missing.text}")


READER_SAMPLES = {
    "sample.pdf": ("pdf", ["MCP Lab sample pdf page 1", "MCP Lab sample pdf page 2"]),
    "sample.docx": ("docx", ["中文段落", "docx-table-value"]),
    "sample.pptx": ("pptx", ["MCP Lab sample pptx", "投影片內容"]),
    "sample.xlsx": ("xlsx", ["MCP Lab sample xlsx", "試算表"]),
    "sample.png": ("image", []),
    "sample.txt": ("text", ["MCP Lab integration test document."]),
}


def check_reader_samples(status_by_service: dict[str, dict[str, Any]], timeout_seconds: int) -> None:
    """Parses the sample files in tests/integration/docs; set MCP_SMOKE_READER_SAMPLES to their subpath."""
    subpath = read_setting("MCP_SMOKE_READER_SAMPLES")
    if not subpath:
        raise SkipCheck("set MCP_SMOKE_READER_SAMPLES to the subpath holding tests/integration/docs samples")

    client = create_client(status_by_service, "mcp-reader", timeout_seconds)
    for name, (expected_type, markers) in READER_SAMPLES.items():
        path = f"{subpath.rstrip('/')}/{name}"
        text = client.call_tool("read_document", {"path": path}).text
        document = json.loads(text)
        blob = json.dumps(document, ensure_ascii=False)

        if document.get("type") != expected_type:
            raise SmokeError(f"Reader returned type={document.get('type')} for {name}: {text[:300]}")

        missing = [marker for marker in markers if marker not in blob]
        if missing:
            raise SmokeError(f"Reader output for {name} is missing {missing}: {text[:300]}")

    search = json.loads(
        client.call_tool("search_documents", {"keyword": "MCP Lab sample", "subpath": subpath}).text
    )
    found = {Path(result["path"]).name for result in search.get("results", [])}
    expected = {"sample.pdf", "sample.docx", "sample.pptx", "sample.xlsx"}
    if not expected <= found:
        raise SmokeError(f"search_documents missed {sorted(expected - found)}: {search}")


def check_docker_mcp(status_by_service: dict[str, dict[str, Any]], timeout_seconds: int) -> None:
    if "mcp-docker" not in status_by_service:
        raise SkipCheck("mcp-docker is not running; start it with `docker compose --profile docker up -d`")

    client = create_client(status_by_service, "mcp-docker", timeout_seconds)
    expect_tool_ok(client.call_tool("ping_docker"))

    containers = expect_tool_ok(
        client.call_tool(
            "list_containers",
            {
                "all": True,
                "service": "mcp-docker",
                "limit": 10,
            },
        )
    )
    data = containers.get("data") or {}
    assert_contains(json.dumps(data), "mcp-docker")

    expect_tool_ok(client.call_tool("get_container_stats", {"container": "mcp-docker"}))

    inspected = json.loads(run_command(["docker", "inspect", "mcp-docker"]).stdout)[0]
    if any(str(mount.get("Destination", "")).endswith("docker.sock") for mount in inspected.get("Mounts") or []):
        raise SmokeError("mcp-docker mounts the Docker socket directly; it should use mcp-docker-proxy.")

    # The proxy must refuse requests that change state, even from the container allowed to reach it.
    request = (
        "exec 3<>/dev/tcp/mcp-docker-proxy/2375 && "
        "printf 'POST /containers/create HTTP/1.1\\r\\nHost: proxy\\r\\nContent-Type: application/json\\r\\n"
        "Content-Length: 2\\r\\nConnection: close\\r\\n\\r\\n{}' >&3 && head -n 1 <&3"
    )
    status_line = run_command(["docker", "exec", "mcp-docker", "bash", "-c", request]).stdout.strip()
    if " 403 " not in f"{status_line} ":
        raise SmokeError(f"mcp-docker-proxy did not reject POST /containers/create: {status_line}")


def check_mailpit_mcp(status_by_service: dict[str, dict[str, Any]], timeout_seconds: int) -> None:
    subject = f"mcp-lab smoke {uuid.uuid4()}"
    recipient = "mcp-smoke@example.test"
    send_smoke_email(subject, recipient)

    client = create_client(status_by_service, "mcp-mailpit", timeout_seconds)
    expect_tool_ok(client.call_tool("ping_connection"))
    found = expect_tool_ok(
        client.call_tool(
            "wait_for_message",
            {
                "query": subject,
                "to": recipient,
                "subjectContains": "mcp-lab smoke",
                "timeoutSeconds": 10,
            },
        )
    )
    message = (found.get("data") or {}).get("message") or {}
    message_id = message.get("id")

    if not isinstance(message_id, str) or not message_id:
        raise SmokeError(f"Mailpit wait_for_message did not return a message id: {found}")

    headers = expect_tool_ok(client.call_tool("get_message_headers", {"id": message_id}))
    assert_contains(json.dumps(headers.get("data") or {}), subject)

    source = expect_tool_ok(client.call_tool("get_message_source", {"id": message_id, "limit": 4000}))
    assert_contains(json.dumps(source.get("data") or {}), subject)


def send_smoke_email(subject: str, recipient: str) -> None:
    host = os.environ.get("MCP_SMOKE_MAILPIT_SMTP_HOST", "127.0.0.1")
    port = int(os.environ.get("MCP_SMOKE_MAILPIT_SMTP_PORT", "1025"))
    message = EmailMessage()
    message["Subject"] = subject
    message["From"] = "mcp-smoke@example.test"
    message["To"] = recipient
    message.set_content("mcp-lab smoke test body")

    try:
        with smtplib.SMTP(host, port, timeout=10) as smtp:
            smtp.send_message(message)
    except OSError as exc:
        raise SmokeError(f"Mailpit SMTP endpoint {host}:{port} is not reachable: {exc}") from exc


def create_unsigned_jwt() -> str:
    header = {"alg": "none", "typ": "JWT"}
    payload = {
        "iss": "https://issuer.example.test",
        "sub": "subject-1",
        "aud": "api://default",
        "scope": "openid profile",
        "exp": 4102444800,
    }

    return f"{base64url_json(header)}.{base64url_json(payload)}."


def base64url_json(value: dict[str, Any]) -> str:
    raw = json.dumps(value, separators=(",", ":")).encode("utf-8")
    return base64.urlsafe_b64encode(raw).decode("ascii").rstrip("=")


def create_client(status_by_service: dict[str, dict[str, Any]], service: str, timeout_seconds: int) -> McpClient:
    url = service_url(status_by_service, service)
    client = McpClient(url, timeout_seconds)
    client.initialize()
    return client


def service_url(status_by_service: dict[str, dict[str, Any]], service: str) -> str:
    status = status_by_service.get(service)
    if not status:
        raise SmokeError(f"{service} is not present in docker compose ps output.")

    for publisher in status.get("Publishers") or []:
        if publisher.get("TargetPort") == 8080 and publisher.get("Protocol") == "tcp":
            published_port = publisher.get("PublishedPort")
            if published_port and publisher.get("URL") != "::":
                return f"http://127.0.0.1:{published_port}/mcp"

    completed = run_command(["docker", "compose", "port", service, "8080"])
    endpoint = completed.stdout.strip().splitlines()[0]
    port = endpoint.rsplit(":", 1)[-1]
    return f"http://127.0.0.1:{port}/mcp"


def parse_mcp_response(body: str) -> dict[str, Any] | None:
    stripped = body.strip()
    if not stripped:
        return None

    messages: list[dict[str, Any]] = []
    for line in body.splitlines():
        if not line.startswith("data:"):
            continue

        data = line.removeprefix("data:").strip()
        if not data or data == "[DONE]":
            continue

        messages.append(json.loads(data))

    if messages:
        return messages[-1]

    return json.loads(stripped)


def extract_tool_text(result: dict[str, Any]) -> str:
    content = result.get("content") or []
    texts = []

    for item in content:
        if isinstance(item, dict) and item.get("type") == "text":
            texts.append(str(item.get("text") or ""))

    return "\n".join(texts)


def parse_tool_response(result: ToolResult) -> dict[str, Any]:
    try:
        response = json.loads(result.text)
    except json.JSONDecodeError as exc:
        raise SmokeError(f"Tool did not return JSON: {result.text}") from exc

    if not isinstance(response, dict) or "ok" not in response or "kind" not in response:
        raise SmokeError(f"Tool did not return a response envelope: {result.text}")

    # Failed envelopes must also carry the MCP isError flag, and successful ones must not.
    if result.is_error != (response.get("ok") is False):
        raise SmokeError(f"isError={result.is_error} does not match the envelope: {result.text}")

    return response


def expect_tool_ok(result: ToolResult) -> dict[str, Any]:
    response = parse_tool_response(result)
    if response.get("ok") is not True:
        raise SmokeError(f"Expected ok=true response: {result.text}")

    return response


def expect_tool_kind(result: ToolResult, expected_kind: str) -> dict[str, Any]:
    response = parse_tool_response(result)
    if response.get("kind") != expected_kind:
        raise SmokeError(f"Expected kind={expected_kind}: {result.text}")

    return response


def get_header(headers: HTTPMessage, expected_name: str) -> str | None:
    for name, value in headers.items():
        if name.lower() == expected_name.lower():
            return value

    return None


def assert_contains(text: str, expected: str) -> None:
    if expected not in text:
        raise SmokeError(f"Expected {expected!r} in response: {text}")


def run_command(args: list[str]) -> subprocess.CompletedProcess[str]:
    try:
        completed = subprocess.run(
            args,
            cwd=REPO_ROOT,
            check=False,
            capture_output=True,
            text=True,
            encoding="utf-8",
        )
    except FileNotFoundError as exc:
        raise SmokeError(f"Required command was not found: {args[0]}") from exc

    if completed.returncode != 0:
        output = (completed.stderr or completed.stdout).strip()
        raise SmokeError(f"`{' '.join(args)}` failed: {output}")

    return completed


def service_config_path(service: str) -> Path:
    return REPO_ROOT / "config" / service / "service.env"


def read_setting(name: str) -> str:
    """Reads a setting from the process environment, the SQL Server service config, or the root .env."""
    value = os.environ.get(name)
    if value is None:
        for path in (service_config_path("mcp-sql-server"), REPO_ROOT / ".env"):
            if not path.exists():
                continue

            for line in path.read_text(encoding="utf-8-sig").splitlines():
                key, separator, raw = line.partition("=")
                if separator and key.strip() == name:
                    value = raw.strip().strip('"').strip("'")

            if value is not None:
                break

    return (value or "").strip()


def is_truthy(value: str | None) -> bool:
    return value is not None and value.lower() in {"1", "true", "yes", "y", "on"}


if __name__ == "__main__":
    sys.exit(main())
