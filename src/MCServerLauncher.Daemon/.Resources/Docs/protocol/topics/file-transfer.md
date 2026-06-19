# 文件传输

当前 C# daemon 没有实现 `/upload/<id>` 或 `/download/<id>` HTTP 文件传输接口。文件传输走 WebSocket action，上传还支持 WebSocket binary frame 快路径。

## 上传流程 {#file_upload}

1. client 调用 [`file_upload_request`](actions.md#file_upload_request)，传入目标路径、大小、可选 SHA-1 和超时。
2. daemon 预分配 `<path>.tmp` 文件并返回 `file_id`。
3. client 使用 [`file_upload_chunk`](actions.md#file_upload_chunk) 发送 Base64 分片，或使用二进制上传 frame。
4. daemon 写入分片并返回 `done` 和 `received`。
5. 全部分片收到后，daemon 校验 SHA-1。通过后将 `.tmp` 文件移动为目标文件。
6. client 可使用 [`file_upload_cancel`](actions.md#file_upload_cancel) 取消上传。

## JSON 分片上传

`file_upload_chunk` 的 `data` 字段是 Base64 字符串。适合普通 JSON RPC 调用，但会产生额外编码开销。

```json
{
  "action": "file_upload_chunk",
  "params": {
    "file_id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    "offset": 0,
    "data": "SGVsbG8="
  },
  "id": "11111111-1111-1111-1111-111111111111"
}
```

## 二进制上传 frame

`MCServerLauncher.DaemonClient` 还支持直接发送 WebSocket binary frame。frame 格式如下：

```text
[16 bytes file_id][8 bytes offset][20 bytes SHA1(data)][data bytes]
```

- `file_id` 是上传会话 ID 的 16 字节 UUID。
- `offset` 是分片偏移。
- `SHA1(data)` 是当前分片内容的 SHA-1。
- `data bytes` 是原始分片内容。

binary frame 的响应由 daemon 返回二进制上传响应，daemon client 会把它映射为 `(done, received)`。

## 下载流程 {#file_download}

1. client 调用 [`file_download_request`](actions.md#file_download_request)，传入 daemon 侧文件路径。
2. daemon 打开文件，返回 `file_id`, `size`, `sha1`。
3. client 调用 [`file_download_range`](actions.md#file_download_range)，传入 `file_id` 和 `<from>..<to>` 范围。
4. daemon 返回 `content` 字符串。
5. client 调用 [`file_download_close`](actions.md#file_download_close) 关闭会话。

## 会话超时

上传和下载会话默认超时时间由 daemon 文件会话逻辑管理，当前默认约为 30 分钟。请求参数中的 `timeout` 使用毫秒数。
