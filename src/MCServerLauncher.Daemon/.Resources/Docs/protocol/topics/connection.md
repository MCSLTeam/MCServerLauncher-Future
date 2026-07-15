# 连接 daemon

## HTTP metadata

`GET /` 返回 daemon 名称、版本、状态和 `api_version: "v2"`。`GET /info` 返回名称、版本和相同的 API 标识。

## WebSocket

使用主令牌或由主令牌签发的 JWT 子令牌建立连接：

```text
ws(s)://<host>:<port>/api/v2?token=<token>
```

daemon 只接受路径完全等于 `/api/v2` 的 WebSocket upgrade。无效或缺失令牌返回未授权响应；其他 WebSocket 路径不属于协议 endpoint。

连接内的 JSON-RPC response、event notification、upload acknowledgement 和 binary frame 共用一个 connection-owned 有序写队列。客户端重连后必须重新建立订阅并重新同步 instance catalog；未完成的 binary session 不跨连接恢复。
