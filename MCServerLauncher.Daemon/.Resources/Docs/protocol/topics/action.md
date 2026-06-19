# Action

**Action** 是 client 主动发起的 JSON RPC 请求。daemon 收到请求后执行对应 handler，并返回同一个 `id` 的响应。

## 请求字段

| 字段 | 数据类型 | 说明 |
| --- | --- | --- |
| `action` | string | action 名称，使用 snake_case，例如 `get_system_info` |
| `params` | object \| null | action 参数。无参数时通常使用 `{}` |
| `id` | uuid string | 请求编号。响应中的 `id` 与请求一致 |

### 请求示例

```json
{
  "action": "ping",
  "params": {},
  "id": "11111111-1111-1111-1111-111111111111"
}
```

## 响应字段

| 字段 | 数据类型 | 说明 |
| --- | --- | --- |
| `status` | string | 请求状态，当前使用 `ok` 或 `error` |
| `retcode` | int | 返回码，见 [](action-errcode.md) |
| `data` | object \| null | action 结果数据。失败时通常为 `null` |
| `message` | string | 返回码消息，可能包含补充错误文本 |
| `id` | uuid string | 请求编号，与请求中的 `id` 一致 |

### 成功响应示例

```json
{
  "status": "ok",
  "retcode": 0,
  "data": {
    "time": 1717171717171
  },
  "message": "OK",
  "id": "11111111-1111-1111-1111-111111111111"
}
```

### 失败响应示例

```json
{
  "status": "error",
  "retcode": 10002,
  "data": null,
  "message": "Unknown Action",
  "id": "11111111-1111-1111-1111-111111111111"
}
```

## 命名规则

C# 中的 `ActionType` enum 通过 JSON snake_case converter 进入 wire format。

| C# 名称 | JSON 名称 |
| --- | --- |
| `GetSystemInfo` | `get_system_info` |
| `FileUploadRequest` | `file_upload_request` |
| `UpdateInstanceSettings` | `update_instance_settings` |

## 权限

每个 action handler 声明一个权限节点。权限值为 `*` 时表示当前 handler 接受任意已连接客户端。权限匹配规则见 [](permissions.md)。
