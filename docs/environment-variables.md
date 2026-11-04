# 環境變數

## 熱重載分類

| 變數或模式 | 熱重載 | 說明 |
| --- | --- | --- |
| `*_CONN_*` | runtime | 各 MCP 服務的連線設定。修改 `config/<服務>/service.env` 後，下一次呼叫 `list_connections()` 或實際工具時生效 |
| `QUERY_TIMEOUT`、`*_QUERY_TIMEOUT` | runtime | SQL Server 與 Oracle 查詢逾時秒數 |
| `MAX_ROWS`、`*_MAX_ROWS`、`ES_SEARCH_SIZE` | runtime | 查詢、搜尋或列舉的回傳量限制 |
| `DOCUMENT_MAX_IMAGES`、`DOCUMENT_MAX_TEXT_MB`、`DOCUMENT_LIST_*`、`DOCUMENT_SEARCH_*`、`DOCUMENT_XLSX_*` | runtime | Reader 文件讀取、列舉、搜尋與 XLSX 解析限制 |
| `DOCUMENT_HOST_MOUNT_ROOT`、`DOCUMENT_HOST_MOUNT_SOURCE` | build-time | Reader host 路徑根與 Docker volume 掛載來源；修改後需重建或重啟 Compose service |
| `MCPLAB_RUNTIME_UID`、`MCPLAB_RUNTIME_GID` | build-time | 容器非 root 執行身分；需能讀取 bind mount 的 `config/<服務>/service.env` |
| `MCPLAB_MCP_TOKEN` | runtime | MCP endpoint 的 Bearer token；留空則不檢查 |
| `*_MAX_CELL_CHARS` | runtime | SQL Server 與 Oracle 單一欄位回傳字元上限 |
| `*_HTTP_TIMEOUT`、`API_CONTRACT_*_BODY_BYTES` | runtime | HTTP 逾時與 API Contract 回應大小上限 |
| `*_TAG`、`MCP_*_PORT`、`MCP_BIND_ADDRESS`、`MCP_ADMIN_BIND_ADDRESS`、`MCPLAB_ADMIN_PASSWORD`、`COMPOSE_PROJECT_NAME`、`TZ` | build-time | 映像標籤、對外 port 與綁定位址、Compose project 與容器時區 |
| `DOCKER_GID`、`DOCKER_SOCKET_PATH` | build-time | Docker socket 掛載與 group 設定 |
| `MCP_SMOKE_*` | build-time | smoke test 腳本執行參數，不屬於服務 runtime 設定 |

runtime 變數放在 `config/<服務>/service.env`，由容器內服務監聽 `/app/config/service.env` 後套用，範本為 `config/services.env.example`。build-time 變數放在根目錄 `.env`，由 Docker Compose 或測試腳本在啟動時讀取，範本為 `.env.example`。

## 基底映像

| 變數 | 預設值 | 說明 |
| --- | --- | --- |
| `COMPOSE_PROJECT_NAME` | `mcp-lab` |  |
| `TZ` | `Asia/Taipei` |  |
| `PYTHON_TAG` | `3.12-slim` |  |
| `DOTNET_TAG` | `10.0` |  |

## MCP 對外埠

| 變數 | 預設值 | 說明 |
| --- | --- | --- |
| `MCP_API_CONTRACT_PORT` | `9851` |  |
| `MCP_SQL_SERVER_PORT` | `9800` |  |
| `MCP_ORACLE_PORT` | `9801` |  |
| `MCP_ELASTICSEARCH_PORT` | `9802` |  |
| `MCP_REDIS_PORT` | `9803` |  |
| `MCP_OIDC_PORT` | `9850` |  |
| `MCP_MOSQUITTO_PORT` | `9820` |  |
| `MCP_RABBITMQ_PORT` | `9821` |  |
| `MCP_MAILPIT_PORT` | `9830` |  |
| `MCP_READER_PORT` | `9840` |  |
| `MCP_DOCKER_PORT` | `9860` | 僅在 `--profile docker` 啟動 |
| `MCP_BIND_ADDRESS` | `127.0.0.1` | 所有 MCP 對外埠的綁定位址；改為 `0.0.0.0` 會對所有網路介面開放 |

## 設定管理介面

| 變數 | 預設值 | 說明 |
| --- | --- | --- |
| `MCP_ADMIN_PORT` | `9890` | 設定管理介面的對外埠 |
| `MCP_ADMIN_BIND_ADDRESS` | `127.0.0.1` | 設定管理介面的綁定位址，獨立於 `MCP_BIND_ADDRESS` |
| `MCPLAB_ADMIN_PASSWORD` | `` | 管理密碼，至少 8 個字元；未設定時無法登入。修改後需重新建立 `mcp-admin` 容器 |

## 存取控制

| 變數 | 預設值 | 說明 |
| --- | --- | --- |
| `MCPLAB_MCP_TOKEN` | `` | 設定後，`/health` 以外的請求必須帶 `Authorization: Bearer <token>` |

## Docker / Compose 服務

| 變數 | 預設值 | 說明 |
| --- | --- | --- |
| `DOCKER_GID` | `1001` | native Linux Docker socket 的 group id；Docker Desktop 另會使用 root group 存取 socket |
| `DOCKER_SOCKET_PATH` | `/var/run/docker.sock` | 容器內 Docker socket 路徑 |
| `MCPLAB_RUNTIME_UID` | `1000` | MCP 服務容器的非 root 使用者 UID，需能讀取掛載的 `service.env` |
| `MCPLAB_RUNTIME_GID` | `1000` | MCP 服務容器的非 root 使用者 GID，需能讀取掛載的 `service.env` |

## 資料服務工具限制

| 變數 | 預設值 | 說明 |
| --- | --- | --- |
| `QUERY_TIMEOUT` | `60` | SQL Server 與 Oracle 查詢逾時秒數後備值，允許 1 到 300 |
| `MSSQL_QUERY_TIMEOUT` | `` | SQL Server 查詢逾時秒數，未設定時使用 `QUERY_TIMEOUT` |
| `ORACLE_QUERY_TIMEOUT` | `` | Oracle 查詢逾時秒數，未設定時使用 `QUERY_TIMEOUT` |
| `MAX_ROWS` | `` | 舊版相容用的回傳筆數後備值，建議改用服務專屬變數 |
| `MSSQL_MAX_ROWS` | `500` | SQL Server 查詢最大回傳列數，允許 1 到 5000 |
| `ORACLE_MAX_ROWS` | `500` | Oracle 查詢最大回傳列數，允許 1 到 5000 |
| `MSSQL_MAX_CELL_CHARS` | `4000` | SQL Server 單一欄位回傳字元上限，允許 1 到 100000 |
| `ORACLE_MAX_CELL_CHARS` | `4000` | Oracle 單一欄位回傳字元上限，允許 1 到 100000 |
| `ES_SEARCH_SIZE` | `10` | Elasticsearch 搜尋預設回傳筆數，允許 1 到 1000 |

## HTTP 逾時與回應上限

| 變數 | 預設值 | 說明 |
| --- | --- | --- |
| `ES_HTTP_TIMEOUT` | `30` | Elasticsearch HTTP 逾時秒數，允許 1 到 300 |
| `API_CONTRACT_HTTP_TIMEOUT` | `15` | API Contract HTTP 逾時秒數，允許 1 到 300 |
| `OIDC_HTTP_TIMEOUT` | `10` | OIDC discovery 與 JWKS 的 HTTP 逾時秒數，允許 1 到 300 |
| `RABBITMQ_HTTP_TIMEOUT` | `10` | RabbitMQ Management API 的 HTTP 逾時秒數，允許 1 到 300 |
| `MAILPIT_HTTP_TIMEOUT` | `10` | Mailpit API 的 HTTP 逾時秒數，允許 1 到 300 |
| `API_CONTRACT_DEFAULT_BODY_BYTES` | `50000` | `invoke_endpoint` 預設回傳的 body 位元組數，不超過 `API_CONTRACT_MAX_BODY_BYTES` |
| `API_CONTRACT_MAX_BODY_BYTES` | `200000` | `invoke_endpoint` 可要求的 body 位元組上限，允許 1 到 10000000 |

## 本機整合測試

| 變數 | 預設值 | 說明 |
| --- | --- | --- |
| `MCP_SMOKE_INCLUDE_ORACLE` | `false` | 設為 `true` 時，`scripts/smoke-test.py` 會執行 Oracle 唯讀查詢檢查 |
| `MCP_SMOKE_TIMEOUT_SECONDS` | `15` | MCP smoke test 每次 HTTP 請求的逾時秒數 |
| `MCP_SMOKE_MAILPIT_SMTP_HOST` | `127.0.0.1` | Mailpit smoke test 寄送測試信件使用的 SMTP host |
| `MCP_SMOKE_MAILPIT_SMTP_PORT` | `1025` | Mailpit smoke test 寄送測試信件使用的 SMTP port |

## SQL Server 連線

提醒：`ALIAS` 為大寫英數，建議用英文代號，例如 `LOCAL`、`PROD`。

| 變數 | 預設值 | 說明 |
| --- | --- | --- |
| `MSSQL_CONN_PROD_NAME` | `` | 連線名稱 |
| `MSSQL_CONN_PROD_HOST` | `` | 主機位址 |
| `MSSQL_CONN_PROD_PORT` | `1433` | 連線埠 |
| `MSSQL_CONN_PROD_USER` | `` | 使用者帳號 |
| `MSSQL_CONN_PROD_PASSWORD` | `` | 使用者密碼 |
| `MSSQL_CONN_PROD_DATABASE` | `master` | 預設資料庫 |
| `MSSQL_CONN_PROD_TRUST_SERVER_CERTIFICATE` | `true` | 信任伺服器憑證而不驗證；連線到已設定正式憑證的伺服器時建議設為 `false` |
| `MSSQL_CONN_PROD_CONNECT_TIMEOUT` | `30` | 建立連線的逾時秒數 |

## API Contract 連線

提醒：`ALIAS` 為大寫英數，建議用英文代號，例如 `LOCAL`、`PROD`。

| 變數 | 預設值 | 說明 |
| --- | --- | --- |
| `API_CONTRACT_CONN_PROD_NAME` | `` | 連線名稱 |
| `API_CONTRACT_CONN_PROD_SPEC_URL` | `` | OpenAPI JSON/YAML URL |
| `API_CONTRACT_CONN_PROD_SPEC_PATH` | `` | 容器內可讀取的 OpenAPI JSON/YAML 路徑 |
| `API_CONTRACT_CONN_PROD_BASE_URL` | `` | API base URL，未設定時使用 spec 的 `servers[0].url` |
| `API_CONTRACT_CONN_PROD_INVOKE_ENABLED` | `false` | 是否啟用 `invoke_endpoint` |
| `API_CONTRACT_CONN_PROD_ALLOWED_METHODS` | `GET,HEAD,OPTIONS` | `invoke_endpoint` 允許的 HTTP methods |
| `API_CONTRACT_CONN_PROD_SSL_SKIP_VERIFY` | `false` | 是否略過 HTTPS 憑證驗證 |
| `API_CONTRACT_CONN_PROD_AUTH_HEADER_NAME` | `` | 固定 auth header 名稱 |
| `API_CONTRACT_CONN_PROD_AUTH_HEADER_VALUE` | `` | 固定 auth header 值 |

## Oracle 連線

提醒：`ALIAS` 為大寫英數，建議用英文代號，例如 `LOCAL`、`PROD`。

| 變數 | 預設值 | 說明 |
| --- | --- | --- |
| `ORACLE_CONN_PROD_NAME` | `` | 連線名稱 |
| `ORACLE_CONN_PROD_HOST` | `` | 主機位址 |
| `ORACLE_CONN_PROD_PORT` | `1521` | 連線埠 |
| `ORACLE_CONN_PROD_SERVICE` | `` | Service 名稱 |
| `ORACLE_CONN_PROD_USER` | `` | 使用者帳號 |
| `ORACLE_CONN_PROD_PASSWORD` | `` | 使用者密碼 |

## Elasticsearch 連線

提醒：`ALIAS` 為大寫英數，建議用英文代號，例如 `LOCAL`、`PROD`。

| 變數 | 預設值 | 說明 |
| --- | --- | --- |
| `ES_CONN_PROD_NAME` | `` | 連線名稱 |
| `ES_CONN_PROD_URL` | `` | 連線 URL |
| `ES_CONN_PROD_USER` | `` | 使用者帳號 |
| `ES_CONN_PROD_PASSWORD` | `` | 使用者密碼 |
| `ES_CONN_PROD_SSL_SKIP_VERIFY` | `` | 略過 SSL 憑證驗證 |

## Redis 連線

提醒：`ALIAS` 為大寫英數，建議用英文代號，例如 `LOCAL`、`PROD`。

| 變數 | 預設值 | 說明 |
| --- | --- | --- |
| `REDIS_CONN_PROD_NAME` | `` | 連線名稱 |
| `REDIS_CONN_PROD_HOST` | `` | 主機位址 |
| `REDIS_CONN_PROD_PORT` | `6379` | 連線埠 |
| `REDIS_CONN_PROD_DATABASE` | `0` | 預設資料庫編號 |
| `REDIS_CONN_PROD_PASSWORD` | `` | 使用者密碼 |
| `REDIS_CONN_PROD_CONNECT_TIMEOUT` | `5` | 建立連線的逾時秒數 |
| `REDIS_CONN_PROD_OPERATION_TIMEOUT` | `10` | 單一指令的逾時秒數 |

## OIDC 連線

提醒：`ALIAS` 為大寫英數，建議用英文代號，例如 `LOCAL`、`PROD`。

| 變數 | 預設值 | 說明 |
| --- | --- | --- |
| `OIDC_CONN_PROD_NAME` | `` | 連線名稱 |
| `OIDC_CONN_PROD_ISSUER` | `` | OIDC issuer URL |
| `OIDC_CONN_PROD_DISCOVERY_URL` | `` | discovery document URL，未設定時使用 `{ISSUER}/.well-known/openid-configuration` |
| `OIDC_CONN_PROD_AUDIENCE` | `` | 預設 JWT audience，未設定時 `validate_jwt` 不驗證 audience |
| `OIDC_CONN_PROD_REQUIRE_HTTPS_METADATA` | `true` | 是否要求 discovery 與 JWKS 使用 HTTPS |

## MQTT 連線

提醒：`ALIAS` 為大寫英數，建議用英文代號，例如 `LOCAL`、`PROD`。

| 變數 | 預設值 | 說明 |
| --- | --- | --- |
| `MQTT_CONN_PROD_NAME` | `` | 連線名稱 |
| `MQTT_CONN_PROD_HOST` | `` | 主機位址 |
| `MQTT_CONN_PROD_PORT` | `1883` | 連線埠 |
| `MQTT_CONN_PROD_USER` | `` | 使用者帳號 |
| `MQTT_CONN_PROD_PASSWORD` | `` | 使用者密碼 |

## RabbitMQ 連線

提醒：`ALIAS` 為大寫英數，建議用英文代號，例如 `LOCAL`、`PROD`。

| 變數 | 預設值 | 說明 |
| --- | --- | --- |
| `RABBITMQ_CONN_PROD_NAME` | `` | 連線名稱 |
| `RABBITMQ_CONN_PROD_HOST` | `` | 主機位址 |
| `RABBITMQ_CONN_PROD_MGMTPORT` | `15672` | 管理介面埠 |
| `RABBITMQ_CONN_PROD_USER` | `` | 使用者帳號 |
| `RABBITMQ_CONN_PROD_PASSWORD` | `` | 使用者密碼 |

## Mailpit 連線

提醒：`ALIAS` 為大寫英數，建議用英文代號，例如 `LOCAL`、`PROD`。

| 變數 | 預設值 | 說明 |
| --- | --- | --- |
| `MAILPIT_CONN_PROD_NAME` | `` | 連線名稱 |
| `MAILPIT_CONN_PROD_URL` | `` | Mailpit Web API URL |

## 文件服務

| 變數 | 預設值 | 說明 |
| --- | --- | --- |
| `DOCUMENT_HOST_MOUNT_ROOT` | `` | Reader 對外接受與回傳的單一 host 掛載根目錄 |
| `DOCUMENT_HOST_MOUNT_SOURCE` | `DOCUMENT_HOST_MOUNT_ROOT` | Docker Compose 實際掛載到 `/host_root` 的來源路徑；Docker Desktop 從 Windows PowerShell 啟動時可設為 `C:/Workspace` |
| `DOCUMENT_MAX_IMAGES` | `20` | 圖片最大回傳張數 |
| `DOCUMENT_MAX_TEXT_MB` | `10` | 文字內容最大回傳大小（MB），設為 `0` 表示不限制 |
| `DOCUMENT_LIST_DEFAULT_LIMIT` | `100` | `list_documents` 預設最大回傳檔案數 |
| `DOCUMENT_LIST_MAX_LIMIT` | `1000` | `list_documents` 允許的最大回傳檔案數 |
| `DOCUMENT_LIST_DEFAULT_DEPTH` | `2` | `list_documents` 遞迴列舉時的預設深度 |
| `DOCUMENT_LIST_MAX_DEPTH` | `10` | `list_documents` 遞迴列舉時允許的最大深度 |
| `DOCUMENT_SEARCH_DEFAULT_LIMIT` | `20` | `search_documents` 預設最大回傳結果數 |
| `DOCUMENT_SEARCH_MAX_LIMIT` | `200` | `search_documents` 允許的最大回傳結果數 |
| `DOCUMENT_SEARCH_DEFAULT_SCAN_LIMIT` | `200` | `search_documents` 預設最大掃描檔案數 |
| `DOCUMENT_SEARCH_MAX_SCAN_LIMIT` | `2000` | `search_documents` 允許的最大掃描檔案數 |
| `DOCUMENT_XLSX_MAX_ROWS` | `1000` | `read_document` 讀取 XLSX 時的預設最大列數 |
| `DOCUMENT_XLSX_MAX_ROWS_LIMIT` | `10000` | `read_document` 讀取 XLSX 時允許的最大列數 |
| `DOCUMENT_XLSX_MAX_CELLS` | `20000` | `read_document` 讀取 XLSX 時的預設最大儲存格數 |
| `DOCUMENT_XLSX_MAX_CELLS_LIMIT` | `200000` | `read_document` 讀取 XLSX 時允許的最大儲存格數 |
