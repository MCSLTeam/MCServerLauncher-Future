# Typed events

通过 `mcsl.event.subscribe` 和 `mcsl.event.unsubscribe` 管理当前连接的 typed subscription。订阅参数引用 catalog 中的 event 名称和可选 typed meta。

内置事件为：

| Event | Meta |
| --- | --- |
| `mcsl.event.instance.catalog.changed` | omitted |
| `mcsl.event.daemon.report` | omitted |
| `mcsl.event.instance.log` | required `instance_id` |
| `mcsl.event.notification` | required source instance/rule identity |

每个事件以其 event 名称作为 JSON-RPC notification method。`params` 中携带 typed `data`、按 catalog 规定的 missing/null/object meta，以及 timestamp。事件按连接顺序投递；慢消费者超过 bounded queue 容量时会被断开，不能依赖静默丢包。
