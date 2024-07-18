### FileUploadRequest

请求

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

应答

```json
{
    "status": "ok",
    "retcode": 0,
    "data": {
        "file_id": "abcdefg-hijk-lmno-pqrstyvw"
    }
}
```

### FileUploadChunk

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

应答

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

### NewToken

请求

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

| 参数名      | 值                             | 含义                                           |
| ----------- | ------------------------------ | ---------------------------------------------- |
| type        | enum("temporary", "permanent") | 创建的Token类型                                |
| seconds     | long                           | 临时token的过期时间(若类型为永久,该参数无意义) |
| permissions | list[bool]                     | 权限                                           |

应答

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

