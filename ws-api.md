### FileUploadRequest

文件上传至后端前的请求，需要提供上传的目标路径，文件校验码和文件大小，由于使用分块传输，故还需提供分块大小。

后端接受请求后:

1. 检查chunk_size和size是否在正int范围、chunk_size是否不大于size。
2. 检查后端是否有同名的文件正在上传。
3. 预分配空间，生成file_id。
4. 回应file_id。

(虽然chunk_size和size都是long处理，但是C#的FileStream.Write方法只支持int范围的寻址，故文件大小不宜超过2GB (2^31-1 B)，如果超过会返回error)

##### 请求

```json
{
    "action": "file_upload_request",
    "params":{
        "path": "path/to/file",
        "sha1": "114514114514114514114514114514",
        "chunk_size": 1024,
        "size": 1919810
    }
}
```

| 参数       | 值   | 含义                     |
| ---------- | ---- | ------------------------ |
| path       | str  | 上传的文件将要存放的位置 |
| sha1       | str  | 文件SHA-1校验码          |
| chunk_size | long | 分块传输的分块大小       |
| size       | long | 文件总大小               |

##### 应答

```json
{
    "status": "ok",
    "retcode": 0,
    "data": {
        "file_id": "abcdefg-hijk-lmno-pqrstyvw"
    }
}
```

| 参数    | 值   | 含义              |
| ------- | ---- | ----------------- |
| file_id | str  | 文件上传句柄/标识 |



### FileUploadChunk

成功申请到file_id后，即可上传文件的分块数据，此时需要提供file_id，分块偏移量，分块数据。

后端接受到此请求后:

1. 检查offset是否大于0且小于文件长度。
2. 将data按大端解码为byte[]。
3. 按偏移量和计算的分块长度写入文件。
4. 检查文件是否下载完毕，若下载完毕，校验SHA-1。

##### 请求

```json
{
    "action": "file_upload_chunk",
    "params":{
        "file_id": "abcdefg-hijk-lmno-pqrstyvw",
        "offset": 114514,
        "data": "????????????????????????????????????????????????????????????????????????????????????????????????????????"
    }
}
```

| 参数    | 值   | 含义                |
| ------- | ---- | ------------------- |
| file_id | str  | 文件上传句柄/标识   |
| offset  | long | 分块偏移量          |
| data    | str  | 字符串形式的bytes[] |

##### 应答

```json
{
    "status": "ok",
    "retcode": 0,
    "data": {
        "done": false,
        "received": 1048576
    }
}
```

| 参数     | 值   | 含义           |
| -------- | ---- | -------------- |
| done     | bool | 是否上传完毕   |
| received | long | 已接受的字节数 |



### NewToken

新建token的请求，包括创建临时，永久以及带自定义权限的token

##### 请求

```json
{
    "action": "new_token",
    "params":{
        "type": "temporary",
        "seconds":86400,
        "permissions":[]
    }
}
```

| 参数名      | 值                             | 含义                                               |
| ----------- | ------------------------------ | -------------------------------------------------- |
| type        | enum("temporary", "permanent") | 创建的Token类型                                    |
| seconds     | long                           | 临时token的过期时长(s) (若类型为永久,该参数无意义) |
| permissions | list[bool]                     | 权限                                               |

##### 应答

```json
{
    "status": "ok",
    "retcode": 0,
    "data": {
        "token": "巴拉巴拉",
        "expired": 16777777777
    }
}
```

| 参数名  | 值     | 含义             |
| ------- | ------ | ---------------- |
| token   | string | 请求的token      |
| expired | long   | 到期的unix时间戳 |

