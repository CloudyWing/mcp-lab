import tempfile
import time
import unittest
from pathlib import Path
import sys

MODULE_DIR = Path(__file__).resolve().parents[1]
if str(MODULE_DIR) not in sys.path:
    sys.path.insert(0, str(MODULE_DIR))

from dotenv_watcher import DotEnvParser, DotEnvSnapshot, DotEnvWatcher


class DotEnvWatcherTests(unittest.TestCase):
    def test_parser_supports_compose_subset(self):
        content = (
            "\ufeff# comment\n"
            "KEY=value\n"
            "EMPTY=\n"
            "QUOTED=\"line\\nvalue\\twith \\\"quotes\\\" and \\\\ slash\"\n"
            "SINGLE='literal\\nvalue'\n"
            "export EXPORTED=ok\n"
            "INLINE=value # not comment\n"
            "INTERPOLATION=${OTHER}\n"
        )

        actual = DotEnvParser.parse(content)

        self.assertEqual("value", actual["KEY"])
        self.assertEqual("", actual["EMPTY"])
        self.assertEqual("line\nvalue\twith \"quotes\" and \\ slash", actual["QUOTED"])
        self.assertEqual("literal\\nvalue", actual["SINGLE"])
        self.assertEqual("ok", actual["EXPORTED"])
        self.assertEqual("value # not comment", actual["INLINE"])
        self.assertEqual("${OTHER}", actual["INTERPOLATION"])

    def test_watcher_reloads_changed_file(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / ".env"
            path.write_text("DOCUMENT_LIST_DEFAULT_LIMIT=10\n", encoding="utf-8")
            snapshot = DotEnvSnapshot({})
            watcher = DotEnvWatcher(path, snapshot, interval_seconds=0.05)

            try:
                watcher.start()
                self._wait_until(lambda: snapshot.current().get("DOCUMENT_LIST_DEFAULT_LIMIT") == "10")

                path.write_text("DOCUMENT_LIST_DEFAULT_LIMIT=20\n", encoding="utf-8")

                self._wait_until(lambda: snapshot.current().get("DOCUMENT_LIST_DEFAULT_LIMIT") == "20")
                self.assertEqual("20", snapshot.current()["DOCUMENT_LIST_DEFAULT_LIMIT"])
            finally:
                watcher.stop()

    def test_watcher_keeps_previous_snapshot_when_parse_fails(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / ".env"
            path.write_text("DOCUMENT_LIST_DEFAULT_LIMIT=10\n", encoding="utf-8")
            snapshot = DotEnvSnapshot({})
            watcher = DotEnvWatcher(path, snapshot, interval_seconds=0.05)

            try:
                watcher.start()
                self._wait_until(lambda: snapshot.current().get("DOCUMENT_LIST_DEFAULT_LIMIT") == "10")

                path.write_text("DOCUMENT_LIST_DEFAULT_LIMIT=\"unterminated\n", encoding="utf-8")
                time.sleep(0.15)

                self.assertEqual("10", snapshot.current()["DOCUMENT_LIST_DEFAULT_LIMIT"])
            finally:
                watcher.stop()

    def test_watcher_retains_baseline_values_when_file_omits_key(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / ".env"
            path.write_text("DOCUMENT_MAX_IMAGES=25\n", encoding="utf-8")
            snapshot = DotEnvSnapshot(
                {"DOCUMENT_MAX_IMAGES": "20", "DOCUMENT_MAX_TEXT_MB": "10"},
                {"DOCUMENT_MAX_IMAGES": "20", "DOCUMENT_MAX_TEXT_MB": "10"},
            )
            watcher = DotEnvWatcher(path, snapshot, interval_seconds=0.05)

            try:
                watcher.start()

                self._wait_until(lambda: snapshot.current().get("DOCUMENT_MAX_IMAGES") == "25")

                self.assertEqual("10", snapshot.current()["DOCUMENT_MAX_TEXT_MB"])
            finally:
                watcher.stop()

    def _wait_until(self, predicate):
        deadline = time.monotonic() + 2

        while time.monotonic() < deadline:
            if predicate():
                return
            time.sleep(0.025)

        self.fail("condition was not satisfied before timeout")


if __name__ == "__main__":
    unittest.main()
