import os
import sys
import threading
import time
from pathlib import Path


class DotEnvParser:
    @staticmethod
    def parse(content: str) -> dict[str, str]:
        if content.startswith("\ufeff"):
            content = content[1:]

        result: dict[str, str] = {}

        for line_number, raw_line in enumerate(content.splitlines(), start=1):
            line = raw_line.strip()

            if not line or line.startswith("#"):
                continue

            if line.startswith("export "):
                line = line[7:].lstrip()

            if "=" not in line:
                raise ValueError(f"Invalid .env syntax at line {line_number}: missing '='.")

            key, raw_value = line.split("=", 1)
            key = key.strip()

            if not key or not (key[0].isalpha() or key[0] == "_") or not all(ch.isalnum() or ch == "_" for ch in key):
                raise ValueError(f"Invalid .env syntax at line {line_number}: invalid key '{key}'.")

            result[key] = DotEnvParser._parse_value(raw_value.strip(), line_number)

        return result

    @staticmethod
    def _parse_value(value: str, line_number: int) -> str:
        if not value:
            return ""

        if value.startswith('"'):
            return DotEnvParser._parse_double_quoted(value, line_number)

        if value.startswith("'"):
            return DotEnvParser._parse_single_quoted(value, line_number)

        return value

    @staticmethod
    def _parse_double_quoted(value: str, line_number: int) -> str:
        if len(value) < 2 or value[-1] != '"':
            raise ValueError(f"Invalid .env syntax at line {line_number}: unterminated double-quoted value.")

        output: list[str] = []
        escaped = False

        for ch in value[1:-1]:
            if escaped:
                output.append({
                    "n": "\n",
                    "r": "\r",
                    "t": "\t",
                    '"': '"',
                    "\\": "\\",
                }.get(ch, ch))
                escaped = False
                continue

            if ch == "\\":
                escaped = True
                continue

            output.append(ch)

        if escaped:
            raise ValueError(f"Invalid .env syntax at line {line_number}: dangling escape sequence.")

        return "".join(output)

    @staticmethod
    def _parse_single_quoted(value: str, line_number: int) -> str:
        if len(value) < 2 or value[-1] != "'":
            raise ValueError(f"Invalid .env syntax at line {line_number}: unterminated single-quoted value.")

        return value[1:-1]


class DotEnvSnapshot:
    def __init__(self, initial: dict[str, str] | None = None, baseline: dict[str, str] | None = None):
        self._lock = threading.RLock()
        self._baseline = dict(baseline if baseline is not None else initial or {})
        self._current = dict(initial or {})

    @classmethod
    def from_environment(cls) -> "DotEnvSnapshot":
        values = {str(key): str(value) for key, value in os.environ.items()}
        return cls(values, values)

    def update(self, values: dict[str, str]) -> None:
        with self._lock:
            next_values = dict(self._baseline)
            next_values.update(values)
            self._current = next_values

    def current(self) -> dict[str, str]:
        with self._lock:
            return dict(self._current)

    def get_first(self, names: list[str]) -> str | None:
        with self._lock:
            for name in names:
                value = self._current.get(name)
                if value is not None and value.strip():
                    return value
        return None


class DotEnvWatcher:
    def __init__(self, path: str | Path, snapshot: DotEnvSnapshot, interval_seconds: float = 0.5):
        self._path = Path(path)
        self._snapshot = snapshot
        self._interval_seconds = interval_seconds
        self._stop_event = threading.Event()
        self._thread: threading.Thread | None = None
        self._fingerprint: tuple[bool, int, int] | None = None
        self._last_error: str | None = None

    def start(self) -> None:
        if self._thread is not None:
            return

        self._reload_if_changed(force=True)
        self._thread = threading.Thread(target=self._run, name="dotenv-watcher", daemon=True)
        self._thread.start()

    def stop(self, timeout: float = 2.0) -> None:
        self._stop_event.set()

        if self._thread is not None:
            self._thread.join(timeout=timeout)
            self._thread = None

    def _run(self) -> None:
        while not self._stop_event.wait(self._interval_seconds):
            self._reload_if_changed(force=False)

    def _reload_if_changed(self, force: bool) -> None:
        fingerprint = self._get_fingerprint()

        if not force and fingerprint == self._fingerprint:
            return

        self._fingerprint = fingerprint

        if not fingerprint[0]:
            return

        try:
            values = DotEnvParser.parse(self._path.read_text(encoding="utf-8-sig"))
        except Exception as exc:
            message = str(exc)
            if message != self._last_error:
                print(f"Warning: .env parse failed: {message}", file=sys.stderr)
                self._last_error = message
            return

        self._last_error = None
        self._snapshot.update(values)

    def _get_fingerprint(self) -> tuple[bool, int, int]:
        try:
            stat = self._path.stat()
        except FileNotFoundError:
            return False, 0, 0

        return True, int(stat.st_mtime_ns), int(stat.st_size)
