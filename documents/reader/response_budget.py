"""
Reader 單次工具呼叫的回應位元組預算。

預算以回應內容的 UTF-8 位元組數計量（文字為 UTF-8 長度，圖片為編碼後的字串長度），
不含 JSON 跳脫字元與 MCP 協定外層。
"""

from typing import Any


def utf8_size(text: str | None) -> int:
    """回傳字串以 UTF-8 編碼後的位元組數，None 與空字串為 0。"""
    if not text:
        return 0
    return len(text.encode("utf-8", errors="replace"))


class ResponseBudget:
    """單次工具呼叫的位元組累計器，非執行緒安全，每次呼叫各自建立。"""

    def __init__(self, max_bytes: int) -> None:
        if max_bytes <= 0:
            raise ValueError("max_bytes must be greater than 0.")

        self._max_bytes = int(max_bytes)
        self._used = 0
        self._observed = 0
        self._exhausted = False
        self._closed = False
        self._original_bytes: int | None = None
        self._original_bytes_exact = False

    @property
    def max_bytes(self) -> int:
        return self._max_bytes

    @property
    def used(self) -> int:
        """已接受的位元組數。"""
        return self._used

    @property
    def observed(self) -> int:
        """已接受，加上被拒絕或被切掉的位元組數。"""
        return self._observed

    @property
    def remaining(self) -> int:
        return self._max_bytes - self._used

    @property
    def exhausted(self) -> bool:
        """任何一次提供被拒絕或被切掉，或呼叫過 mark_incomplete() 後為 True。"""
        return self._exhausted

    @property
    def closed(self) -> bool:
        """文字已被切掉、已呼叫 mark_incomplete()，或沒有剩餘空間，後續內容不再收錄。"""
        return self._closed or self.remaining <= 0

    def take_text(self, text: str) -> str:
        """接受 text 的完整內容；放不下時只接受能容納的 UTF-8 前綴，並切在字元邊界。"""
        if not text:
            return text

        size = utf8_size(text)
        if size <= self.remaining:
            self._used += size
            self._observed += size
            return text

        self._observed += size
        self._exhausted = True
        self._closed = True

        allowed = self.remaining
        if allowed <= 0:
            return ""

        prefix = text.encode("utf-8", errors="replace")[:allowed].decode("utf-8", errors="ignore")
        self._used += utf8_size(prefix)
        return prefix

    def try_add(self, size: int) -> bool:
        """全有或全無地接受 size 個位元組；放不下時回傳 False 並標示已用完。"""
        if size < 0:
            raise ValueError("size must not be negative.")

        self._observed += size
        if size <= self.remaining:
            self._used += size
            return True

        self._exhausted = True
        return False

    def mark_incomplete(self, original_bytes: int | None = None, original_bytes_exact: bool = False) -> None:
        """標示內容不完整並停止收錄後續內容；已知原始大小時一併記錄。"""
        self._exhausted = True
        self._closed = True
        if original_bytes is not None:
            self._original_bytes = int(original_bytes)
            self._original_bytes_exact = original_bytes_exact

    def fields(self) -> dict[str, Any]:
        """回傳截斷時要附加在回應末端的欄位。"""
        if self._original_bytes is not None:
            original_bytes, exact = self._original_bytes, self._original_bytes_exact
        else:
            original_bytes, exact = self._observed, False

        return {
            "truncated": True,
            "original_bytes": original_bytes,
            "original_bytes_exact": exact,
            "max_response_bytes": self._max_bytes,
        }
