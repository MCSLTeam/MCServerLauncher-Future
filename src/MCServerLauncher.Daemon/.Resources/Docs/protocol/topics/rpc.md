# JSON-RPC

请求遵循 JSON-RPC 2.0，并使用 string 或 Int64 `id`：

```json
{
  "jsonrpc": "2.0",
  "id": "request-1",
  "method": "mcsl.system.info.get",
  "params": {}
}
```

成功响应包含同一个 `id` 和 `result`；失败响应包含同一个 `id` 以及标准 JSON-RPC `error` 对象。应用错误通过稳定的 code/kind 数据映射，不序列化 daemon exception。

调用 `rpc.discover` 获取完整 method、permission、params、result 和 event schema。daemon 不接受 catalog 之外的 method，也不从字符串或 assembly scan 动态发现内置 DTO。
