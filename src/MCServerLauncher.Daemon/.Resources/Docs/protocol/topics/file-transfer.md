# 文件传输

文件控制使用 `mcsl.file.*` JSON-RPC method，内容使用 versioned binary frame。单个会话属于创建它的连接和权限快照，默认最大 chunk 为 1 MiB，daemon 负责 offset、长度、hash、超时和清理校验。

## Upload

1. 调用 `mcsl.file.upload.open`，提供 daemon-relative path、长度和可选 SHA-256。
2. 使用 `UploadChunk` binary frame 顺序发送内容；daemon 通过 `mcsl.file.upload.ack` notification 确认 accepted/rejected。
3. 调用 `mcsl.file.upload.close` 校验并原子提交，或调用 `mcsl.file.upload.cancel` 清理 staging 文件。

## Download

1. 调用 `mcsl.file.download.open` 获取 session id、长度、SHA-256、最大 chunk 和 expiry。
2. 调用 `mcsl.file.download.read`；JSON-RPC result 描述 offset/length/final，实际内容紧随为 `DownloadChunk` binary frame。
3. 调用 `mcsl.file.download.close` 释放会话。

## Binary header

固定 header 为 32 bytes：version(1)、kind(1)、reserved(2)、big-endian GUID(16)、little-endian offset(8)、little-endian payload length(4)，之后是 payload。当前 version 为 `1`，reserved bytes 必须为零。
