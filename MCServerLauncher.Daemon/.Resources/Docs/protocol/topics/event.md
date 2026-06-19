# Event

**Event** 是 daemon 主动推送给已订阅 client 的 JSON 消息。client 需要先调用 `subscribe_event`。

## 订阅事件

订阅通过 [](actions.md#subscribe_event) action 完成。当前实现不返回 listener id。取消订阅时再次提供相同的 `type` 和 `meta`。

某些事件需要 `meta` 作为过滤条件。例如 `instance_log` 需要指定 `instance_id`。

## 事件字段

| 字段 | 数据类型 | 说明 |
| --- | --- | --- |
| `event` | string | event 名称，使用 snake_case，例如 `instance_log` |
| `meta` | object \| null | 事件元数据或订阅过滤元数据 |
| `data` | object \| null | 事件数据 |
| `time` | int64 | Unix milliseconds 时间戳 |

### 事件示例

```json
{
  "event": "instance_log",
  "meta": {
    "instance_id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
  },
  "data": {
    "log": "[Server thread/INFO]: Done"
  },
  "time": 1717171717000
}
```

## 批量事件

daemon 可能把同一 WebSocket frame 中的多个事件序列化为 `EventPacket[]`。client 应同时接受单个事件对象和事件数组。
