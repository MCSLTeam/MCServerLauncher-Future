# MCSL Future 通信协议

**MCSL Future 通信协议（MCSL Future Protocol，简称 MFP）** 描述 WPF client、daemon client 与 daemon 之间的现行通信格式。

本文档以 `MCServerLauncher-Future` C# 实现为准。当前实现使用 TouchSocket WebSocket 承载 action/event 协议，消息正文使用 `System.Text.Json` 编码，字段命名使用 `snake_case`。

## 架构

MCSL Future 分为两类主要进程：

- **daemon**：运行在目标主机上，管理 instance、文件、Java 扫描、事件广播和 WebSocket/HTTP 入口。
- **client**：通过 daemon client 连接 daemon。WPF client 是当前仓库内的桌面客户端。

一个 client 可以连接多个 daemon。每个 daemon 暴露一个 WebSocket RPC 入口和少量 HTTP 辅助接口。

## 通信流程

1. client 通过 HTTP 读取 daemon 信息，或使用已知地址直接连接。
2. client 使用 token 建立 WebSocket 连接：`ws(s)://<host>:<port>/api/v1?token=<token>`。
3. client 发送 [](action.md) 请求，daemon 返回同 `id` 的响应。
4. client 订阅 [](event.md) 后，daemon 主动推送事件包。
5. 文件传输使用 action 和 WebSocket frame，详见 [](file-transfer.md)。

## 编码约定

- action 和 event 消息使用 JSON，不使用 MessagePack。
- enum 值按 `JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)` 序列化。例如 `GetSystemInfo` 在线上为 `get_system_info`。
- UUID 使用 JSON 字符串。
- 时间戳使用 Unix milliseconds，类型为 JSON number。
- action 参数和结果中的 payload 使用 JSON object，空参数通常为 `{}`。
