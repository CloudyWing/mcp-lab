#!/usr/bin/env python3
"""
mcp-reader — Document & image reader MCP server.
"""

import base64
import chardet
import codecs
import hmac
import json
import math
import os
import re
import threading
from collections import deque
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from dotenv_watcher import DotEnvSnapshot, DotEnvWatcher
from mcp.server.fastmcp import FastMCP
from mcp.server.fastmcp.server import Settings as FastMCPSettings
from mcp.types import CallToolResult, TextContent
from response_budget import ResponseBudget, utf8_size

FastMCPSettings.model_config["env_file"] = None

MOUNT_ROOT = Path("/host_root").resolve()
RAW_HOST_MOUNT_ROOT = os.environ.get("DOCUMENT_HOST_MOUNT_ROOT", "/").strip()
DOTENV_PATH = os.environ.get("MCPLAB_DOTENV_PATH", "/app/config/service.env").strip() or "/app/config/service.env"
_SNAPSHOT = DotEnvSnapshot.from_environment()
_WATCHER = DotEnvWatcher(DOTENV_PATH, _SNAPSHOT)

DOCUMENT_EXTS = {".pdf", ".pptx", ".docx", ".xlsx"}
IMAGE_EXTS    = {".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif", ".svg"}
TEXT_EXTS     = {".txt", ".md", ".csv", ".json", ".xml", ".yaml", ".yml", ".log", ".ini", ".toml", ".html"}
SUPPORTED_EXTENSIONS = DOCUMENT_EXTS | IMAGE_EXTS | TEXT_EXTS

_ERROR_SCAN_LIMIT = 64 * 1024


def _is_error_payload(text: str) -> bool:
    """Returns whether a tool's JSON text reports a failure through a top-level "error" field.

    Error results are small, so large results (documents with images) are not parsed a second time.
    """
    if len(text) > _ERROR_SCAN_LIMIT and not text.startswith('{"error"'):
        return False

    try:
        payload = json.loads(text)
    except ValueError:
        return False
    return isinstance(payload, dict) and bool(payload.get("error"))


class ReaderMCP(FastMCP):
    """FastMCP that also sets the MCP isError flag when a tool result reports an error."""

    async def call_tool(self, name: str, arguments: dict[str, Any]) -> Any:
        result = await super().call_tool(name, arguments)
        content, structured = result if isinstance(result, tuple) else (result, None)

        if isinstance(content, dict):
            return result

        blocks = list(content)
        if len(blocks) == 1 and isinstance(blocks[0], TextContent) and _is_error_payload(blocks[0].text):
            return CallToolResult(content=blocks, structuredContent=structured, isError=True)

        return result


mcp = ReaderMCP(
    "mcp-reader",
    host="0.0.0.0",
    port=int(os.environ.get("PORT", 8080)),
)


def _normalize_mount_root(raw_root: str) -> str:
    return _normalize_absolute_host_path(raw_root) or "/"


def _normalize_absolute_host_path(raw_path: str) -> str | None:
    normalized = raw_path.strip().strip('"').strip("'").replace("\\", "/")
    if not normalized:
        return None

    windows_path = re.match(r"^(?P<drive>[A-Za-z]):/?(?P<tail>.*)$", normalized)
    if windows_path:
        drive = windows_path.group("drive").lower()
        tail = windows_path.group("tail").strip("/")
        return f"/mnt/{drive}/{tail}".rstrip("/")

    desktop_path = re.match(r"^/run/desktop/mnt/host/(?P<drive>[A-Za-z])(?:/(?P<tail>.*))?$", normalized)
    if desktop_path:
        drive = desktop_path.group("drive").lower()
        tail = (desktop_path.group("tail") or "").strip("/")
        return f"/mnt/{drive}/{tail}".rstrip("/")

    if normalized.startswith("/"):
        return normalized.rstrip("/") or "/"

    return None


HOST_MOUNT_ROOT = _normalize_mount_root(RAW_HOST_MOUNT_ROOT)


def _clamp_int(value: int, default: int, minimum: int, maximum: int) -> int:
    try:
        parsed = int(value)
    except (TypeError, ValueError):
        return default

    if parsed < minimum:
        return default
    return min(parsed, maximum)


def _setting_int(names: list[str], default: int, minimum: int, maximum: int) -> int:
    value = _SNAPSHOT.get_first(names)
    return _clamp_int(value, default, minimum, maximum)


def _setting_float(names: list[str], default: float, minimum: float, maximum: float) -> float:
    value = _SNAPSHOT.get_first(names)

    try:
        parsed = float(value) if value is not None else default
    except (TypeError, ValueError):
        return default

    if parsed < minimum:
        return default
    return min(parsed, maximum)


@dataclass(frozen=True)
class ReaderSetting:
    """Reader 的 runtime 設定定義；max_value 可指向另一個設定名稱，表示上限由該設定決定。"""

    name: str
    description: str
    unit: str
    default_value: float
    min_value: float
    max_value: float | str
    fallbacks: tuple[str, ...] = ()
    is_float: bool = False


READER_SETTINGS: dict[str, ReaderSetting] = {
    setting.name: setting
    for setting in (
        ReaderSetting("DOCUMENT_MAX_IMAGES", "單一文件最多回傳的圖片數", "images", 20, 1, 200, ("MAX_IMAGES",)),
        ReaderSetting("DOCUMENT_MAX_TEXT_MB", "文字檔讀取上限，0 表示不限制", "MB", 10, 0, 1024, ("MAX_TEXT_MB",), True),
        ReaderSetting("DOCUMENT_LIST_MAX_LIMIT", "list_documents 可要求的筆數上限", "files", 1000, 1, 100000),
        ReaderSetting("DOCUMENT_LIST_DEFAULT_LIMIT", "list_documents 預設筆數", "files", 100, 1, "DOCUMENT_LIST_MAX_LIMIT"),
        ReaderSetting("DOCUMENT_LIST_MAX_DEPTH", "list_documents 可要求的遞迴深度上限", "levels", 10, 0, 100),
        ReaderSetting("DOCUMENT_LIST_DEFAULT_DEPTH", "list_documents 預設遞迴深度", "levels", 2, 0, "DOCUMENT_LIST_MAX_DEPTH"),
        ReaderSetting("DOCUMENT_SEARCH_MAX_LIMIT", "search_documents 可要求的結果上限", "results", 200, 1, 100000),
        ReaderSetting("DOCUMENT_SEARCH_DEFAULT_LIMIT", "search_documents 預設結果數", "results", 20, 1, "DOCUMENT_SEARCH_MAX_LIMIT"),
        ReaderSetting("DOCUMENT_SEARCH_MAX_SCAN_LIMIT", "search_documents 可要求的掃描檔案上限", "files", 2000, 1, 1000000),
        ReaderSetting("DOCUMENT_SEARCH_DEFAULT_SCAN_LIMIT", "search_documents 預設掃描檔案數", "files", 200, 1, "DOCUMENT_SEARCH_MAX_SCAN_LIMIT"),
        ReaderSetting("DOCUMENT_XLSX_MAX_ROWS_LIMIT", "XLSX 可要求的列數上限", "rows", 10000, 1, 1000000),
        ReaderSetting("DOCUMENT_XLSX_MAX_ROWS", "XLSX 預設讀取列數", "rows", 1000, 1, "DOCUMENT_XLSX_MAX_ROWS_LIMIT"),
        ReaderSetting("DOCUMENT_XLSX_MAX_CELLS_LIMIT", "XLSX 可要求的儲存格上限", "cells", 200000, 1, 10000000),
        ReaderSetting("DOCUMENT_XLSX_MAX_CELLS", "XLSX 預設讀取儲存格數", "cells", 20000, 1, "DOCUMENT_XLSX_MAX_CELLS_LIMIT"),
        ReaderSetting("MCPLAB_MAX_RESPONSE_BYTES", "單次工具回應內容的位元組上限，超過時截斷", "bytes", 1048576, 1, 2097152),
        ReaderSetting("DOCUMENT_MAX_FILE_MB", "可解析的檔案大小上限，超過時回錯誤", "MB", 20, 0.1, 1024, (), True),
    )
}


def _setting_max(setting: ReaderSetting) -> float:
    return _read_setting(setting.max_value) if isinstance(setting.max_value, str) else setting.max_value


def _read_setting(name: str) -> float:
    setting = READER_SETTINGS[name]
    names = [setting.name, *setting.fallbacks]
    maximum = _setting_max(setting)
    if setting.is_float:
        return _setting_float(names, setting.default_value, setting.min_value, maximum)
    return _setting_int(names, int(setting.default_value), int(setting.min_value), int(maximum))


def _max_images() -> int:
    return _read_setting("DOCUMENT_MAX_IMAGES")


def _max_text_mb() -> float:
    return _read_setting("DOCUMENT_MAX_TEXT_MB")


def _list_max_limit() -> int:
    return _read_setting("DOCUMENT_LIST_MAX_LIMIT")


def _list_default_limit() -> int:
    return _read_setting("DOCUMENT_LIST_DEFAULT_LIMIT")


def _list_max_depth() -> int:
    return _read_setting("DOCUMENT_LIST_MAX_DEPTH")


def _list_default_depth() -> int:
    return _read_setting("DOCUMENT_LIST_DEFAULT_DEPTH")


def _search_max_limit() -> int:
    return _read_setting("DOCUMENT_SEARCH_MAX_LIMIT")


def _search_default_limit() -> int:
    return _read_setting("DOCUMENT_SEARCH_DEFAULT_LIMIT")


def _search_max_scan_limit() -> int:
    return _read_setting("DOCUMENT_SEARCH_MAX_SCAN_LIMIT")


def _search_default_scan_limit() -> int:
    return _read_setting("DOCUMENT_SEARCH_DEFAULT_SCAN_LIMIT")


def _xlsx_max_rows_limit() -> int:
    return _read_setting("DOCUMENT_XLSX_MAX_ROWS_LIMIT")


def _xlsx_default_max_rows() -> int:
    return _read_setting("DOCUMENT_XLSX_MAX_ROWS")


def _xlsx_max_cells_limit() -> int:
    return _read_setting("DOCUMENT_XLSX_MAX_CELLS_LIMIT")


def _xlsx_default_max_cells() -> int:
    return _read_setting("DOCUMENT_XLSX_MAX_CELLS")


def _max_response_bytes() -> int:
    return _read_setting("MCPLAB_MAX_RESPONSE_BYTES")


def _max_file_mb() -> float:
    return _read_setting("DOCUMENT_MAX_FILE_MB")


def _map_host_path(source_path: str) -> Path | None:
    if source_path == HOST_MOUNT_ROOT:
        return MOUNT_ROOT

    prefix = f"{HOST_MOUNT_ROOT}/"
    if source_path.startswith(prefix):
        tail = source_path.removeprefix(prefix).strip("/")
        return MOUNT_ROOT / tail if tail else MOUNT_ROOT

    return None


def _resolve_path(path: str = ".") -> Path:
    normalized = path.strip().strip('"').strip("'")
    if not normalized or normalized == ".":
        target = MOUNT_ROOT
    elif normalized.startswith("/host_root/") or normalized == "/host_root":
        target = Path(normalized).resolve()
    else:
        mapped_target: Path | None = None
        host_style_path = _normalize_absolute_host_path(normalized)

        if host_style_path is not None:
            mapped_target = _map_host_path(host_style_path)
        else:
            normalized = normalized.replace("\\", "/")
            mapped_target = (MOUNT_ROOT / normalized).resolve()

        if mapped_target is None:
            raise ValueError(f"Path must stay under DOCUMENT_HOST_MOUNT_ROOT: {HOST_MOUNT_ROOT}")
        target = mapped_target.resolve()

    try:
        target.relative_to(MOUNT_ROOT)
    except ValueError as exc:
        raise ValueError(f"Path must stay under DOCUMENT_HOST_MOUNT_ROOT: {HOST_MOUNT_ROOT}") from exc
    return target


def _relative_path_for(file_path: Path) -> str:
    try:
        relative_path = file_path.relative_to(MOUNT_ROOT).as_posix()
        if not relative_path or relative_path == ".":
            return HOST_MOUNT_ROOT
        if HOST_MOUNT_ROOT == "/":
            return f"/{relative_path}"
        return f"{HOST_MOUNT_ROOT}/{relative_path}"
    except Exception:
        return str(file_path.name)


def _candidate_host_path(file_path: Path) -> str:
    try:
        return _relative_path_for(file_path)
    except Exception:
        return str(file_path)


def _file_category(file_path: Path) -> str:
    ext = file_path.suffix.lower()
    if ext in DOCUMENT_EXTS:
        return "document"
    if ext in IMAGE_EXTS:
        return "image"
    return "text"


def _document_file_info(file_path: Path) -> dict:
    return {
        "name":     file_path.name,
        "path":     _relative_path_for(file_path),
        "type":     file_path.suffix.lstrip(".").lower(),
        "category": _file_category(file_path),
        "size_kb":  round(file_path.stat().st_size / 1024, 1),
    }


def _list_supported_files(target: Path, recursive: bool, max_depth: int, limit: int) -> tuple[list[dict], bool, bool]:
    if target.is_file():
        if target.suffix.lower() not in SUPPORTED_EXTENSIONS:
            return [], False, False
        return [_document_file_info(target)], False, False

    if not target.is_dir():
        return [], False, False

    files: list[dict] = []
    depth_limited = False
    queue = deque([(target, 0)])

    while queue:
        current, depth = queue.popleft()

        try:
            entries = sorted(current.iterdir(), key=lambda item: item.name.lower())
        except OSError:
            continue

        for entry in entries:
            if entry.is_file() and entry.suffix.lower() in SUPPORTED_EXTENSIONS:
                if len(files) >= limit:
                    return files, True, depth_limited
                files.append(_document_file_info(entry))
                continue

            if recursive and entry.is_dir() and not entry.is_symlink():
                if depth < max_depth:
                    queue.append((entry, depth + 1))
                else:
                    depth_limited = True

    return files, False, depth_limited


def _find_path_candidates(raw_path: str, limit: int = 10) -> list[str]:
    try:
        requested = _resolve_path(raw_path)
    except ValueError:
        return []

    if requested.exists():
        return [_candidate_host_path(requested)]

    parent = requested.parent
    name_prefix = requested.name.lower()
    if not parent.exists():
        return []

    candidates: list[str] = []
    for file_path in sorted(parent.iterdir(), key=lambda item: item.name.lower()):
        if not file_path.is_file():
            continue
        if file_path.suffix.lower() not in SUPPORTED_EXTENSIONS:
            continue
        if name_prefix and not file_path.name.lower().startswith(name_prefix):
            continue
        candidates.append(_candidate_host_path(file_path))
        if len(candidates) >= limit:
            break
    return candidates


def _img_to_b64(img_bytes: bytes, fmt: str = "png") -> str:
    return f"data:image/{fmt};base64," + base64.b64encode(img_bytes).decode()


def _img_b64_size(blob_size: int, fmt: str = "png") -> int:
    """回傳 _img_to_b64 產生的字串長度（皆為 ASCII），供編碼前以預算判斷。"""
    return len(f"data:image/{fmt};base64,") + ((blob_size + 2) // 3) * 4


_PDF_IMAGE_SCALE = 1.5
_PNG_FIXED_BYTES = 57
_DEFLATE_MAX_RATIO = 1032


def _min_png_size(width_px: int, height_px: int) -> int:
    """回傳寬高為 width_px 乘 height_px 的 PNG 不可能小於的位元組數，供渲染前判斷圖片必然放不下。

    以每像素 1 位元組的原始掃描線資料，除以 deflate 的最大壓縮比 1032，再加 PNG 固定結構，
    因此任何色彩類型的實際輸出都不會小於此值。
    """
    raw_bytes = max(height_px, 1) * (1 + max(width_px, 1))
    return _PNG_FIXED_BYTES + raw_bytes // _DEFLATE_MAX_RATIO


# FastMCP 1.30.0 的 Tool.run 在事件迴圈執行緒直接呼叫同步工具函式；鎖在直接呼叫與 thread pool 兩種執行緒模型下都能序列化解析。
_PARSE_LOCK = threading.Lock()


def _parse_pdf(path: Path, include_images: bool, start_page: int, end_page: int, budget: ResponseBudget) -> dict:
    import fitz

    document = fitz.open(str(path))
    total = len(document)
    p_start = max(0, start_page - 1)
    p_end   = (min(end_page, total) - 1) if end_page > 0 else (total - 1)
    pages, images_b64 = [], []
    images_open = include_images
    for i in range(p_start, p_end + 1):
        if budget.closed:
            budget.mark_incomplete()
            break

        page     = document[i]
        page_num = i + 1
        text     = budget.take_text(page.get_text("text").strip())
        pages.append({"page": page_num, "text": text})
        if images_open and len(images_b64) < _max_images():
            if budget.closed:
                images_open = False
                budget.mark_incomplete()
                continue

            min_image_size = _img_b64_size(_min_png_size(
                math.ceil(page.rect.width * _PDF_IMAGE_SCALE),
                math.ceil(page.rect.height * _PDF_IMAGE_SCALE),
            ))
            if min_image_size > budget.remaining:
                # 圖片最小可能大小已超過剩餘預算，不渲染 pixmap 與 PNG；以最小大小提供預算，必然被拒絕並記錄被略過的大小。
                budget.try_add(min_image_size)
                images_open = False
                continue

            matrix = fitz.Matrix(_PDF_IMAGE_SCALE, _PDF_IMAGE_SCALE)
            pixmap = page.get_pixmap(matrix=matrix)
            png_bytes = pixmap.tobytes("png")
            if budget.try_add(_img_b64_size(len(png_bytes))):
                images_b64.append({"page": page_num, "image": _img_to_b64(png_bytes)})
            else:
                images_open = False
    document.close()
    returned_end = pages[-1]["page"] if pages else p_end + 1
    return {
        "type": "pdf",
        "total_pages": total,
        "returned_pages": f"{p_start + 1}-{returned_end}",
        "pages": pages,
        "images": images_b64,
    }


def _parse_pptx(path: Path, include_images: bool, start_page: int, end_page: int, budget: ResponseBudget) -> dict:
    from pptx import Presentation
    from pptx.enum.shapes import MSO_SHAPE_TYPE

    prs   = Presentation(str(path))
    total = len(prs.slides)
    s_start = max(0, start_page - 1)
    s_end   = (min(end_page, total) - 1) if end_page > 0 else (total - 1)
    slides, images_b64 = [], []
    images_open = include_images
    for i in range(s_start, s_end + 1):
        if budget.closed:
            budget.mark_incomplete()
            break

        slide     = prs.slides[i]
        slide_num = i + 1
        texts, slide_images = [], []
        for shape in slide.shapes:
            if budget.closed:
                budget.mark_incomplete()
                break

            if getattr(shape, "has_text_frame", False):
                for para in shape.text_frame.paragraphs:
                    line = "".join(run.text for run in para.runs).strip()
                    if line:
                        texts.append(budget.take_text(line))
                        if budget.closed:
                            break
            if images_open and len(images_b64) < _max_images() and getattr(shape, "shape_type", None) == MSO_SHAPE_TYPE.PICTURE:
                try:
                    img_bytes = shape.image.blob
                    img_fmt   = shape.image.ext.lower().lstrip(".")
                    if img_fmt == "jpg":
                        img_fmt = "jpeg"
                    # 同一個編碼字串會同時出現在 slides 與頂層 images，回應內容含兩份。
                    if budget.try_add(2 * _img_b64_size(len(img_bytes), img_fmt)):
                        encoded = _img_to_b64(img_bytes, img_fmt)
                        slide_images.append(encoded)
                        images_b64.append({"slide": slide_num, "image": encoded})
                    else:
                        images_open = False
                except Exception:
                    pass
        title = budget.take_text(slide.shapes.title.text if slide.shapes.title else "")
        slides.append({"slide": slide_num, "title": title, "texts": texts, "images": slide_images})
    returned_end = slides[-1]["slide"] if slides else s_end + 1
    return {
        "type": "pptx",
        "total_slides": total,
        "returned_slides": f"{s_start + 1}-{returned_end}",
        "slides": slides,
        "images": images_b64,
    }


def _parse_docx(path: Path, include_images: bool, budget: ResponseBudget) -> dict:
    import docx

    doc        = docx.Document(str(path))
    paragraphs = []
    for paragraph in doc.paragraphs:
        text = paragraph.text.strip()
        if not text:
            continue
        if budget.closed:
            budget.mark_incomplete()
            break
        paragraphs.append(budget.take_text(text))

    tables = []
    for table in doc.tables:
        if budget.closed:
            budget.mark_incomplete()
            break

        rows = []
        for row in table.rows:
            cells = [cell.text.strip() for cell in row.cells]
            if not budget.try_add(sum(utf8_size(cell) for cell in cells)):
                budget.mark_incomplete()
                break
            rows.append(cells)
        tables.append(rows)

    images_b64 = []
    if include_images:
        for rel in doc.part.rels.values():
            if "image" in rel.reltype and len(images_b64) < _max_images():
                try:
                    img_bytes = rel.target_part.blob
                    img_fmt   = rel.target_part.content_type.split("/")[-1]
                    if img_fmt == "jpg":
                        img_fmt = "jpeg"
                    if not budget.try_add(_img_b64_size(len(img_bytes), img_fmt)):
                        break
                    images_b64.append({"index": len(images_b64) + 1, "image": _img_to_b64(img_bytes, img_fmt)})
                except Exception:
                    pass
    return {"type": "docx", "paragraphs": paragraphs, "tables": tables, "images": images_b64}


def _parse_xlsx(path: Path, max_rows: int, max_cells: int, budget: ResponseBudget) -> dict:
    from openpyxl import load_workbook

    wb = load_workbook(str(path), read_only=True, data_only=True)
    try:
        total_sheet_count = len(wb.sheetnames)
        sheets = []
        total_rows = 0
        total_cells = 0
        truncated = False

        for name in wb.sheetnames:
            ws = wb[name]
            ws.reset_dimensions()
            rows = []
            for row in ws.iter_rows(values_only=True):
                if total_rows >= max_rows or total_cells + len(row) > max_cells:
                    truncated = True
                    break

                values = [str(c) if c is not None else "" for c in row]
                if not budget.try_add(sum(utf8_size(value) for value in values)):
                    budget.mark_incomplete()
                    truncated = True
                    break

                rows.append(values)
                total_rows += 1
                total_cells += len(row)

            sheets.append({"sheet": name, "rows": rows})

            if truncated:
                break
    finally:
        wb.close()

    return {
        "type": "xlsx",
        "sheet_count": total_sheet_count,
        "returned_sheet_count": len(sheets),
        "returned_rows": total_rows,
        "truncated": truncated,
        "sheets": sheets,
        "images": [],
    }


def _parse_image(path: Path, budget: ResponseBudget) -> dict:
    from PIL import Image
    import io

    size_bytes = path.stat().st_size
    with Image.open(str(path)) as img:
        width, height = img.size
        fmt      = img.format or path.suffix.lstrip(".").upper()
        mime_fmt = fmt.lower()
        if mime_fmt in ("jpg",):
            mime_fmt = "jpeg"

        image_b64 = None
        if size_bytes <= budget.remaining:
            buf = io.BytesIO()
            save_fmt = "PNG" if fmt in ("", "ICO", "ICNS") else fmt
            try:
                img.save(buf, format=save_fmt)
            except Exception:
                img.convert("RGB").save(buf, format="PNG")
                mime_fmt = "png"
            blob = buf.getvalue()
            if budget.try_add(_img_b64_size(len(blob), mime_fmt)):
                image_b64 = _img_to_b64(blob, mime_fmt)
        else:
            # 檔案本身已超過剩餘預算，不解碼像素也不重新編碼；以編碼後大小提供預算，必然被拒絕並記錄被略過的大小。
            budget.try_add(_img_b64_size(size_bytes, mime_fmt))

    result: dict[str, Any] = {
        "type":       "image",
        "format":     fmt,
        "width":      width,
        "height":     height,
        "size_bytes": size_bytes,
    }
    if image_b64 is not None:
        result["image"] = image_b64
    result["images"] = []

    try:
        import pytesseract
        ocr_text = pytesseract.image_to_string(str(path), lang="chi_tra+chi_sim+eng").strip()
        if ocr_text:
            result["ocr_text"] = budget.take_text(ocr_text)
    except Exception:
        pass

    return result


def _parse_text(path: Path, budget: ResponseBudget) -> dict:
    size_bytes = path.stat().st_size
    max_text_mb = _max_text_mb()
    max_bytes  = int(max_text_mb * 1024 * 1024) if max_text_mb > 0 else 0
    budget_bytes = budget.remaining
    read_limit = min(max_bytes, budget_bytes) if max_bytes > 0 else budget_bytes

    with path.open("rb") as file:
        raw = file.read(read_limit)
    truncated = size_bytes > len(raw)

    detected = chardet.detect(raw)
    encoding = detected.get("encoding") or "utf-8"
    try:
        content = codecs.getincrementaldecoder(encoding)(errors="replace").decode(raw, final=not truncated)
    except Exception:
        content = raw.decode("utf-8", errors="replace")

    content = budget.take_text(content)
    if truncated and read_limit == budget_bytes:
        budget.mark_incomplete(original_bytes=size_bytes, original_bytes_exact=True)

    return {
        "type":       "text",
        "encoding":   encoding,
        "line_count": len(content.splitlines()),
        "size_bytes": size_bytes,
        "truncated":  truncated,
        "content":    content,
        "images":     [],
    }


@mcp.tool()
def get_document_root() -> dict:
    """回傳目前掛載根目錄資訊、支援格式，與路徑傳入方式提示。"""
    return {
        "document_root": str(MOUNT_ROOT),
        "host_mount_root": HOST_MOUNT_ROOT,
        "path_usage": {
            "json_encoding": "utf-8",
            "accepts": [
                "完整 Windows 路徑，例如 C:\\Workspace\\WSL\\Reader\\sample.txt",
                "完整 WSL 路徑，例如 /home/user/documents/sample.txt",
                "掛載根目錄下相對路徑，例如 sample.txt",
            ],
            "partial_path_strategy": "若只有部分檔名，先呼叫 resolve_document_path 取得候選完整路徑，再呼叫 read_document。",
            "requires_under_mount_root": True,
        },
        "supported_extensions": sorted(SUPPORTED_EXTENSIONS),
        "categories": {
            "documents": sorted(DOCUMENT_EXTS),
            "images":    sorted(IMAGE_EXTS),
            "text":      sorted(TEXT_EXTS),
        },
    }


@mcp.tool()
def resolve_document_path(path: str, limit: int = 10) -> dict:
    """解析指定路徑是否可讀；若檔案不存在，回傳同目錄下符合前綴的候選檔案。"""
    try:
        resolved = _resolve_path(path)
    except ValueError as exc:
        return {
            "input_path": path,
            "host_mount_root": HOST_MOUNT_ROOT,
            "exists": False,
            "error": str(exc),
            "candidates": [],
        }

    candidates = [] if resolved.exists() else _find_path_candidates(path, limit=limit)
    return {
        "input_path": path,
        "resolved_path": str(resolved),
        "host_mount_root": HOST_MOUNT_ROOT,
        "exists": resolved.exists(),
        "is_file": resolved.is_file() if resolved.exists() else False,
        "candidates": candidates,
    }


@mcp.tool()
def list_runtime_limits() -> dict:
    """回傳目前生效的文件讀取 runtime limit 設定。"""
    return {
        "max_images": _max_images(),
        "max_text_mb": _max_text_mb(),
        "document_list_default_limit": _list_default_limit(),
        "document_list_max_limit": _list_max_limit(),
        "document_list_default_depth": _list_default_depth(),
        "document_list_max_depth": _list_max_depth(),
        "document_search_default_limit": _search_default_limit(),
        "document_search_max_limit": _search_max_limit(),
        "document_search_default_scan_limit": _search_default_scan_limit(),
        "document_search_max_scan_limit": _search_max_scan_limit(),
        "document_xlsx_max_rows": _xlsx_default_max_rows(),
        "document_xlsx_max_rows_limit": _xlsx_max_rows_limit(),
        "document_xlsx_max_cells": _xlsx_default_max_cells(),
        "document_xlsx_max_cells_limit": _xlsx_max_cells_limit(),
        "max_response_bytes": _max_response_bytes(),
        "document_max_file_mb": _max_file_mb(),
    }


def config_status() -> dict:
    """回傳 Reader 實際載入的設定狀態，格式與 .NET 服務的 /config/status 相同。"""
    return {
        "service": "mcp-reader",
        "config_file": _WATCHER.status(),
        "connections": None,
        "ignored_settings": None,
        "connections_loaded_at": None,
        "limits": list_runtime_limits(),
        "settings": [
            {
                "name": setting.name,
                "description": setting.description,
                "unit": setting.unit,
                "default_value": setting.default_value,
                "min": setting.min_value,
                "max": _setting_max(setting),
                "max_from": setting.max_value if isinstance(setting.max_value, str) else None,
                "fallbacks": list(setting.fallbacks),
                "effective": _read_setting(setting.name),
            }
            for setting in READER_SETTINGS.values()
        ],
        "schema": None,
    }


@mcp.custom_route("/config/status", methods=["GET"])
async def _config_status_route(request):
    from starlette.responses import JSONResponse

    return JSONResponse(config_status())


@mcp.tool()
def list_documents(
    subpath: str = ".",
    recursive: bool = False,
    limit: int = 0,
    max_depth: int = -1,
) -> dict:
    """列出掛載根目錄下可讀取的檔案清單，預設不遞迴且有回傳上限。"""

    safe_limit = _clamp_int(limit, _list_default_limit(), 1, _list_max_limit())
    safe_depth = _clamp_int(max_depth, _list_default_depth(), 0, _list_max_depth())
    response = {
        "subpath": subpath,
        "host_mount_root": HOST_MOUNT_ROOT,
        "recursive": recursive,
        "max_depth": safe_depth if recursive else 0,
        "limit": safe_limit,
        "exists": False,
        "returned_count": 0,
        "depth_limited": False,
        "truncated": False,
        "files": [],
    }

    try:
        target = _resolve_path(subpath)
    except ValueError as exc:
        response["error"] = str(exc)
        return response

    if not target.exists():
        return response

    files, truncated, depth_limited = _list_supported_files(target, recursive, safe_depth, safe_limit)
    response["exists"] = True
    response["returned_count"] = len(files)
    response["depth_limited"] = depth_limited
    response["truncated"] = truncated or depth_limited
    response["files"] = files
    return response


@mcp.tool()
def read_document(
    path: str,
    include_images: bool = True,
    start_page: int = 1,
    end_page: int = 0,
    xlsx_max_rows: int = 0,
    xlsx_max_cells: int = 0,
) -> str:
    """讀取文件、圖片或文字檔，path 可傳完整 Windows/WSL 路徑或掛載根下相對路徑。
    start_page / end_page 僅適用於 PDF 和 PPTX（1-based，end_page=0 表示讀到末頁）。
    """

    try:
        file_path = _resolve_path(path)
    except ValueError as exc:
        return json.dumps({"error": str(exc)}, ensure_ascii=False)

    if not file_path.exists():
        return json.dumps({"error": f"File not found: {path}"}, ensure_ascii=False)

    ext = file_path.suffix.lower()
    if ext not in SUPPORTED_EXTENSIONS:
        return json.dumps({"error": f"Unsupported format: {ext}"}, ensure_ascii=False)

    try:
        if ext in DOCUMENT_EXTS or ext in IMAGE_EXTS:
            size_bytes = file_path.stat().st_size
            max_file_mb = _max_file_mb()
            max_file_bytes = int(max_file_mb * 1024 * 1024)
            if size_bytes > max_file_bytes:
                return json.dumps({
                    "error": f"File too large: {size_bytes / 1024 / 1024:.2f} MB exceeds DOCUMENT_MAX_FILE_MB={max_file_mb:g}",
                    "size_bytes": size_bytes,
                    "max_file_bytes": max_file_bytes,
                }, ensure_ascii=False)

        budget = ResponseBudget(_max_response_bytes())
        with _PARSE_LOCK:
            if ext == ".pdf":
                result = _parse_pdf(file_path, include_images, start_page, end_page, budget)
            elif ext == ".pptx":
                result = _parse_pptx(file_path, include_images, start_page, end_page, budget)
            elif ext == ".docx":
                result = _parse_docx(file_path, include_images, budget)
            elif ext == ".xlsx":
                safe_rows = _clamp_int(xlsx_max_rows, _xlsx_default_max_rows(), 1, _xlsx_max_rows_limit())
                safe_cells = _clamp_int(xlsx_max_cells, _xlsx_default_max_cells(), 1, _xlsx_max_cells_limit())
                result = _parse_xlsx(file_path, safe_rows, safe_cells, budget)
            elif ext in IMAGE_EXTS:
                result = _parse_image(file_path, budget)
            else:
                result = _parse_text(file_path, budget)

        result["path"]        = _relative_path_for(file_path)
        result["image_count"] = len(result.get("images", []))
        if budget.exhausted:
            result.update(budget.fields())
        return json.dumps(result, ensure_ascii=False)
    except Exception as exc:
        return json.dumps({"error": str(exc)}, ensure_ascii=False)


@mcp.tool()
def read_document_text_only(
    path: str,
    start_page: int = 1,
    end_page: int = 0,
) -> str:
    """只讀取文字（不擷取圖片，速度較快）。
    start_page / end_page 適用於 PDF 和 PPTX。
    """
    return read_document(path, include_images=False, start_page=start_page, end_page=end_page)


@mcp.tool()
def search_documents(
    keyword: str,
    subpath: str = ".",
    recursive: bool = True,
    limit: int = 0,
    max_files_scanned: int = 0,
    max_depth: int = -1,
    include_image_ocr: bool = False,
) -> dict:
    """在指定路徑下搜尋包含關鍵字的檔案，預設限制掃描數量且不做圖片 OCR。"""
    safe_limit = _clamp_int(limit, _search_default_limit(), 1, _search_max_limit())
    safe_scan_limit = _clamp_int(max_files_scanned, _search_default_scan_limit(), 1, _search_max_scan_limit())
    safe_depth = _clamp_int(max_depth, _list_default_depth(), 0, _list_max_depth())
    response = {
        "keyword": keyword,
        "subpath": subpath,
        "recursive": recursive,
        "max_depth": safe_depth if recursive else 0,
        "limit": safe_limit,
        "max_files_scanned": safe_scan_limit,
        "scanned_count": 0,
        "returned_count": 0,
        "truncated": False,
        "results": [],
    }

    if not keyword.strip():
        response["error"] = "Keyword is required."
        return response

    listing = list_documents(
        subpath=subpath,
        recursive=recursive,
        limit=safe_scan_limit,
        max_depth=safe_depth,
    )

    if "error" in listing:
        response["error"] = listing["error"]
        return response

    results = []
    for doc in listing["files"]:
        if doc.get("category") == "image" and not include_image_ocr:
            continue

        response["scanned_count"] += 1

        try:
            content = json.loads(read_document_text_only(doc["path"]))
        except Exception:
            continue

        if isinstance(content, dict) and content.get("error"):
            continue

        text_blob = json.dumps(content, ensure_ascii=False)
        if keyword.lower() in text_blob.lower():
            results.append({
                "path":     doc["path"],
                "type":     doc["type"],
                "category": doc.get("category", ""),
            })

            if len(results) >= safe_limit:
                response["truncated"] = True
                break

    response["returned_count"] = len(results)
    response["truncated"] = response["truncated"] or listing.get("truncated", False)
    response["results"] = results
    return response


MCP_TOKEN_VARIABLE = "MCPLAB_MCP_TOKEN"


def _expected_access_token() -> str:
    return (_SNAPSHOT.get_first([MCP_TOKEN_VARIABLE]) or "").strip()


def _is_authorized(authorization_header: str | None, expected: str) -> bool:
    scheme = "bearer "
    if not expected or not authorization_header or not authorization_header.lower().startswith(scheme):
        return False
    actual = authorization_header[len(scheme):].strip()
    return hmac.compare_digest(actual.encode("utf-8"), expected.encode("utf-8"))


class AccessTokenMiddleware:
    """當設定 MCPLAB_MCP_TOKEN 時，要求 /health 以外的 HTTP 請求帶上 Bearer token。"""

    def __init__(self, app):
        self.app = app

    async def __call__(self, scope, receive, send):
        if scope["type"] == "http" and not scope.get("path", "").startswith("/health"):
            expected = _expected_access_token()
            headers = {key.decode("latin-1").lower(): value.decode("latin-1") for key, value in scope.get("headers", [])}
            if expected and not _is_authorized(headers.get("authorization"), expected):
                body = json.dumps({
                    "error": "unauthorized",
                    "message": f"Missing or invalid bearer token. Set the Authorization header to match {MCP_TOKEN_VARIABLE}.",
                }).encode("utf-8")
                await send({
                    "type": "http.response.start",
                    "status": 401,
                    "headers": [
                        (b"content-type", b"application/json"),
                        (b"www-authenticate", b"Bearer"),
                    ],
                })
                await send({"type": "http.response.body", "body": body})
                return
        await self.app(scope, receive, send)


if __name__ == "__main__":
    import uvicorn

    _WATCHER.start()
    uvicorn.run(
        AccessTokenMiddleware(mcp.streamable_http_app()),
        host=mcp.settings.host,
        port=mcp.settings.port,
    )
