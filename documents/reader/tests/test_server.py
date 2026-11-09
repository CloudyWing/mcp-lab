import asyncio
import importlib.util
import json
import sys
import tempfile
import threading
import types
import unittest
from pathlib import Path
from unittest import mock


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

        async def call_tool(self, name, arguments):
            return self.next_result

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

    class FakeModel:
        def __init__(self, **fields):
            self.__dict__.update(fields)

    types_module = types.ModuleType("mcp.types")
    types_module.CallToolResult = type("CallToolResult", (FakeModel,), {})
    types_module.TextContent = type("TextContent", (FakeModel,), {})

    fastmcp_module.FastMCP = FakeFastMCP
    fastmcp_server_module.Settings = FakeSettings
    server_module.fastmcp = fastmcp_module
    mcp_module.server = server_module
    sys.modules.setdefault("mcp", mcp_module)
    sys.modules.setdefault("mcp.server", server_module)
    sys.modules.setdefault("mcp.server.fastmcp", fastmcp_module)
    sys.modules.setdefault("mcp.server.fastmcp.server", fastmcp_server_module)
    sys.modules.setdefault("mcp.types", types_module)

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
ResponseBudget = sys.modules["response_budget"].ResponseBudget


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


    def test_parse_text_reads_only_configured_bytes(self):
        server._SNAPSHOT.update({"DOCUMENT_MAX_TEXT_MB": "0.001"})
        file_path = self.root / "big.txt"
        file_path.write_bytes(b"a" * 5000)
        original_read_bytes = Path.read_bytes

        def fail_read_bytes(path_self):
            raise AssertionError("read_bytes must not load the whole file when a limit is set")

        Path.read_bytes = fail_read_bytes
        try:
            result = server._parse_text(file_path, ResponseBudget(1048576))
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
        settings = {item["name"]: item for item in status["settings"]}
        self.assertEqual(settings["DOCUMENT_MAX_IMAGES"]["effective"], 7)
        self.assertEqual(settings["DOCUMENT_LIST_DEFAULT_LIMIT"]["max_from"], "DOCUMENT_LIST_MAX_LIMIT")
        self.assertNotIn("secret", str(status))

    def test_default_limit_is_capped_by_configured_max(self):
        server._SNAPSHOT.update({"DOCUMENT_LIST_MAX_LIMIT": "50", "DOCUMENT_LIST_DEFAULT_LIMIT": "80"})
        try:
            self.assertEqual(server._list_default_limit(), 50)
        finally:
            server._SNAPSHOT.update({})


class ToolErrorFlagTests(unittest.TestCase):
    def _call(self, text):
        block = server.TextContent(type="text", text=text)
        server.mcp.next_result = ([block], {"result": text})
        return asyncio.run(server.mcp.call_tool("read_document", {}))

    def test_error_payload_sets_is_error_and_keeps_content(self):
        result = self._call('{"error": "File not found: x.pdf"}')
        self.assertIsInstance(result, server.CallToolResult)
        self.assertTrue(result.isError)
        self.assertEqual(result.content[0].text, '{"error": "File not found: x.pdf"}')
        self.assertEqual(result.structuredContent, {"result": '{"error": "File not found: x.pdf"}'})

    def test_success_payload_is_returned_unchanged(self):
        for text in ('{"type": "text", "content": "error"}', '{"error": ""}', "[]", "not json"):
            with self.subTest(text=text):
                self.assertIsInstance(self._call(text), tuple)


class FakeWorksheet:
    def __init__(self, rows, calls):
        self._rows = rows
        self._calls = calls

    def reset_dimensions(self):
        self._calls.append("reset_dimensions")

    def iter_rows(self, values_only=False):
        return iter(self._rows)


class FakeWorkbook:
    def __init__(self, sheets, calls):
        self._sheets = sheets
        self._calls = calls
        self.sheetnames = list(sheets)

    def __getitem__(self, name):
        return FakeWorksheet(self._sheets[name], self._calls)

    def close(self):
        self._calls.append("close")


def _fake_openpyxl(sheets, calls):
    module = types.ModuleType("openpyxl")

    def load_workbook(path, **kwargs):
        calls.append(("load_workbook", kwargs))
        return FakeWorkbook(sheets, calls)

    module.load_workbook = load_workbook
    return module


class FakePage:
    def __init__(self, text, png_size, calls, width=10, height=10):
        self._text = text
        self._png_size = png_size
        self._calls = calls
        self.rect = types.SimpleNamespace(width=width, height=height)

    def get_text(self, mode):
        return self._text

    def get_pixmap(self, matrix=None):
        self._calls.append("pixmap")
        png_size = self._png_size
        return types.SimpleNamespace(tobytes=lambda fmt: b"x" * png_size)


class FakePdfDocument(list):
    def close(self):
        pass


def _fake_fitz(pages):
    module = types.ModuleType("fitz")
    module.open = lambda path: FakePdfDocument(pages)
    module.Matrix = lambda x, y: (x, y)
    return module


def _fake_docx(paragraphs, tables, images=()):
    module = types.ModuleType("docx")
    rels = {
        str(index): types.SimpleNamespace(
            reltype="http://schemas/image",
            target_part=types.SimpleNamespace(blob=b"x" * size, content_type="image/png"),
        )
        for index, size in enumerate(images)
    }
    document = types.SimpleNamespace(
        paragraphs=[types.SimpleNamespace(text=text) for text in paragraphs],
        tables=[
            types.SimpleNamespace(rows=[
                types.SimpleNamespace(cells=[types.SimpleNamespace(text=cell) for cell in row]) for row in table
            ])
            for table in tables
        ],
        part=types.SimpleNamespace(rels=rels),
    )
    module.Document = lambda path: document
    return module


class ReaderResponseLimitTests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_dir.name).resolve()
        server.MOUNT_ROOT = self.root
        server.HOST_MOUNT_ROOT = "/mnt/c/docs"
        server._SNAPSHOT.update({})

    def tearDown(self):
        server._SNAPSHOT.update({})
        self.temp_dir.cleanup()

    def _read(self, name, **kwargs):
        return json.loads(server.read_document(name, **kwargs))

    def test_non_text_file_over_file_limit_returns_error_without_parsing(self):
        server._SNAPSHOT.update({"DOCUMENT_MAX_FILE_MB": "0.1"})
        for name in ("big.pdf", "big.png"):
            with self.subTest(name=name):
                (self.root / name).write_bytes(b"x" * 200000)

                with mock.patch.object(server, "_parse_pdf", side_effect=AssertionError("must not parse")), \
                        mock.patch.object(server, "_parse_image", side_effect=AssertionError("must not parse")):
                    text = server.read_document(name)

                payload = json.loads(text)
                self.assertEqual("File too large: 0.19 MB exceeds DOCUMENT_MAX_FILE_MB=0.1", payload["error"])
                self.assertEqual(200000, payload["size_bytes"])
                self.assertEqual(int(0.1 * 1024 * 1024), payload["max_file_bytes"])
                self.assertTrue(server._is_error_payload(text))

    def test_text_file_is_not_limited_by_file_limit(self):
        server._SNAPSHOT.update({"DOCUMENT_MAX_FILE_MB": "0.1"})
        (self.root / "big.txt").write_bytes(b"a" * 200000)

        result = self._read("big.txt")

        self.assertNotIn("error", result)
        self.assertEqual(200000, result["size_bytes"])

    def test_text_file_within_budget_has_no_truncation_fields(self):
        (self.root / "small.txt").write_text("hello", encoding="utf-8")

        result = self._read("small.txt")

        self.assertEqual("hello", result["content"])
        self.assertFalse(result["truncated"])
        for name in ("original_bytes", "original_bytes_exact", "max_response_bytes"):
            self.assertNotIn(name, result)

    def test_text_file_over_budget_reports_original_size(self):
        server._SNAPSHOT.update({"MCPLAB_MAX_RESPONSE_BYTES": "1000"})
        (self.root / "big.txt").write_bytes(b"a" * 5000)

        result = self._read("big.txt")

        self.assertTrue(result["truncated"])
        self.assertEqual(5000, result["original_bytes"])
        self.assertTrue(result["original_bytes_exact"])
        self.assertEqual(1000, result["max_response_bytes"])
        self.assertEqual(1000, len(result["content"]))
        self.assertEqual(5000, result["size_bytes"])

    def test_text_limit_applies_when_smaller_than_budget(self):
        server._SNAPSHOT.update({"DOCUMENT_MAX_TEXT_MB": "0.0001", "MCPLAB_MAX_RESPONSE_BYTES": "1000000"})
        (self.root / "big.txt").write_bytes(b"a" * 5000)

        result = self._read("big.txt")

        self.assertTrue(result["truncated"])
        self.assertEqual(int(0.0001 * 1024 * 1024), len(result["content"]))
        self.assertNotIn("original_bytes", result)

    def test_budget_applies_when_smaller_than_text_limit_and_when_text_limit_is_unlimited(self):
        def fail_read_bytes(path_self):
            raise AssertionError("read_bytes must not be used")

        for text_limit in ("1", "0"):
            with self.subTest(text_limit=text_limit):
                server._SNAPSHOT.update({"DOCUMENT_MAX_TEXT_MB": text_limit, "MCPLAB_MAX_RESPONSE_BYTES": "500"})
                (self.root / "big.txt").write_bytes(b"a" * 5000)

                with mock.patch.object(Path, "read_bytes", fail_read_bytes):
                    result = self._read("big.txt")

                self.assertEqual(500, len(result["content"]))
                self.assertEqual(5000, result["original_bytes"])

    def test_text_truncation_does_not_split_multibyte_characters(self):
        server._SNAPSHOT.update({"MCPLAB_MAX_RESPONSE_BYTES": "10"})
        (self.root / "cjk.txt").write_text("測試文字內容很長很長", encoding="utf-8")

        result = self._read("cjk.txt")

        self.assertTrue(result["truncated"])
        self.assertEqual("測試文", result["content"])

    def test_xlsx_uses_read_only_mode_and_closes_workbook(self):
        (self.root / "a.xlsx").write_bytes(b"x")
        calls = []
        fake = _fake_openpyxl({"S1": [("a", 1)], "S2": [("b", None)]}, calls)

        with mock.patch.dict(sys.modules, {"openpyxl": fake}):
            result = self._read("a.xlsx")

        self.assertEqual(("load_workbook", {"read_only": True, "data_only": True}), calls[0])
        self.assertEqual(2, calls.count("reset_dimensions"))
        self.assertEqual("close", calls[-1])
        self.assertEqual([["a", "1"]], result["sheets"][0]["rows"])
        self.assertFalse(result["truncated"])
        self.assertNotIn("original_bytes", result)

    def test_xlsx_closes_workbook_when_reading_fails(self):
        (self.root / "a.xlsx").write_bytes(b"x")
        calls = []
        fake = _fake_openpyxl({"S1": [("a",)]}, calls)

        with mock.patch.dict(sys.modules, {"openpyxl": fake}), \
                mock.patch.object(FakeWorksheet, "iter_rows", side_effect=RuntimeError("boom")):
            result = self._read("a.xlsx")

        self.assertEqual("boom", result["error"])
        self.assertEqual("close", calls[-1])

    def test_xlsx_rows_stop_at_budget(self):
        server._SNAPSHOT.update({"MCPLAB_MAX_RESPONSE_BYTES": "12"})
        (self.root / "a.xlsx").write_bytes(b"x")
        fake = _fake_openpyxl({"S1": [("aaaa", "bbbb"), ("cccc", "dddd"), ("eeee", "ffff")]}, [])

        with mock.patch.dict(sys.modules, {"openpyxl": fake}):
            result = self._read("a.xlsx")

        self.assertEqual([["aaaa", "bbbb"]], result["sheets"][0]["rows"])
        self.assertEqual(1, result["returned_rows"])
        self.assertTrue(result["truncated"])
        self.assertEqual(16, result["original_bytes"])
        self.assertFalse(result["original_bytes_exact"])
        self.assertEqual(12, result["max_response_bytes"])

    def test_pdf_text_stops_at_budget_and_reports_returned_pages(self):
        server._SNAPSHOT.update({"MCPLAB_MAX_RESPONSE_BYTES": "100"})
        (self.root / "a.pdf").write_bytes(b"x")
        pages = [FakePage("a" * 40, 0, []), FakePage("b" * 40, 0, []), FakePage("c" * 40, 0, [])]

        with mock.patch.dict(sys.modules, {"fitz": _fake_fitz(pages)}):
            result = self._read("a.pdf", include_images=False)

        self.assertEqual([40, 40, 20], [len(page["text"]) for page in result["pages"]])
        self.assertEqual("1-3", result["returned_pages"])
        self.assertTrue(result["truncated"])
        self.assertEqual(120, result["original_bytes"])
        self.assertFalse(result["original_bytes_exact"])

    def test_pdf_stops_when_budget_is_full_and_pages_remain(self):
        server._SNAPSHOT.update({"MCPLAB_MAX_RESPONSE_BYTES": "80"})
        (self.root / "a.pdf").write_bytes(b"x")
        pages = [FakePage("a" * 40, 0, []), FakePage("b" * 40, 0, []), FakePage("c" * 40, 0, [])]

        with mock.patch.dict(sys.modules, {"fitz": _fake_fitz(pages)}):
            result = self._read("a.pdf", include_images=False)

        self.assertEqual(2, len(result["pages"]))
        self.assertEqual("1-2", result["returned_pages"])
        self.assertEqual(3, result["total_pages"])
        self.assertTrue(result["truncated"])

    def test_pdf_exactly_filling_budget_is_not_truncated(self):
        server._SNAPSHOT.update({"MCPLAB_MAX_RESPONSE_BYTES": "80"})
        (self.root / "a.pdf").write_bytes(b"x")
        pages = [FakePage("a" * 40, 0, []), FakePage("b" * 40, 0, [])]

        with mock.patch.dict(sys.modules, {"fitz": _fake_fitz(pages)}):
            result = self._read("a.pdf", include_images=False)

        self.assertEqual("1-2", result["returned_pages"])
        self.assertNotIn("truncated", result)

    def test_pdf_images_stop_before_rendering_when_they_do_not_fit(self):
        server._SNAPSHOT.update({"MCPLAB_MAX_RESPONSE_BYTES": "1000"})
        (self.root / "a.pdf").write_bytes(b"x")
        calls = []
        pages = [FakePage("t" * 10, 300, calls) for _ in range(4)]

        with mock.patch.dict(sys.modules, {"fitz": _fake_fitz(pages)}):
            result = self._read("a.pdf")

        self.assertEqual(2, len(result["images"]))
        self.assertEqual(4, len(result["pages"]))
        self.assertEqual(3, calls.count("pixmap"))
        self.assertTrue(result["truncated"])
        self.assertEqual(10 * 4 + 422 * 3, result["original_bytes"])

    def test_pdf_images_are_not_rendered_when_page_cannot_fit_in_remaining_budget(self):
        server._SNAPSHOT.update({"MCPLAB_MAX_RESPONSE_BYTES": "1000"})
        (self.root / "a.pdf").write_bytes(b"x")
        calls = []
        # Letter 尺寸頁面以 1.5 倍渲染，PNG 不可能小於 1.1 KB，編碼後超過 1000 位元組的預算。
        pages = [FakePage("t" * 10, 300, calls, width=612, height=792) for _ in range(3)]

        with mock.patch.dict(sys.modules, {"fitz": _fake_fitz(pages)}):
            result = self._read("a.pdf")

        self.assertEqual(0, calls.count("pixmap"))
        self.assertEqual([], result["images"])
        self.assertEqual(3, len(result["pages"]))
        self.assertTrue(result["truncated"])
        self.assertGreater(result["original_bytes"], 10 * 3)

    def test_min_png_size_never_exceeds_a_real_png(self):
        import zlib

        width, height = 64, 48
        raw = b"".join(b"\x00" + b"\x00" * width * 3 for _ in range(height))

        def chunk(kind, data):
            body = kind + data
            return len(data).to_bytes(4, "big") + body + zlib.crc32(body).to_bytes(4, "big")

        header = width.to_bytes(4, "big") + height.to_bytes(4, "big") + bytes([8, 2, 0, 0, 0])
        png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")

        self.assertLessEqual(server._min_png_size(width, height), len(png))
        self.assertGreater(server._min_png_size(612 * 3 // 2, 792 * 3 // 2), 1000)

    def test_docx_paragraphs_stop_at_budget_and_skip_tables(self):
        server._SNAPSHOT.update({"MCPLAB_MAX_RESPONSE_BYTES": "50"})
        (self.root / "a.docx").write_bytes(b"x")
        fake = _fake_docx(["p" * 20, "", "q" * 20, "r" * 20], [[["cell"]]])

        with mock.patch.dict(sys.modules, {"docx": fake}):
            result = self._read("a.docx")

        self.assertEqual([20, 20, 10], [len(text) for text in result["paragraphs"]])
        self.assertEqual([], result["tables"])
        self.assertTrue(result["truncated"])

    def test_docx_table_rows_stop_at_budget(self):
        server._SNAPSHOT.update({"MCPLAB_MAX_RESPONSE_BYTES": "30"})
        (self.root / "a.docx").write_bytes(b"x")
        fake = _fake_docx(["hello"], [[["a" * 10, "b" * 10], ["c" * 10, "d" * 10], ["e" * 10, "f" * 10]]])

        with mock.patch.dict(sys.modules, {"docx": fake}):
            result = self._read("a.docx")

        self.assertEqual(["hello"], result["paragraphs"])
        self.assertEqual([[["a" * 10, "b" * 10]]], result["tables"])
        self.assertTrue(result["truncated"])

    def test_docx_images_skipped_when_they_do_not_fit(self):
        server._SNAPSHOT.update({"MCPLAB_MAX_RESPONSE_BYTES": "600"})
        (self.root / "a.docx").write_bytes(b"x")
        fake = _fake_docx(["hello"], [], images=(300, 300))

        with mock.patch.dict(sys.modules, {"docx": fake}):
            result = self._read("a.docx")

        self.assertEqual(["hello"], result["paragraphs"])
        self.assertEqual(1, len(result["images"]))
        self.assertTrue(result["truncated"])

    def test_image_larger_than_budget_is_not_encoded(self):
        try:
            from PIL import Image
        except ImportError:
            self.skipTest("Pillow is not installed")

        server._SNAPSHOT.update({"MCPLAB_MAX_RESPONSE_BYTES": "20"})
        Image.new("RGB", (8, 8), "red").save(self.root / "tiny.png")

        result = self._read("tiny.png")

        self.assertNotIn("image", result)
        self.assertEqual(8, result["width"])
        self.assertTrue(result["truncated"])
        self.assertGreater(result["original_bytes"], 20)

    def test_image_within_budget_is_encoded(self):
        try:
            from PIL import Image
        except ImportError:
            self.skipTest("Pillow is not installed")

        Image.new("RGB", (8, 8), "red").save(self.root / "tiny.png")

        result = self._read("tiny.png")

        self.assertTrue(result["image"].startswith("data:image/png;base64,"))
        self.assertNotIn("truncated", result)

    def test_parsing_is_serialized_by_lock(self):
        (self.root / "a.txt").write_text("a", encoding="utf-8")
        events = []
        entered = threading.Event()
        release = threading.Event()

        def fake_parse(path, budget):
            events.append("enter")
            entered.set()
            self.assertTrue(release.wait(timeout=5))
            events.append("exit")
            return {"type": "text", "images": []}

        with mock.patch.object(server, "_parse_text", side_effect=fake_parse):
            first = threading.Thread(target=server.read_document, args=("a.txt",))
            second = threading.Thread(target=server.read_document, args=("a.txt",))
            first.start()
            self.assertTrue(entered.wait(timeout=5))
            second.start()
            second.join(timeout=0.3)

            self.assertTrue(second.is_alive())
            self.assertEqual(["enter"], events)

            release.set()
            first.join(timeout=5)
            second.join(timeout=5)

        self.assertEqual(["enter", "exit", "enter", "exit"], events)

    def test_config_status_includes_new_settings_with_defaults(self):
        status = server.config_status()

        settings = {item["name"]: item for item in status["settings"]}
        self.assertEqual(1048576, settings["MCPLAB_MAX_RESPONSE_BYTES"]["effective"])
        self.assertEqual(1, settings["MCPLAB_MAX_RESPONSE_BYTES"]["min"])
        self.assertEqual(2097152, settings["MCPLAB_MAX_RESPONSE_BYTES"]["max"])
        self.assertEqual(20, settings["DOCUMENT_MAX_FILE_MB"]["effective"])
        self.assertEqual("MB", settings["DOCUMENT_MAX_FILE_MB"]["unit"])
        self.assertEqual(1048576, status["limits"]["max_response_bytes"])
        self.assertEqual(20, status["limits"]["document_max_file_mb"])

    def test_response_limit_changes_take_effect_without_restart(self):
        server._SNAPSHOT.update({"MCPLAB_MAX_RESPONSE_BYTES": "2048", "DOCUMENT_MAX_FILE_MB": "5"})
        self.assertEqual(2048, server._max_response_bytes())
        self.assertEqual(5, server._max_file_mb())

        server._SNAPSHOT.update({"MCPLAB_MAX_RESPONSE_BYTES": "4096", "DOCUMENT_MAX_FILE_MB": "7.5"})
        self.assertEqual(4096, server._max_response_bytes())
        self.assertEqual(7.5, server._max_file_mb())

    def test_response_limit_values_outside_range_are_clamped(self):
        for configured in ("999999999", "2097153", "2097152"):
            with self.subTest(configured=configured):
                server._SNAPSHOT.update({"MCPLAB_MAX_RESPONSE_BYTES": configured})
                self.assertEqual(2097152, server._max_response_bytes())

        server._SNAPSHOT.update({"MCPLAB_MAX_RESPONSE_BYTES": "0"})
        self.assertEqual(1048576, server._max_response_bytes())

    def test_read_document_writes_chinese_text_without_escapes(self):
        original = "\n".join(
            f"第{index}行：這是用來驗證回應編碼的中文內容，包含全形標點與數字 {index}。"
            for index in range(1, 61)
        )
        (self.root / "chinese.txt").write_text(original, encoding="utf-8")

        text = server.read_document("chinese.txt")
        payload = json.loads(text)

        self.assertIn("這是用來驗證回應編碼的中文內容", text)
        self.assertNotIn("\\u", text)
        self.assertEqual(original, payload["content"])

    def test_search_documents_skips_files_over_file_limit(self):
        server._SNAPSHOT.update({"DOCUMENT_MAX_FILE_MB": "0.1"})
        (self.root / "big.pdf").write_bytes(b"x" * 200000)
        (self.root / "other.md").write_text("nothing here", encoding="utf-8")

        result = server.search_documents("too large", recursive=False)

        self.assertEqual(0, result["returned_count"])
        self.assertEqual([], result["results"])

        (self.root / "match.md").write_text("a line that is too large to read", encoding="utf-8")

        result = server.search_documents("too large", recursive=False)

        self.assertEqual(["/mnt/c/docs/match.md"], [item["path"] for item in result["results"]])


if __name__ == "__main__":
    unittest.main()
