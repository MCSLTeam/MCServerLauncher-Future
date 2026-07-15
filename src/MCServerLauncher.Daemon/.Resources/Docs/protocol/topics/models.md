# 数据模型

runtime schema 以 `rpc.discover` 返回的 OpenRPC 文档为准。内置 JSON metadata 来自显式 source-generated `JsonTypeInfo`，字段使用 catalog/context 定义的 snake_case 名称。

主要 contract 组：

- `Contracts.Instances`: instance configuration、report、catalog snapshot/delta。
- `Contracts.Files`: contained path、metadata、upload/download session。
- `Contracts.System`: OS、CPU、memory、drive 和 Java runtime。
- `Contracts.EventRules`: event-rule query/update 以及 persisted rule models。
- `Contracts.Protocol`: JSON-RPC envelope、typed event、OpenRPC 和 binary frame。

客户端不应依赖未列入 runtime catalog 的 Common 类型，也不应推断未知 envelope。
