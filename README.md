# MCP Lab

AI Agent 在沒有專屬工具的情況下操作資料庫時，通常會透過「寫程式 → 執行 → 觀察輸出 → 調整」的迂迴流程來完成任務。這個專案的目的是提供直接的資料庫操作工具，讓 Agent 能夠跳過這個過程。

內容包含許多個人化的設計決策，不建議直接採用，請優先選擇社群或官方維護的成熟專案。

## 服務

| 類別 | 服務 | Port | Runtime | 功能 |
| --- | --- | --- | --- | --- |
| datastores | mcp-sql-server | 9800 | .NET 10 | SQL Server 查詢、schema 瀏覽、寫入防護 |
| datastores | mcp-oracle | 9801 | .NET 10 | Oracle 查詢、schema 瀏覽、寫入防護 |
| datastores | mcp-elasticsearch | 9802 | .NET 10 | Elasticsearch 索引、搜尋、文件操作 |
| datastores | mcp-redis | 9803 | .NET 10 | Redis key 讀寫、TTL 管理、伺服器資訊 |
| messaging | mcp-mosquitto | 9820 | .NET 10 | MQTT 發佈、訂閱與 broker 狀態 |
| messaging | mcp-rabbitmq | 9821 | .NET 10 | RabbitMQ queue、exchange 與發佈 |
| documents | mcp-reader | 9840 | Python 3.12 | 文件、圖片、文字檔讀取與搜尋 |

## 多連線支援

每個服務支援同時連線多個實例，以 `{TYPE}_CONN_{ALIAS}_*` 格式設定（`ALIAS` 為大寫英數，如 `PROD`、`STAGE`）：

```env
MSSQL_CONN_PROD_NAME=production
MSSQL_CONN_PROD_HOST=prod-server
MSSQL_CONN_PROD_PORT=1433
MSSQL_CONN_PROD_USER=sa
MSSQL_CONN_PROD_PASSWORD=pw1

MSSQL_CONN_STAGE_NAME=staging
MSSQL_CONN_STAGE_HOST=stage-server
MSSQL_CONN_STAGE_PORT=1433
MSSQL_CONN_STAGE_USER=sa
MSSQL_CONN_STAGE_PASSWORD=pw2
```

Agent 可呼叫 `list_connections()` 查詢可用連線，再以 `connection="staging"` 指定目標。未指定時自動使用第一個已設定的連線。

| 服務 | 環境變數前綴 | 必填欄位 |
| --- | --- | --- |
| mcp-sql-server | `MSSQL_CONN` | HOST, PORT |
| mcp-oracle | `ORACLE_CONN` | HOST, PORT, SERVICE |
| mcp-elasticsearch | `ES_CONN` | URL |
| mcp-redis | `REDIS_CONN` | HOST, PORT |
| mcp-mosquitto | `MQTT_CONN` | HOST, PORT |
| mcp-rabbitmq | `RABBITMQ_CONN` | HOST, MGMTPORT |

## 快速開始

```bash
cp .env.example .env
# 填入至少一個連線設定
docker compose up -d --build
docker compose ps
```

## 環境變數一覽

完整清單請見 [docs/environment-variables.md](./docs/environment-variables.md)。

## 設定原則

`.env.example` 不預設任何主機位址，請依你的部署位置填寫。

常見情境：

- 目標服務在同一台 Docker 主機上：使用 `host.docker.internal`
- 目標服務為遠端機器：填 DNS 名稱或 IP
- Elasticsearch 使用 HTTPS 自簽憑證：設定 `ES_CONN_{ALIAS}_SSL_SKIP_VERIFY=true`

## SQL / Oracle 寫入防護

`execute` 工具內建 SqlGuard，防止危險寫入操作：

| 規則 | 說明 |
| --- | --- |
| 禁止 DROP | SQL Server：拒絕 `DROP TABLE/DATABASE/SCHEMA`；Oracle：拒絕 `DROP TABLE/USER/TABLESPACE` |
| 禁止 TRUNCATE | 拒絕清空資料表 |
| DELETE/UPDATE 需 WHERE | 無條件刪除或更新一律拒絕 |
| 拒絕無效 WHERE | `WHERE 1=1`、`WHERE TRUE` 等繞過條件一律拒絕 |

`query` 工具僅允許唯讀 `SELECT / WITH` 查詢。

## Base64 工具（避免字元清洗）

部分 MCP client 會清洗單引號等字元，造成 SQL/JSON 內容失真。當你發現字串常值或 JSON 內容被清洗（例如單引號消失）時，可改用 Base64 版本工具傳輸，同時支援 Base64URL（`-`、`_`）與省略 padding（`=`）：

- SQL Server：`query_base64`、`execute_base64`
- Oracle：`query_base64`、`execute_base64`
- Elasticsearch：`search_base64`、`index_document_base64`、`delete_by_query_base64`

範例（SQL Server `query_base64`）：

```text
SQL: SELECT 'alpha' AS name
Base64: U0VMRUNUICdhbHBoYScgQVMgbmFtZQ==
```

## 文件服務

`mcp-reader` 採單一 `DOCUMENT_HOST_MOUNT_ROOT` 模型：先把一個共同根目錄掛進容器，再直接傳完整 Windows/WSL 路徑給 MCP，不需要先同步，也不一定要先列檔。

### 基本設定

```env
DOCUMENT_HOST_MOUNT_ROOT=/mnt/c/Users/<你的帳號>/Documents
DOCUMENT_MAX_TEXT_MB=10
```

Windows 路徑與 WSL 路徑均可直接傳入：

```text
read_document(path="C:\Users\<你的帳號>\Documents\mcp-reader\sample.txt")
read_document(path="/home/<你的帳號>/documents/mcp-reader/sample.txt")
```

### 路徑規則

- 傳入路徑必須落在 `DOCUMENT_HOST_MOUNT_ROOT` 底下。
- Windows via WSL 時，不要把根目錄設成 `/mnt`；請直接掛 `/mnt/c`、`/mnt/d` 或更窄的共同父層。
- MCP JSON 應以 UTF-8 傳送，reader 會直接處理中文路徑。
- 如果 Agent 只有部分檔名或不確定完整路徑，先呼叫 `resolve_document_path`。例如傳入 `C:\Workspace\Reader\ECK`，reader 會回傳候選完整路徑，再把結果交給 `read_document`。

### 大檔說明

- PDF / PPTX / DOCX / XLSX 沒有額外內容大小上限；真正常見的限制是 MCP client 或終端顯示。
- 純文字檔才受 `DOCUMENT_MAX_TEXT_MB` 控制；設為 `0` 表示不限制。
- 若回應太大，優先用 `start_page` / `end_page` 分段讀 PDF、PPTX，而不是把文件能力限制掉。

### 啟動注意事項

- 若 `DOCUMENT_HOST_MOUNT_ROOT` 是 WSL 路徑（如 `/mnt/c/...`、`/home/...`），請從 WSL shell 執行 `docker compose up`。
- 直接從 Windows PowerShell 啟動時，容器在某些環境下可能只看到空目錄。

### 支援格式

| 類型 | 副檔名 |
| --- | --- |
| 辦公室文件 | `.pdf`, `.docx`, `.pptx`, `.xlsx` |
| 圖片 | `.jpg`, `.jpeg`, `.png`, `.gif`, `.bmp`, `.webp`, `.svg` |
| 文字 | `.txt`, `.md`, `.log`, `.csv`, `.json`, `.yaml`, `.toml`, `.ini`, `.xml`, `.html` |

圖片讀取使用 Pillow 萃取 EXIF 與尺寸資訊；若系統安裝 Tesseract（容器已內建），另可提取 OCR 文字（支援繁中、簡中、英文）。

### 可用工具

| 工具 | 說明 |
| --- | --- |
| `get_document_root` | 查詢目前掛載根目錄與支援格式清單 |
| `resolve_document_path` | 驗證指定路徑是否可讀，或回傳同目錄下符合前綴的候選完整路徑 |
| `list_documents` | 以完整 Windows/WSL 路徑或相對路徑列出可讀取檔案 |
| `read_document` | 以完整 Windows/WSL 路徑或相對路徑讀取指定檔案（含圖片） |
| `read_document_text_only` | 僅讀取文字內容 |
| `search_documents` | 在指定 Windows/WSL 路徑下依關鍵字全文搜尋 |

## AI 工具設定

各 AI 工具的 MCP 設定路徑與格式略有差異，請對照下方各節填入。

### Gemini CLI / Codex CLI

設定檔格式：`mcpServers` + `url`

- Gemini CLI：`%USERPROFILE%\.gemini\settings.json`
- Codex CLI：`%USERPROFILE%\.codex\mcp.json`

```json
{
  "mcpServers": {
    "mcp-sql-server":    { "url": "http://127.0.0.1:9800/mcp" },
    "mcp-oracle":        { "url": "http://127.0.0.1:9801/mcp" },
    "mcp-elasticsearch": { "url": "http://127.0.0.1:9802/mcp" },
    "mcp-redis":         { "url": "http://127.0.0.1:9803/mcp" },
    "mcp-mosquitto":     { "url": "http://127.0.0.1:9820/mcp" },
    "mcp-rabbitmq":      { "url": "http://127.0.0.1:9821/mcp" },
    "mcp-reader":        { "url": "http://127.0.0.1:9840/mcp" }
  }
}
```

### GitHub Copilot（VS Code）

設定檔格式：`servers` + `type`

- `%AppData%\Code\User\mcp.json`

```json
{
  "servers": {
    "mcp-sql-server":    { "type": "http", "url": "http://127.0.0.1:9800/mcp" },
    "mcp-oracle":        { "type": "http", "url": "http://127.0.0.1:9801/mcp" },
    "mcp-elasticsearch": { "type": "http", "url": "http://127.0.0.1:9802/mcp" },
    "mcp-redis":         { "type": "http", "url": "http://127.0.0.1:9803/mcp" },
    "mcp-mosquitto":     { "type": "http", "url": "http://127.0.0.1:9820/mcp" },
    "mcp-rabbitmq":      { "type": "http", "url": "http://127.0.0.1:9821/mcp" },
    "mcp-reader":        { "type": "http", "url": "http://127.0.0.1:9840/mcp" }
  }
}
```

### GitHub Copilot CLI

設定檔格式：`mcpServers` + `type`

- `%USERPROFILE%\.copilot\mcp-config.json`

```json
{
  "mcpServers": {
    "mcp-sql-server":    { "type": "http", "url": "http://localhost:9800/mcp" },
    "mcp-oracle":        { "type": "http", "url": "http://localhost:9801/mcp" },
    "mcp-elasticsearch": { "type": "http", "url": "http://localhost:9802/mcp" },
    "mcp-redis":         { "type": "http", "url": "http://localhost:9803/mcp" },
    "mcp-mosquitto":     { "type": "http", "url": "http://localhost:9820/mcp" },
    "mcp-rabbitmq":      { "type": "http", "url": "http://localhost:9821/mcp" },
    "mcp-reader":        { "type": "http", "url": "http://localhost:9840/mcp" }
  }
}
```

### Claude Code

設定檔格式：`mcpServers` + `type`

- `%USERPROFILE%\.claude.json`

```json
{
  "mcpServers": {
    "mcp-sql-server":    { "type": "http", "url": "http://localhost:9800/mcp" },
    "mcp-oracle":        { "type": "http", "url": "http://localhost:9801/mcp" },
    "mcp-elasticsearch": { "type": "http", "url": "http://localhost:9802/mcp" },
    "mcp-redis":         { "type": "http", "url": "http://localhost:9803/mcp" },
    "mcp-mosquitto":     { "type": "http", "url": "http://localhost:9820/mcp" },
    "mcp-rabbitmq":      { "type": "http", "url": "http://localhost:9821/mcp" },
    "mcp-reader":        { "type": "http", "url": "http://localhost:9840/mcp" }
  }
}
```

### Antigravity

設定檔格式：`mcpServers` + `serverUrl`

- `%USERPROFILE%\.gemini\antigravity\mcp_config.json`

```json
{
  "mcpServers": {
    "mcp-sql-server":    { "serverUrl": "http://localhost:9800/mcp" },
    "mcp-oracle":        { "serverUrl": "http://localhost:9801/mcp" },
    "mcp-elasticsearch": { "serverUrl": "http://localhost:9802/mcp" },
    "mcp-redis":         { "serverUrl": "http://localhost:9803/mcp" },
    "mcp-mosquitto":     { "serverUrl": "http://localhost:9820/mcp" },
    "mcp-rabbitmq":      { "serverUrl": "http://localhost:9821/mcp" },
    "mcp-reader":        { "serverUrl": "http://localhost:9840/mcp" }
  }
}
```

## 常用指令

```bash
docker compose up -d --build              # 啟動（含重新建置）
docker compose down -v                    # 停止並清除資料
docker compose ps                         # 查看狀態
docker compose logs -f mcp-sql-server     # 查看指定服務 log
docker compose restart mcp-reader         # 重啟指定服務
```

## 授權

This project is MIT licensed. See [LICENSE.md](LICENSE.md).
