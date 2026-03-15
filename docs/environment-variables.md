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
| `MCP_SQL_SERVER_PORT` | `9800` |  |
| `MCP_ORACLE_PORT` | `9801` |  |
| `MCP_ELASTICSEARCH_PORT` | `9802` |  |
| `MCP_REDIS_PORT` | `9803` |  |
| `MCP_MOSQUITTO_PORT` | `9820` |  |
| `MCP_RABBITMQ_PORT` | `9821` |  |
| `MCP_READER_PORT` | `9840` |  |

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

## 文件服務

| 變數 | 預設值 | 說明 |
| --- | --- | --- |
| `DOCUMENT_HOST_MOUNT_ROOT` | `` | 容器可讀取的單一 host 掛載根目錄 |
| `DOCUMENT_MAX_IMAGES` | `20` | 圖片最大回傳張數 |
| `DOCUMENT_MAX_TEXT_MB` | `10` | 文字內容最大回傳大小（MB），設為 `0` 表示不限制 |


