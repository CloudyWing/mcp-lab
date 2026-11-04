# MCP Lab

AI Agent 在沒有專屬工具的情況下操作資料庫時，通常會透過「寫程式 → 執行 → 觀察輸出 → 調整」的迂迴流程來完成任務。這個專案的目的是提供直接的資料庫操作工具，讓 Agent 能夠跳過這個過程。

內容包含許多個人化的設計決策，不建議直接採用，請優先選擇社群或官方維護的成熟專案。

## 服務

| 類別 | 服務 | Port | Runtime | 功能 |
| --- | --- | --- | --- | --- |
| api | mcp-api-contract | 9851 | .NET 10 | OpenAPI spec 檢視、drift 比對與有限 response 驗證 |
| datastores | mcp-sql-server | 9800 | .NET 10 | SQL Server 查詢、schema 瀏覽、寫入防護 |
| datastores | mcp-oracle | 9801 | .NET 10 | Oracle 查詢、schema 瀏覽、寫入防護 |
| datastores | mcp-elasticsearch | 9802 | .NET 10 | Elasticsearch 索引、搜尋、文件操作 |
| datastores | mcp-redis | 9803 | .NET 10 | Redis key 讀寫、TTL 管理、伺服器資訊 |
| identity | mcp-oidc | 9850 | .NET 10 | OIDC discovery、JWKS 與 JWT 檢查 |
| messaging | mcp-mosquitto | 9820 | .NET 10 | MQTT 發佈、訂閱與 broker 狀態 |
| messaging | mcp-rabbitmq | 9821 | .NET 10 | RabbitMQ queue、exchange 與發佈 |
| tools | mcp-mailpit | 9830 | .NET 10 | Mailpit 測試信箱唯讀檢查 |
| documents | mcp-reader | 9840 | Python 3.12 | 文件、圖片、文字檔讀取與搜尋 |
| system | mcp-docker | 9860 | .NET 10 | Docker / Compose read-only 狀態檢查 |
| admin | mcp-admin | 9890 | .NET 10 Blazor | 設定管理介面（非 MCP 服務） |

## 多連線支援

每個服務支援同時連線多個實例，在該服務的 `config/<服務>/service.env` 以 `{TYPE}_CONN_{ALIAS}_*` 格式設定（`ALIAS` 為大寫英數，如 `PROD`、`STAGE`）。以下為 `config/mcp-sql-server/service.env` 範例：

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

Agent 可呼叫 `list_connections()` 查詢可用連線，再以 `connection="staging"` 指定目標。讀取類工具未指定時使用第一個已設定的連線；寫入類工具（如 `execute`、`set_key`、`delete_by_query`、`publish`）必須明確指定 `connection`，否則回傳 `blocked`。

| 服務 | 環境變數前綴 | 必填欄位 | 選填欄位（預設值） |
| --- | --- | --- | --- |
| mcp-api-contract | `API_CONTRACT_CONN` | SPEC_URL 或 SPEC_PATH | BASE_URL、INVOKE_ENABLED（false）、ALLOWED_METHODS（GET,HEAD,OPTIONS）、SSL_SKIP_VERIFY（false）、AUTH_HEADER_NAME、AUTH_HEADER_VALUE |
| mcp-sql-server | `MSSQL_CONN` | HOST、USER、PASSWORD | PORT（1433）、DATABASE（master）、TRUST_SERVER_CERTIFICATE（true）、CONNECT_TIMEOUT（30 秒） |
| mcp-oracle | `ORACLE_CONN` | HOST、SERVICE、USER、PASSWORD | PORT（1521） |
| mcp-elasticsearch | `ES_CONN` | URL | USER、PASSWORD、SSL_SKIP_VERIFY（false） |
| mcp-redis | `REDIS_CONN` | HOST | PORT（6379）、PASSWORD、DATABASE（0）、CONNECT_TIMEOUT（5 秒）、OPERATION_TIMEOUT（10 秒） |
| mcp-oidc | `OIDC_CONN` | ISSUER | DISCOVERY_URL、AUDIENCE、REQUIRE_HTTPS_METADATA（true） |
| mcp-mosquitto | `MQTT_CONN` | HOST | PORT（1883）、USER、PASSWORD |
| mcp-rabbitmq | `RABBITMQ_CONN` | HOST、USER、PASSWORD | MGMTPORT（15672） |
| mcp-mailpit | `MAILPIT_CONN` | URL | — |

每個連線都可設定 `NAME`，未填時使用 Alias 的小寫（例如 `PROD` → `prod`）。也可設定 `ENVIRONMENT`（`local`、`dev`、`test`、`staging`、`prod`），只用來顯示，`list_connections()` 會回傳此標籤，方便 Agent 辨認正式環境。欄位定義集中在 `shared/McpLab.Shared/Configuration/ConnectionSchemas.cs`，解析時會檢查：

- 必填欄位、Port 範圍、`true`/`false`、URL 格式。
- 打錯的欄位名稱（例如 `HOTS`）與含底線或小寫的 Alias，這類設定會被忽略並記錄原因。
- 重複的 `NAME`：後出現的連線會被拒絕，不會覆蓋前一個。

連線設定有誤時，若該連線先前載入成功，會繼續使用前一版設定。

## 快速開始

```bash
cp .env.example .env
# 在 .env 設定 MCPLAB_ADMIN_PASSWORD（至少 8 個字元）
python3 scripts/init-config.py
docker compose up -d --build
docker compose ps
```

啟動後開啟 http://127.0.0.1:9890 ，以 `MCPLAB_ADMIN_PASSWORD` 登入設定管理介面，新增連線與調整設定。也可以直接編輯 `config/<服務>/service.env`。

設定分成兩層：

- 根目錄 `.env`：Compose 層設定，例如對外埠、映像標籤、UID/GID、Reader 掛載路徑。修改後需重新建立容器。
- `config/<服務>/service.env`：各服務的連線與查詢限制。每個容器只掛載自己的 `config/<服務>` 目錄，不會讀到其他服務的帳密，修改後不需重啟容器。

`scripts/init-config.py` 會為每個服務建立 `service.env`，已存在的檔案不會覆寫（`--force` 可強制覆寫）。內容來源：

- 根目錄 `.env` 已有該服務的設定時，從 `.env` 搬移，適用於從舊版升級。
- 否則從 `config/services.env.example` 範本建立。

從舊版升級時，可加上 `--strip-env`，把已搬移的設定從 `.env` 移除，原檔備份為 `.env.bak`：

```bash
python3 scripts/init-config.py --strip-env
docker compose up -d --force-recreate
```

`mcp-docker` 需要掛載 Docker socket，預設不啟動。需要時以 profile 啟動：

```bash
docker compose --profile docker up -d --build
```

## 設定管理介面

`mcp-admin` 是以 Blazor 與 MudBlazor 製作的設定管理介面，取代手動編輯 `service.env`：

| 頁面 | 功能 |
| --- | --- |
| 總覽 | 各服務是否連得到、連線載入數量，以及設定錯誤與被忽略的變數 |
| 各服務頁 | 新增、編輯、複製、刪除連線；以該服務的 ping 工具測試連線；編輯查詢限制與逾時 |
| 存取權杖 | 一次替所有服務產生或停用 `MCPLAB_MCP_TOKEN` |
| 從 .env 匯入 | 貼上舊版 `.env`，預覽每個變數會分到哪個服務，並先列出打錯的欄位與不合法的 Alias |
| 異動紀錄 | 透過介面做的每次變更與結果，記錄在 `config/.admin/audit.jsonl`，不含設定值 |

行為說明：

- 表單依各服務的連線 Schema 產生，存檔前會檢查必填欄位、Port、URL、Alias 格式與名稱重複，有誤時無法儲存。
- 密碼類欄位只寫不讀：畫面只顯示「已設定」，留空表示保持不變。
- 存檔後會等服務熱重載，再以 `/config/status` 確認結果；若新設定讓原本可用的連線失效，會自動還原上一版。每次寫入前的內容會備份在 `config/.admin/history`，各服務保留最近 20 版；服務頁的「版本紀錄」可比對備份與目前版本的差異（密碼類變數只顯示有變更），並還原任一版本。
- 只修改受影響的變數，檔案中的註解與順序會保留。

存取限制：

- 預設只綁定 `127.0.0.1:9890`，可用 `MCP_ADMIN_BIND_ADDRESS`、`MCP_ADMIN_PORT` 調整，與 MCP 服務的 `MCP_BIND_ADDRESS` 分開設定。
- 需以 `MCPLAB_ADMIN_PASSWORD` 登入；未設定或少於 8 個字元時無法登入。修改密碼後需執行 `docker compose up -d mcp-admin` 重新建立容器。
- `mcp-admin` 會掛載整個 `config/` 目錄（可寫入）以管理各服務設定，各 MCP 服務仍只掛載自己的目錄。

## 存取控制

所有 MCP 服務的對外埠預設只綁定 `127.0.0.1`，同一區網的其他主機無法連線。確實需要對外開放時，可設定 `MCP_BIND_ADDRESS`（例如 `0.0.0.0`），修改後需重新建立容器。

若要要求 MCP client 帶 token，設定 `MCPLAB_MCP_TOKEN`。設定後除了 `/health` 以外的請求都必須帶 `Authorization: Bearer <token>`，否則回傳 HTTP 401。此設定位於各服務的 `service.env`，會熱重載，修改後不需重啟容器；留空則不檢查。`scripts/init-config.py` 會把同一個值複製到每個服務。

```env
MCPLAB_MCP_TOKEN=<隨機產生的長字串>
```

MCP client 設定範例（HTTP transport）：

```json
{
  "type": "http",
  "url": "http://127.0.0.1:9800/mcp",
  "headers": {
    "Authorization": "Bearer <MCPLAB_MCP_TOKEN>"
  }
}
```

## 設定熱重載

各 MCP 服務會把 `config/<服務>` 目錄以唯讀方式掛載到容器內 `/app/config`，並由服務進程監聽 `/app/config/service.env`。修改後不需要重啟容器；下一次呼叫 `list_connections()`、`list_runtime_limits()` 或實際查詢工具時會讀取新的 runtime 設定。新增、修改與刪除連線都會生效。

熱重載涵蓋連線設定與查詢限制，例如 `MSSQL_CONN_*_HOST`、`ORACLE_QUERY_TIMEOUT`、`MSSQL_MAX_ROWS`、`ES_SEARCH_SIZE`、`DOCUMENT_LIST_DEFAULT_LIMIT`。Compose 與建置階段設定位於根目錄 `.env`，不熱重載，例如 `*_TAG`、`MCP_*_PORT`、`COMPOSE_PROJECT_NAME`、`DOCKER_SOCKET_PATH`、`TZ`、`MCP_SMOKE_*`。

`service.env` 變更後通常會在 1 秒內生效。若語法錯誤，服務會保留前一份可用設定並輸出 warning log；修正檔案後 watcher 會再次載入。既有請求與 driver connection pool 不會被主動中斷。

## 設定狀態

每個 MCP 服務提供 `GET /config/status`，回報服務實際載入的設定，不含任何設定值（密碼、token 只會以變數名稱出現）：

| 欄位 | 說明 |
| --- | --- |
| `config_file` | 設定檔路徑、是否存在、最後載入時間與語法錯誤；有錯誤時服務繼續使用前一份設定 |
| `connections` | 每個連線的 Alias、名稱與狀態：`loaded`、`rejected`、`rejected_using_previous`（設定有誤，沿用前一版），以及原因 |
| `ignored_settings` | 被忽略的變數與原因，例如打錯的欄位名稱或不合法的 Alias |
| `limits` | 目前生效的查詢限制，與 `list_runtime_limits()` 相同 |
| `schema` | 該服務可用的連線欄位、型別、必填與預設值 |

設定 `MCPLAB_MCP_TOKEN` 後，此端點同樣需要 Bearer token：

```bash
curl -H "Authorization: Bearer <token>" http://127.0.0.1:9800/config/status
```

## 測試

單元測試不依賴外部服務，可直接執行：

```bash
dotnet test
```

本機整合 smoke test 需要 Docker Compose 堆疊已啟動，並且 `config/<服務>/service.env` 已指向可連線的測試環境：

```bash
python3 scripts/smoke-test.py
python3 scripts/smoke-test.py --check-hot-reload
```

此腳本會檢查 Compose 容器健康狀態、`read_only`、`cap_drop: [ALL]`、`no-new-privileges`、`/tmp`
tmpfs、非 root 使用者，並呼叫各 MCP 服務的 ping 或唯讀查詢工具。SQL Server 與 Oracle 會驗證
`query` 工具拒絕同一段輸入中的寫入語句。加入 `--check-hot-reload` 時，腳本會暫時修改 `config/mcp-sql-server/service.env` 中第一個 `MSSQL_CONN_*_HOST`，並新增後移除一組連線，確認 `list_connections()` 不需重啟就反映修改、新增與刪除，最後還原原檔。

Oracle 查詢預設略過，因為專案未內建 Oracle 資料庫。若 `config/mcp-oracle/service.env` 已設定可查詢的 Oracle 連線，可明確啟用：

```bash
python3 scripts/smoke-test.py --include-oracle
```

可使用分層檢查腳本串接單元測試、Docker build 與 smoke test：

```bash
python3 scripts/check-all.py
python3 scripts/check-all.py --build-services --smoke
python3 scripts/check-all.py --build-services --smoke --include-oracle
```

## 環境變數一覽

完整清單請見 [docs/environment-variables.md](./docs/environment-variables.md)。

## 設定原則

`config/services.env.example` 不預設任何主機位址，請依你的部署位置填寫。

常見情境：

- 目標服務在同一台 Docker 主機上：使用 `host.docker.internal`
- 目標服務為遠端機器：填 DNS 名稱或 IP
- Elasticsearch 使用 HTTPS 自簽憑證：設定 `ES_CONN_{ALIAS}_SSL_SKIP_VERIFY=true`

## 資料服務工具限制

SQL Server、Oracle 與 Elasticsearch 的查詢回傳量與逾時有上限，避免單次 MCP 呼叫回傳過大內容或長時間卡住。

| 服務 | 變數 | 預設值 | 上限 |
| --- | --- | --- | --- |
| SQL Server | `MSSQL_QUERY_TIMEOUT` / `QUERY_TIMEOUT` | 60 秒 | 300 秒 |
| SQL Server | `MSSQL_MAX_ROWS` / `MAX_ROWS` | 500 | 5000 |
| Oracle | `ORACLE_QUERY_TIMEOUT` / `QUERY_TIMEOUT` | 60 秒 | 300 秒 |
| Oracle | `ORACLE_MAX_ROWS` / `MAX_ROWS` | 500 | 5000 |
| SQL Server | `MSSQL_MAX_CELL_CHARS` | 4000 | 100000 |
| Oracle | `ORACLE_MAX_CELL_CHARS` | 4000 | 100000 |
| Elasticsearch | `ES_SEARCH_SIZE` / `MAX_ROWS` | 10 | 1000 |
| Redis | `REDIS_MAX_ROWS` / `MAX_ROWS` | 100 | 1000 |

HTTP 逾時與 API Contract 回應上限：

| 服務 | 變數 | 預設值 | 上限 |
| --- | --- | --- | --- |
| Elasticsearch | `ES_HTTP_TIMEOUT` | 30 秒 | 300 秒 |
| API Contract | `API_CONTRACT_HTTP_TIMEOUT` | 15 秒 | 300 秒 |
| OIDC | `OIDC_HTTP_TIMEOUT` | 10 秒 | 300 秒 |
| RabbitMQ | `RABBITMQ_HTTP_TIMEOUT` | 10 秒 | 300 秒 |
| Mailpit | `MAILPIT_HTTP_TIMEOUT` | 10 秒 | 300 秒 |
| API Contract | `API_CONTRACT_DEFAULT_BODY_BYTES` | 50000 | 10000000 |
| API Contract | `API_CONTRACT_MAX_BODY_BYTES` | 200000 | 10000000 |

以上設定與連線的逾時欄位都會熱重載，修改後不需重啟容器。服務層級設定集中定義在 `shared/McpLab.Shared/Configuration/ServiceSettings.cs`，`/config/status` 的 `settings` 會列出每個設定的預設值、範圍與目前生效值。

`*_MAX_CELL_CHARS` 限制單一欄位回傳的字元數，超過的部分會截斷並標示原始長度。Redis 的 `get_key` 讀取 list、set、sorted set、hash 時，最多回傳 `REDIS_MAX_ROWS` 筆，並回傳 `total_count` 與 `truncated`。

可用 `list_runtime_limits()` 查詢 SQL Server、Oracle、Elasticsearch、Redis 與 Reader 目前生效的限制值。

## MCP Tool 回應格式

.NET MCP 服務的 tool 回應使用固定 JSON envelope：

```json
{
  "ok": true,
  "kind": "ok",
  "message": "OK",
  "data": {}
}
```

`kind` 的常見值：

| kind | 說明 |
| --- | --- |
| `ok` | 工具執行成功且有資料或操作結果 |
| `empty` | 工具執行成功，但沒有符合條件的資料 |
| `blocked` | 輸入被安全規則阻擋 |
| `error` | 執行失敗或輸入格式錯誤 |

Tool 回應會遮罩常見機密片段，例如 password、token、secret、API key 與 URL userinfo。

## Healthcheck

.NET MCP 服務提供 `/health` endpoint，Docker Compose healthcheck 會檢查 HTTP 200，而不是只檢查 TCP port 是否開啟。`mcp-reader` 使用 Python FastMCP，仍維持 TCP healthcheck。

## API Contract / Drift 服務

`mcp-api-contract` 是可選的 OpenAPI 輔助 MCP，用於讀取 OpenAPI JSON/YAML spec、列出 endpoint、查詢 operation
schema、比較兩份 spec 的 endpoint/status drift，並以有限 JSON schema 檢查 response。

API 呼叫功能預設關閉。只有在 profile 明確設定 `INVOKE_ENABLED=true` 時，`invoke_endpoint` 才會依 method
allow-list 呼叫 API；預設 allow-list 為 `GET`、`HEAD`、`OPTIONS`。

範例連線：

```env
API_CONTRACT_CONN_STAGE_NAME=stage
API_CONTRACT_CONN_STAGE_SPEC_URL=https://api-stage.example.test/openapi.json
API_CONTRACT_CONN_STAGE_BASE_URL=https://api-stage.example.test
API_CONTRACT_CONN_STAGE_INVOKE_ENABLED=false
API_CONTRACT_CONN_STAGE_ALLOWED_METHODS=GET,HEAD,OPTIONS
```

若 API 需要固定 header，可透過環境變數注入，工具回應不會回傳 header value：

```env
API_CONTRACT_CONN_STAGE_AUTH_HEADER_NAME=Authorization
API_CONTRACT_CONN_STAGE_AUTH_HEADER_VALUE=Bearer <token>
```

### 可用工具

| 工具 | 說明 |
| --- | --- |
| `list_connections` | 列出 API contract profiles，不回傳 auth header value |
| `get_spec_summary` | 讀取 OpenAPI spec 摘要 |
| `list_endpoints` | 依 path prefix、method 或 tag 列出 endpoints |
| `get_endpoint_schema` | 取得指定 operation JSON |
| `compare_specs` | 比較 baseline 與 candidate 的 endpoint/status drift |
| `invoke_endpoint` | profile 啟用後依 allow-list 呼叫 API endpoint |
| `validate_response` | 以有限 OpenAPI response schema 檢查 status code 與 JSON body |

## OIDC / JWT 服務

`mcp-oidc` 用於檢查 OpenID Connect provider 與 JWT token。工具只讀取 discovery document、JWKS 公鑰與本機 JWT
內容，不執行登入、不呼叫 token endpoint，也不修改 identity provider 設定。

範例連線：

```env
OIDC_CONN_LOCAL_NAME=local
OIDC_CONN_LOCAL_ISSUER=http://host.docker.internal:8180/realms/demo
OIDC_CONN_LOCAL_REQUIRE_HTTPS_METADATA=false
OIDC_CONN_LOCAL_AUDIENCE=account
```

正式或測試環境建議維持 `OIDC_CONN_<ALIAS>_REQUIRE_HTTPS_METADATA=true`。

### 可用工具

| 工具 | 說明 |
| --- | --- |
| `list_connections` | 列出 OIDC provider profiles，不回傳 token |
| `get_discovery_document` | 讀取 discovery document 摘要 |
| `get_jwks` | 讀取 JWKS 公鑰摘要 |
| `inspect_jwt` | 解析 JWT header 與 claims；不驗證簽章 |
| `validate_jwt` | 使用 provider JWKS 驗證 JWT 簽章、issuer、audience 與 lifetime |

## Mailpit 服務

`mcp-mailpit` 是可選測試輔助 MCP，僅用於讀取 Mailpit 測試信箱，協助測試流程確認預期信件是否出現。
寄信、信件刪除、已讀狀態變更與 Mailpit 管理功能不在此服務範圍；驗證流程與判斷規則由測試腳本或 Skill 定義。
工具只包裝不會改變信件狀態的 API；Mailpit 的 `GET /api/v1/message/{ID}` 會標記已讀，因此不提供該工具。

本機範例連線：

```env
MAILPIT_CONN_DEFAULT_NAME=default
MAILPIT_CONN_DEFAULT_URL=http://host.docker.internal:8025
```

### 可用工具

| 工具 | 說明 |
| --- | --- |
| `list_connections` | 列出 Mailpit 連線 |
| `ping_connection` | 取得 Mailpit 版本與信件計數 |
| `list_messages` | 列出最新信件摘要 |
| `search_messages` | 使用 Mailpit 搜尋語法查詢信件摘要 |
| `wait_for_message` | 輪詢等待符合條件的信件出現 |
| `get_message_headers` | 取得信件 headers，不標記已讀 |
| `get_message_source` | 取得信件 raw source，不標記已讀 |

## Docker / Compose 服務

`mcp-docker` 透過 Docker Engine API 讀取本機 Docker 狀態，工具只呼叫 read-only API，不提供 restart、exec、
remove、prune 等會改變狀態的操作。此服務仍套用 `read_only`、`cap_drop: [ALL]`、`no-new-privileges` 與非 root
使用者。

`mcp-docker` 不直接掛載 Docker socket，而是透過同一個 profile 中的 `mcp-docker-proxy`
（[docker-socket-proxy](https://github.com/Tecnativa/docker-socket-proxy)）存取。Proxy 只開放 ping、version 與 containers
的 GET 請求，拒絕所有會改變狀態的請求，並且只放在 internal network，其他服務連不到。即使 `mcp-docker` 被入侵，
也無法透過 Docker API 建立或執行容器。

### 可用工具

| 工具 | 說明 |
| --- | --- |
| `ping_docker` | 檢查 Docker Engine 是否可連線 |
| `get_docker_version` | 取得 Docker Engine 版本資訊 |
| `list_compose_projects` | 依容器 label 彙整 Compose project |
| `list_containers` | 列出容器，可依 Compose project 或 service 篩選 |
| `inspect_container` | 檢視容器狀態、ports、networks、mounts，不回傳環境變數 |
| `get_container_logs` | 讀取容器最近 stdout/stderr logs，限制 tail 並套用回應遮罩 |
| `get_container_stats` | 取得單一容器 CPU、記憶體、網路與 block I/O 快照 |

## SQL / Oracle 寫入防護

`execute` 工具內建 SqlGuard，防止危險寫入操作：

| 規則 | 說明 |
| --- | --- |
| 禁止 DROP | SQL Server：拒絕 `DROP TABLE/DATABASE/SCHEMA`；Oracle：拒絕 `DROP TABLE/USER/TABLESPACE` |
| 禁止 TRUNCATE | 拒絕清空資料表 |
| DELETE/UPDATE 需 WHERE | 無條件刪除或更新一律拒絕 |
| 拒絕無效 WHERE | `WHERE 1=1`、`WHERE TRUE` 等繞過條件一律拒絕 |

`query` 工具僅允許唯讀 `SELECT / WITH` 查詢，並拒絕 `SELECT ... INTO`、`OPENROWSET`、`OPENQUERY`、`OPENDATASOURCE`（SQL Server）與 `FOR UPDATE`（Oracle）等會產生副作用的語法。

## Elasticsearch 刪除防護

`delete_by_query` 必須提供非空的 `query` 物件，且查詢中任何位置都不能出現 `match_all`。目標索引必須是單一索引，不接受 `*`、逗號分隔清單、`_all` 或 `-` 開頭的名稱。

## API Contract 呼叫限制

`invoke_endpoint` 與 spec 下載不會自動跟隨 HTTP 重導向，避免 auth header 被送到其他位置；遇到 3xx 時會回傳 status code 與 `redirect_location`。回應以串流方式讀取，超過 `maxBodyBytes` 的部分不會讀入記憶體；spec 上限為 10 MB。

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
DOCUMENT_HOST_MOUNT_ROOT=C:/Users/<你的帳號>/Documents
DOCUMENT_MAX_TEXT_MB=10
```

`DOCUMENT_HOST_MOUNT_ROOT` 是 Reader 對外接受與回傳的 host 路徑根。若 Docker Compose 實際需要使用不同格式掛載同一個目錄，可另外設定 `DOCUMENT_HOST_MOUNT_SOURCE`。例如從 Windows PowerShell 使用 Docker Desktop 啟動時，`DOCUMENT_HOST_MOUNT_SOURCE=C:/Workspace` 會比 `/mnt/c/Workspace` 更穩定。

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

- PDF / PPTX / DOCX / XLSX 的常見限制是 MCP client 或終端顯示。
- 純文字檔才受 `DOCUMENT_MAX_TEXT_MB` 控制；設為 `0` 表示不限制。
- 若回應太大，優先用 `start_page` / `end_page` 分段讀 PDF、PPTX。
- XLSX 讀取預設限制列數與儲存格數，避免一次回傳整本大型工作簿。

### 列舉與搜尋限制

`list_documents` 預設不遞迴列舉，並限制最大回傳筆數；回應中會包含 `returned_count`、`depth_limited` 與 `truncated`，用來判斷是否仍有未回傳的候選檔案。需要掃描子目錄時，明確傳入 `recursive=true`、`max_depth` 與 `limit`。

`search_documents` 會限制掃描檔案數與回傳結果數，預設不對圖片執行 OCR 搜尋。若搜尋結果回傳 `truncated=true`，請縮小 `subpath`、提高限制值，或改用更精確的檔名前綴搭配 `resolve_document_path`。

### 啟動注意事項

- 從 Windows PowerShell 使用 Docker Desktop 啟動時，建議把 Windows 目錄寫成 `C:/Workspace`。
- 若 `DOCUMENT_HOST_MOUNT_ROOT` 是 WSL 路徑（如 `/mnt/c/...`、`/home/...`），請從 WSL shell 執行 `docker compose up`。
- 直接從 Windows PowerShell 啟動時，容器在某些環境下可能只看到空目錄。

### 支援格式

| 類型 | 副檔名 |
| --- | --- |
| 辦公室文件 | `.pdf`, `.docx`, `.pptx`, `.xlsx` |
| 圖片 | `.jpg`, `.jpeg`, `.png`, `.gif`, `.bmp`, `.webp`, `.tiff`, `.tif`, `.svg` |
| 文字 | `.txt`, `.md`, `.log`, `.csv`, `.json`, `.yaml`, `.yml`, `.toml`, `.ini`, `.xml`, `.html` |

圖片讀取使用 Pillow 萃取 EXIF 與尺寸資訊；若系統安裝 Tesseract（容器已內建），另可提取 OCR 文字（支援繁中、簡中、英文）。

### 可用工具

| 工具 | 說明 |
| --- | --- |
| `get_document_root` | 查詢目前掛載根目錄與支援格式清單 |
| `resolve_document_path` | 驗證指定路徑是否可讀，或回傳同目錄下符合前綴的候選完整路徑 |
| `list_runtime_limits` | 查詢目前生效的文件讀取與搜尋限制 |
| `list_documents` | 以完整 Windows/WSL 路徑或相對路徑列出可讀取檔案，預設不遞迴且有回傳上限 |
| `read_document` | 以完整 Windows/WSL 路徑或相對路徑讀取指定檔案（含圖片） |
| `read_document_text_only` | 僅讀取文字內容 |
| `search_documents` | 在指定 Windows/WSL 路徑下依關鍵字搜尋，限制掃描數與回傳結果數 |

## AI 工具設定

各 AI 工具的 MCP 設定路徑與格式略有差異，請對照下方各節填入。

### Gemini CLI / Codex CLI

設定檔格式：`mcpServers` + `url`

- Gemini CLI：`%USERPROFILE%\.gemini\settings.json`
- Codex CLI：`%USERPROFILE%\.codex\mcp.json`

```json
{
  "mcpServers": {
    "mcp-api-contract": { "url": "http://127.0.0.1:9851/mcp" },
    "mcp-sql-server":    { "url": "http://127.0.0.1:9800/mcp" },
    "mcp-oracle":        { "url": "http://127.0.0.1:9801/mcp" },
    "mcp-elasticsearch": { "url": "http://127.0.0.1:9802/mcp" },
    "mcp-redis":         { "url": "http://127.0.0.1:9803/mcp" },
    "mcp-oidc":          { "url": "http://127.0.0.1:9850/mcp" },
    "mcp-mosquitto":     { "url": "http://127.0.0.1:9820/mcp" },
    "mcp-rabbitmq":      { "url": "http://127.0.0.1:9821/mcp" },
    "mcp-mailpit":       { "url": "http://127.0.0.1:9830/mcp" },
    "mcp-reader":        { "url": "http://127.0.0.1:9840/mcp" },
    "mcp-docker":        { "url": "http://127.0.0.1:9860/mcp" }
  }
}
```

### Codex Desktop

設定檔格式：`mcp_servers` TOML table + `url`

- `%USERPROFILE%\.codex\config.toml`

```toml
[mcp_servers.mcp-api-contract]
url = "http://127.0.0.1:9851/mcp"

[mcp_servers.mcp-sql-server]
url = "http://127.0.0.1:9800/mcp"

[mcp_servers.mcp-oracle]
url = "http://127.0.0.1:9801/mcp"

[mcp_servers.mcp-elasticsearch]
url = "http://127.0.0.1:9802/mcp"

[mcp_servers.mcp-redis]
url = "http://127.0.0.1:9803/mcp"

[mcp_servers.mcp-oidc]
url = "http://127.0.0.1:9850/mcp"

[mcp_servers.mcp-mosquitto]
url = "http://127.0.0.1:9820/mcp"

[mcp_servers.mcp-rabbitmq]
url = "http://127.0.0.1:9821/mcp"

[mcp_servers.mcp-mailpit]
url = "http://127.0.0.1:9830/mcp"

[mcp_servers.mcp-reader]
url = "http://127.0.0.1:9840/mcp"

[mcp_servers.mcp-docker]
url = "http://127.0.0.1:9860/mcp"
```

### GitHub Copilot（VS Code）

設定檔格式：`servers` + `type`

- `%AppData%\Code\User\mcp.json`

```json
{
  "servers": {
    "mcp-api-contract": { "type": "http", "url": "http://127.0.0.1:9851/mcp" },
    "mcp-sql-server":    { "type": "http", "url": "http://127.0.0.1:9800/mcp" },
    "mcp-oracle":        { "type": "http", "url": "http://127.0.0.1:9801/mcp" },
    "mcp-elasticsearch": { "type": "http", "url": "http://127.0.0.1:9802/mcp" },
    "mcp-redis":         { "type": "http", "url": "http://127.0.0.1:9803/mcp" },
    "mcp-oidc":          { "type": "http", "url": "http://127.0.0.1:9850/mcp" },
    "mcp-mosquitto":     { "type": "http", "url": "http://127.0.0.1:9820/mcp" },
    "mcp-rabbitmq":      { "type": "http", "url": "http://127.0.0.1:9821/mcp" },
    "mcp-mailpit":       { "type": "http", "url": "http://127.0.0.1:9830/mcp" },
    "mcp-reader":        { "type": "http", "url": "http://127.0.0.1:9840/mcp" },
    "mcp-docker":        { "type": "http", "url": "http://127.0.0.1:9860/mcp" }
  }
}
```

### GitHub Copilot CLI

設定檔格式：`mcpServers` + `type`

- `%USERPROFILE%\.copilot\mcp-config.json`

```json
{
  "mcpServers": {
    "mcp-api-contract": { "type": "http", "url": "http://localhost:9851/mcp" },
    "mcp-sql-server":    { "type": "http", "url": "http://localhost:9800/mcp" },
    "mcp-oracle":        { "type": "http", "url": "http://localhost:9801/mcp" },
    "mcp-elasticsearch": { "type": "http", "url": "http://localhost:9802/mcp" },
    "mcp-redis":         { "type": "http", "url": "http://localhost:9803/mcp" },
    "mcp-oidc":          { "type": "http", "url": "http://localhost:9850/mcp" },
    "mcp-mosquitto":     { "type": "http", "url": "http://localhost:9820/mcp" },
    "mcp-rabbitmq":      { "type": "http", "url": "http://localhost:9821/mcp" },
    "mcp-mailpit":       { "type": "http", "url": "http://localhost:9830/mcp" },
    "mcp-reader":        { "type": "http", "url": "http://localhost:9840/mcp" },
    "mcp-docker":        { "type": "http", "url": "http://localhost:9860/mcp" }
  }
}
```

### Claude Code

設定檔格式：`mcpServers` + `type`

- `%USERPROFILE%\.claude.json`

```json
{
  "mcpServers": {
    "mcp-api-contract": { "type": "http", "url": "http://localhost:9851/mcp" },
    "mcp-sql-server":    { "type": "http", "url": "http://localhost:9800/mcp" },
    "mcp-oracle":        { "type": "http", "url": "http://localhost:9801/mcp" },
    "mcp-elasticsearch": { "type": "http", "url": "http://localhost:9802/mcp" },
    "mcp-redis":         { "type": "http", "url": "http://localhost:9803/mcp" },
    "mcp-oidc":          { "type": "http", "url": "http://localhost:9850/mcp" },
    "mcp-mosquitto":     { "type": "http", "url": "http://localhost:9820/mcp" },
    "mcp-rabbitmq":      { "type": "http", "url": "http://localhost:9821/mcp" },
    "mcp-mailpit":       { "type": "http", "url": "http://localhost:9830/mcp" },
    "mcp-reader":        { "type": "http", "url": "http://localhost:9840/mcp" },
    "mcp-docker":        { "type": "http", "url": "http://localhost:9860/mcp" }
  }
}
```

### Antigravity

設定檔格式：`mcpServers` + `serverUrl`

- `%USERPROFILE%\.gemini\antigravity\mcp_config.json`

```json
{
  "mcpServers": {
    "mcp-api-contract": { "serverUrl": "http://localhost:9851/mcp" },
    "mcp-sql-server":    { "serverUrl": "http://localhost:9800/mcp" },
    "mcp-oracle":        { "serverUrl": "http://localhost:9801/mcp" },
    "mcp-elasticsearch": { "serverUrl": "http://localhost:9802/mcp" },
    "mcp-redis":         { "serverUrl": "http://localhost:9803/mcp" },
    "mcp-oidc":          { "serverUrl": "http://localhost:9850/mcp" },
    "mcp-mosquitto":     { "serverUrl": "http://localhost:9820/mcp" },
    "mcp-rabbitmq":      { "serverUrl": "http://localhost:9821/mcp" },
    "mcp-mailpit":       { "serverUrl": "http://localhost:9830/mcp" },
    "mcp-reader":        { "serverUrl": "http://localhost:9840/mcp" },
    "mcp-docker":        { "serverUrl": "http://localhost:9860/mcp" }
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
python3 scripts/smoke-test.py             # 執行本機整合 smoke test
python3 scripts/check-all.py --smoke      # 執行單元測試與 smoke test
```

## 授權

This project is MIT licensed. See [LICENSE.md](LICENSE.md).
