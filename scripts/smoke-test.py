#!/usr/bin/env python3
"""Run local Docker-backed smoke tests for the MCP services."""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import urllib.error
import urllib.request
from dataclasses import dataclass
from http.client import HTTPMessage
from pathlib import Path
from typing import Any


PROTOCOL_VERSION = "2025-06-18"
REPO_ROOT = Path(__file__).resolve().parents[1]

EXPECTED_SERVICES = [
    "mcp-sql-server",
    "mcp-oracle",
    "mcp-elasticsearch",
    "mcp-redis",
    "mcp-mosquitto",
    "mcp-rabbitmq",
    "mcp-reader",
    "mcp-docker",
]


class SmokeError(Exception):
    """Represents a failed smoke-test assertion."""


class SkipCheck(Exception):
    """Represents an intentionally skipped smoke-test check."""


@dataclass(frozen=True)
class ToolResult:
    text: str
    raw: dict[str, Any]


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

        text = extract_tool_text(result)
        if result.get("isError") is True:
            raise SmokeError(f"{name} returned isError=true: {text}")

        return ToolResult(text=text, raw=result)

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
    args = parser.parse_args()

    runner = Runner()
    status_by_service = load_compose_status()

    runner.check("compose services are running and healthy", lambda: check_compose_health(status_by_service))
    runner.check("container security policy is enforced", check_container_security)
    runner.check("HTTP health endpoints are available", lambda: check_health_endpoints(status_by_service))
    runner.check(
        "SQL Server MCP query is read-only and connected",
        lambda: check_sql_server(status_by_service, args.timeout_seconds),
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
        "Mosquitto MCP is connected",
        lambda: check_ping(status_by_service, "mcp-mosquitto", args.timeout_seconds),
    )
    runner.check(
        "RabbitMQ MCP is connected",
        lambda: check_ping(status_by_service, "mcp-rabbitmq", args.timeout_seconds),
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


def check_container_security() -> None:
    completed = run_command(["docker", "inspect", *EXPECTED_SERVICES])
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


def check_sql_server(status_by_service: dict[str, dict[str, Any]], timeout_seconds: int) -> None:
    client = create_client(status_by_service, "mcp-sql-server", timeout_seconds)
    response = expect_tool_ok(client.call_tool("query", {"sql": "SELECT 1 AS value"}))
    data = response.get("data") or {}
    assert_contains(json.dumps(data), "value")
    assert_contains(json.dumps(data), "1")

    blocked = client.call_tool("query", {"sql": "SELECT 1 AS value; DROP TABLE dbo.SmokeTest"})
    expect_tool_kind(blocked, "blocked")


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


def check_ping(status_by_service: dict[str, dict[str, Any]], service: str, timeout_seconds: int) -> None:
    client = create_client(status_by_service, service, timeout_seconds)
    expect_tool_ok(client.call_tool("ping_connection"))


def check_write_tool_guards(
    status_by_service: dict[str, dict[str, Any]],
    timeout_seconds: int,
    include_oracle: bool,
) -> None:
    sql_server = create_client(status_by_service, "mcp-sql-server", timeout_seconds)
    expect_tool_kind(sql_server.call_tool("execute", {"sql": "DROP TABLE dbo.SmokeTest"}), "blocked")

    redis = create_client(status_by_service, "mcp-redis", timeout_seconds)
    expect_tool_kind(redis.call_tool("delete_keys", {"keys": ""}), "error")
    expect_tool_kind(redis.call_tool("set_key_ttl", {"key": "smoke-missing", "ttlSeconds": 0}), "error")

    elasticsearch = create_client(status_by_service, "mcp-elasticsearch", timeout_seconds)
    expect_tool_kind(
        elasticsearch.call_tool(
            "delete_by_query",
            {
                "index": "smoke-test",
                "queryBody": "{\"query\":{\"match_all\":{}}}",
            },
        ),
        "blocked",
    )
    expect_tool_kind(
        elasticsearch.call_tool(
            "index_document",
            {
                "index": "smoke-test",
                "document": "{",
            },
        ),
        "error",
    )

    if include_oracle:
        oracle = create_client(status_by_service, "mcp-oracle", timeout_seconds)
        expect_tool_kind(oracle.call_tool("execute", {"sql": "DROP TABLE SmokeTest"}), "blocked")


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


def check_docker_mcp(status_by_service: dict[str, dict[str, Any]], timeout_seconds: int) -> None:
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


def is_truthy(value: str | None) -> bool:
    return value is not None and value.lower() in {"1", "true", "yes", "y", "on"}


if __name__ == "__main__":
    sys.exit(main())
