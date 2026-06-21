# 连接 daemon

## 获取 daemon 信息

向 `http(s)://<daemon 地址>:<端口>/info` 发起 **GET** 请求，即可获取 daemon 基本信息。

### 响应字段

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `name` | string | 程序名称，例如 `MCServerLauncher Future Daemon CSharp` |
| `version` | string | daemon 程序版本 |
| `api_version` | string | 当前 HTTP/WebSocket API 标识，现行为 `v1` |

### 响应示例

```json
{
  "name": "MCServerLauncher Future Daemon CSharp",
  "version": "0.1.0.1",
  "api_version": "v1"
}
```

## 根路径状态

向 `http(s)://<daemon 地址>:<端口>/` 发起 **GET** 请求会返回简单状态。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `message` | string | 程序名称 |
| `version` | string | daemon 程序版本 |
| `status` | string | 当前固定为 `ok` |
| `api_version` | string | 当前固定为 `v1` |

## 令牌

WebSocket 连接使用 `token` 查询参数鉴权。daemon 支持主令牌和基于主令牌签发的 JWT 子令牌。

### 主令牌

主令牌来自 daemon 配置。持有主令牌的调用方可以申请子令牌。

### 子令牌

向 `http(s)://<daemon 地址>:<端口>/subtoken` 发送 **POST** 表单请求可申请子令牌。

### 请求字段

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `token` | string | 主令牌 |
| `permissions` | string | 权限字符串。多个权限使用英文逗号分隔，例如 `mcsl.daemon.java_list,mcsl.daemon.file.download` |
| `expires` | int | 可选，过期秒数。无法解析时返回 HTTP 400。默认值为 `30` |

### 响应

成功时返回 `text/plain` JWT 字符串。

失败响应：

| HTTP 状态 | 原因 |
| --- | --- |
| `400` | `expires` 无法解析，或 `permissions` 格式无效 |
| `401` | 主令牌不匹配 |
| `500` | daemon 处理异常 |

## Postman 集合

daemon 提供 Postman Collection 作为机器可读的调试入口：

```uri
http(s)://<daemon 地址>:<端口>/postman_collection.json
```

集合中包含 HTTP 辅助接口和 WebSocket action 示例。当前不提供 `openapi.json`，action/event 协议以 WebSocket 为准。具体 action 参数和模型仍以 [](actions.md) 与 [](models.md) 为准。

## 建立 WebSocket 连接

使用下面的地址建立 action/event 长连接：

```uri
ws(s)://<daemon 地址>:<端口>/api/v1?token=<令牌>
```

连接建立后，client 发送 [](action.md) 请求。daemon 可在订阅后推送 [](event.md)。
