# 環境變數


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
| `MCP_DOCKER_PORT` | `9860` |  |

## Docker / Compose 服務

| 變數 | 預設值 | 說明 |
| --- | --- | --- |
| `DOCKER_GID` | `1001` | native Linux Docker socket 的 group id；Docker Desktop 另會使用 root group 存取 socket |
| `DOCKER_SOCKET_PATH` | `/var/run/docker.sock` | 容器內 Docker socket 路徑 |

## 資料服務工具限制

| 變數 | 預設值 | 說明 |
| --- | --- | --- |
| `QUERY_TIMEOUT` | `60` | SQL Server 與 Oracle 查詢逾時秒數後備值，允許 1 到 300 |
| `MSSQL_QUERY_TIMEOUT` | `` | SQL Server 查詢逾時秒數，未設定時使用 `QUERY_TIMEOUT` |
| `ORACLE_QUERY_TIMEOUT` | `` | Oracle 查詢逾時秒數，未設定時使用 `QUERY_TIMEOUT` |
| `MAX_ROWS` | `` | 舊版相容用的回傳筆數後備值，建議改用服務專屬變數 |
| `MSSQL_MAX_ROWS` | `500` | SQL Server 查詢最大回傳列數，允許 1 到 5000 |
| `ORACLE_MAX_ROWS` | `500` | Oracle 查詢最大回傳列數，允許 1 到 5000 |
| `ES_SEARCH_SIZE` | `10` | Elasticsearch 搜尋預設回傳筆數，允許 1 到 1000 |

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
| `MSSQL_CONN_PROD_DATABASE` | `` | 預設資料庫 |

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
| `REDIS_CONN_PROD_DATABASE` | `` | 預設資料庫 |
| `REDIS_CONN_PROD_PASSWORD` | `` | 使用者密碼 |

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
| `DOCUMENT_HOST_MOUNT_ROOT` | `` | 容器可讀取的單一 host 掛載根目錄 |
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
