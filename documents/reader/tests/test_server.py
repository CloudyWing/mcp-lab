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

    mcp_module = types.ModuleType("mcp")
    server_module = types.ModuleType("mcp.server")
    fastmcp_module = types.ModuleType("mcp.server.fastmcp")
    fastmcp_module.FastMCP = FakeFastMCP
    server_module.fastmcp = fastmcp_module
    mcp_module.server = server_module
    sys.modules.setdefault("mcp", mcp_module)
    sys.modules.setdefault("mcp.server", server_module)
    sys.modules.setdefault("mcp.server.fastmcp", fastmcp_module)

    if "chardet" not in sys.modules:
        chardet_module = types.ModuleType("chardet")
        chardet_module.detect = lambda raw: {"encoding": "utf-8"}
        sys.modules["chardet"] = chardet_module

    module_path = Path(__file__).resolve().parents[1] / "server.py"
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

    def tearDown(self):
        self.temp_dir.cleanup()

    def test_resolve_windows_path_stays_under_mount_root(self):
        target = self.root / "notes" / "sample.md"
        target.parent.mkdir()
        target.write_text("content", encoding="utf-8")

        resolved = server._resolve_path(r"C:\docs\notes\sample.md")

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

        self.assertEqual(server.DEFAULT_LIST_LIMIT, result["limit"])
        self.assertEqual(1, result["returned_count"])

    def test_search_documents_limits_results_and_reports_truncation(self):
        (self.root / "a.md").write_text("needle one", encoding="utf-8")
        (self.root / "b.md").write_text("needle two", encoding="utf-8")

        result = server.search_documents("needle", recursive=False, limit=1, max_files_scanned=2)

        self.assertEqual(1, result["returned_count"])
        self.assertEqual(1, len(result["results"]))
        self.assertTrue(result["truncated"])


if __name__ == "__main__":
    unittest.main()
