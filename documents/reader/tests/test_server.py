import asyncio
import importlib.util
import sys
import tempfile
import types
import unittest
from pathlib import Path


def _load_server_module():
    class FakeFastMCP:
        def __init__(self, *args, **kwargs):
            pass

        def tool(self):
            def decorator(func):
                return func

            return decorator

        def run(self, *args, **kwargs):
            pass

        def custom_route(self, *args, **kwargs):
            def decorator(func):
                return func

            return decorator

    mcp_module = types.ModuleType("mcp")
    server_module = types.ModuleType("mcp.server")
    fastmcp_module = types.ModuleType("mcp.server.fastmcp")
    fastmcp_server_module = types.ModuleType("mcp.server.fastmcp.server")

    class FakeSettings:
        model_config = {"env_file": ".env"}

    fastmcp_module.FastMCP = FakeFastMCP
    fastmcp_server_module.Settings = FakeSettings
    server_module.fastmcp = fastmcp_module
    mcp_module.server = server_module
    sys.modules.setdefault("mcp", mcp_module)
    sys.modules.setdefault("mcp.server", server_module)
    sys.modules.setdefault("mcp.server.fastmcp", fastmcp_module)
    sys.modules.setdefault("mcp.server.fastmcp.server", fastmcp_server_module)

    if "chardet" not in sys.modules:
        chardet_module = types.ModuleType("chardet")
        chardet_module.detect = lambda raw: {"encoding": "utf-8"}
        sys.modules["chardet"] = chardet_module

    module_path = Path(__file__).resolve().parents[1] / "server.py"
    module_dir = str(module_path.parent)
    if module_dir not in sys.path:
        sys.path.insert(0, module_dir)

    spec = importlib.util.spec_from_file_location("mcp_reader_server", module_path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


server = _load_server_module()


class ReaderServerTests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_dir.name).resolve()
        server.MOUNT_ROOT = self.root
        server.HOST_MOUNT_ROOT = "/mnt/c/docs"
        server._SNAPSHOT.update({})

    def tearDown(self):
        self.temp_dir.cleanup()

    def test_resolve_windows_path_stays_under_mount_root(self):
        target = self.root / "notes" / "sample.md"
        target.parent.mkdir()
        target.write_text("content", encoding="utf-8")

        resolved = server._resolve_path(r"C:\docs\notes\sample.md")

        self.assertEqual(target, resolved)

    def test_normalize_mount_root_accepts_windows_drive_path(self):
        actual = server._normalize_mount_root(r"C:\Workspace\Projects")

        self.assertEqual("/mnt/c/Workspace/Projects", actual)

    def test_normalize_mount_root_accepts_docker_desktop_host_path(self):
        actual = server._normalize_mount_root("/run/desktop/mnt/host/c/Workspace/Projects")

        self.assertEqual("/mnt/c/Workspace/Projects", actual)

    def test_resolve_windows_path_when_mount_root_is_windows_drive_path(self):
        target = self.root / "Projects" / "ECK_NIS" / "sample.pdf"
        target.parent.mkdir(parents=True)
        target.write_bytes(b"%PDF-1.7")
        server.HOST_MOUNT_ROOT = server._normalize_mount_root("C:/Workspace")

        resolved = server._resolve_path(r"C:\Workspace\Projects\ECK_NIS\sample.pdf")

        self.assertEqual(target, resolved)

    def test_resolve_rejects_path_outside_mount_root(self):
        with self.assertRaises(ValueError):
            server._resolve_path("/mnt/c/other/sample.md")

    def test_list_documents_is_non_recursive_by_default(self):
        (self.root / "root.md").write_text("root", encoding="utf-8")
        nested = self.root / "nested"
        nested.mkdir()
        (nested / "child.md").write_text("child", encoding="utf-8")

        result = server.list_documents()

        self.assertTrue(result["exists"])
        self.assertFalse(result["recursive"])
        self.assertEqual(1, result["returned_count"])
        self.assertEqual(["/mnt/c/docs/root.md"], [item["path"] for item in result["files"]])

    def test_list_documents_reports_truncation(self):
        (self.root / "a.md").write_text("a", encoding="utf-8")
        (self.root / "b.md").write_text("b", encoding="utf-8")
        nested = self.root / "nested"
        nested.mkdir()
        (nested / "c.md").write_text("c", encoding="utf-8")

        result = server.list_documents(recursive=True, limit=2, max_depth=1)

        self.assertEqual(2, result["returned_count"])
        self.assertTrue(result["truncated"])

    def test_list_documents_reports_depth_limit(self):
        nested = self.root / "nested"
        nested.mkdir()
        (nested / "child.md").write_text("child", encoding="utf-8")

        result = server.list_documents(recursive=True, max_depth=0)

        self.assertTrue(result["depth_limited"])
        self.assertTrue(result["truncated"])

    def test_list_documents_falls_back_on_invalid_limit(self):
        (self.root / "a.md").write_text("a", encoding="utf-8")

        result = server.list_documents(limit="bad")

        self.assertEqual(100, result["limit"])
        self.assertEqual(1, result["returned_count"])

    def test_list_documents_uses_snapshot_default_limit(self):
        (self.root / "a.md").write_text("a", encoding="utf-8")
        (self.root / "b.md").write_text("b", encoding="utf-8")
        (self.root / "c.md").write_text("c", encoding="utf-8")
        server._SNAPSHOT.update({"DOCUMENT_LIST_DEFAULT_LIMIT": "2"})

        result = server.list_documents()

        self.assertEqual(2, result["limit"])
        self.assertEqual(2, result["returned_count"])
        self.assertTrue(result["truncated"])

    def test_list_runtime_limits_returns_snapshot_values(self):
        server._SNAPSHOT.update({
            "DOCUMENT_LIST_DEFAULT_LIMIT": "25",
            "DOCUMENT_MAX_TEXT_MB": "5",
        })

        result = server.list_runtime_limits()

        self.assertEqual(25, result["document_list_default_limit"])
        self.assertEqual(5, result["max_text_mb"])

    def test_search_documents_limits_results_and_reports_truncation(self):
        (self.root / "a.md").write_text("needle one", encoding="utf-8")
        (self.root / "b.md").write_text("needle two", encoding="utf-8")

        result = server.search_documents("needle", recursive=False, limit=1, max_files_scanned=2)

        self.assertEqual(1, result["returned_count"])
        self.assertEqual(1, len(result["results"]))
        self.assertTrue(result["truncated"])


if __name__ == "__main__":
    unittest.main()

    def test_parse_text_reads_only_configured_bytes(self):
        server._SNAPSHOT.update({"DOCUMENT_MAX_TEXT_MB": "0.001"})
        file_path = self.root / "big.txt"
        file_path.write_bytes(b"a" * 5000)
        original_read_bytes = Path.read_bytes

        def fail_read_bytes(path_self):
            raise AssertionError("read_bytes must not load the whole file when a limit is set")

        Path.read_bytes = fail_read_bytes
        try:
            result = server._parse_text(file_path)
        finally:
            Path.read_bytes = original_read_bytes

        self.assertTrue(result["truncated"])
        self.assertEqual(len(result["content"]), int(0.001 * 1024 * 1024))


class AccessTokenTests(unittest.TestCase):
    def tearDown(self):
        server._SNAPSHOT.update({})

    def test_is_authorized_accepts_matching_bearer_token(self):
        self.assertTrue(server._is_authorized("Bearer secret", "secret"))
        self.assertTrue(server._is_authorized("bearer  secret ", "secret"))

    def test_is_authorized_rejects_missing_or_wrong_token(self):
        self.assertFalse(server._is_authorized(None, "secret"))
        self.assertFalse(server._is_authorized("Bearer other", "secret"))
        self.assertFalse(server._is_authorized("Basic secret", "secret"))
        self.assertFalse(server._is_authorized("Bearer secret", ""))

    def _call(self, path, headers):
        calls = []
        sent = []

        async def app(scope, receive, send):
            calls.append(scope["path"])

        async def send(message):
            sent.append(message)

        middleware = server.AccessTokenMiddleware(app)
        scope = {"type": "http", "path": path, "headers": headers}
        asyncio.run(middleware(scope, None, send))
        return calls, sent

    def test_middleware_passes_through_when_token_not_configured(self):
        calls, sent = self._call("/mcp", [])
        self.assertEqual(calls, ["/mcp"])
        self.assertEqual(sent, [])

    def test_middleware_rejects_request_without_token(self):
        server._SNAPSHOT.update({"MCPLAB_MCP_TOKEN": "secret"})
        calls, sent = self._call("/mcp", [])
        self.assertEqual(calls, [])
        self.assertEqual(sent[0]["status"], 401)

    def test_middleware_allows_request_with_token_and_health_without_token(self):
        server._SNAPSHOT.update({"MCPLAB_MCP_TOKEN": "secret"})
        calls, _ = self._call("/mcp", [(b"authorization", b"Bearer secret")])
        health_calls, _ = self._call("/health", [])
        self.assertEqual(calls, ["/mcp"])
        self.assertEqual(health_calls, ["/health"])


class ConfigStatusTests(unittest.TestCase):
    def test_config_status_reports_file_state_and_limits_without_values(self):
        server._SNAPSHOT.update({"DOCUMENT_MAX_IMAGES": "7", "MCPLAB_MCP_TOKEN": "secret"})
        try:
            status = server.config_status()
        finally:
            server._SNAPSHOT.update({})

        self.assertEqual(status["service"], "mcp-reader")
        self.assertIn("last_error", status["config_file"])
        self.assertEqual(status["limits"]["max_images"], 7)
        self.assertNotIn("secret", str(status))
