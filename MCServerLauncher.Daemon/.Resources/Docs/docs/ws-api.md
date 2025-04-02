### FileUploadRequest

文件上传至后端前的请求，需要提供上传的目标路径，文件校验码和文件大小，由于使用分块传输，故还需提供分块大小。

后端接受请求后:

1. 检查chunk_size和size是否在正int范围、chunk_size是否不大于size。
2. 检查后端是否有同名的文件正在上传。
3. 预分配空间，生成file_id。
4. 回应file_id。

(虽然chunk_size和size都是long处理，但是C#的FileStream.Write方法只支持int范围的寻址，故文件大小不宜超过2GB (2^31-1 B)
，如果超过会返回error)

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

| 参数         | 值             | 含义                                                |
|------------|---------------|---------------------------------------------------|
| path       | Optional[str] | 上传的文件将要存放的位置，若为空，上传到默认文件夹(FileManager.UploadRoot) |
| sha1       | Optional[str] | 文件SHA-1校验码,为空则不检查SHA-1                            |
| chunk_size | long          | 分块传输的分块大小                                         |
| size       | long          | 文件总大小                                             |

##### 响应

```json
{
    "status": "ok",
    "retcode": 0,
    "data": {
        "file_id": "abcdefg-hijk-lmno-pqrstyvw"
    }
}
```

| 参数      | 值   | 含义        |
|---------|-----|-----------|
| file_id | str | 文件上传句柄/标识 |

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

| 参数      | 值    | 含义            |
|---------|------|---------------|
| file_id | str  | 文件上传句柄/标识     |
| offset  | long | 分块偏移量         |
| data    | str  | 字符串形式的bytes[] |

##### 响应

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

| 参数       | 值    | 含义      |
|----------|------|---------|
| done     | bool | 是否上传完毕  |
| received | long | 已接受的字节数 |

### FileUploadCancel

通过file_id取消上传的任务

后端接受到此请求后:

1. 从uploadSessions取出该上传info
2. 关闭文件指针，并删除临时文件

##### 请求

```json
{
    "action": "file_upload_cancel",
    "params":{
        "file_id": "..."
    }
}
```

| 参数名     | 值   | 含义              |
|---------|-----|-----------------|
| file_id | str | 要取消上传任务的file_id |

##### 响应

```json
{
    "status": "ok",
    "retcode": 0,
    "data": {}
}
```

| 参数名 | 值 | 含义 |
|-----|---|----|

### GetFileInfo

获取指定路径文件的信息

##### 请求

```json
{
    "action": "get_file_info",
    "params":{
        "path": "..."
    }
}
```

| 参数名  | 值   | 含义           |
|------|-----|--------------|
| path | str | 目标路径文件(相对路径) |

##### 响应

```json
{
    "status": "ok",
    "retcode": 0,
    "data": {
        "meta":{
            "read_only": false,
            "size": 114514,
            "creation_time": 17777777777,
            "last_write_time": 17777777777,
            "last_access_time": 17777777777,
        }
    }
}
```

| 参数名  | 值            | 含义    |
|------|--------------|-------|
| meta | FileMetadata | 文件元信息 |

##### FileMetadata

| 参数名              | 值    | 含义        |
|------------------|------|-----------|
| read_only        | bool | 是否为只读     |
| size             | long | 文件大小      |
| create_time      | long | 文件创建时间戳   |
| last_write_time  | long | 文件上次写入时间戳 |
| last_access_time | long | 文件上次访问时间戳 |

### GetDirectoryInfo

获取目标目录目录项

##### 请求

```json
{
    "action": "get_directory_info",
    "params":{
        "path": "..."
    }
}
```

| 参数名  | 值   | 含义           |
|------|-----|--------------|
| path | str | 目标路径文件(相对路径) |

##### 响应

```json
{
  "status": "ok",
  "retcode": 0,
  "data": {
    "parent": "relative/to/daemon/root",
    "files": [
      {
        "name": "file1",
        "type": "file",
        "meta": {
          "read_only": false,
          "size": 114514,
          "link_target": "some/path",
          "hidden": false,
          "creation_time": 17777777777,
          "last_write_time": 17777777777,
          "last_access_time": 17777777777
        }
      }
    ],
    "directories": [
      {
        "name": ".dir1",
        "type": "directory",
        "meta": {
          "hidden": true,
          "link_target": "some/path",
          "creation_time": 17777777777,
          "last_write_time": 17777777777,
          "last_access_time": 17777777777
        }
      }
    ]
  }
}
```

| 参数名         | 值                                         | 含义                  |
|-------------|-------------------------------------------|---------------------|
| parent      | str                                       | 相对于daemon root的相对路径 |
| files       | list[DirectoryEntry.FileInformation]      | 当前目录下子文件的信息列表       |
| directories | list[DirectoryEntry.DirectoryInformation] | 当前目录下子文件夹的信息列表      |

```c#
internal record FileSystemMetadata
{
    public DateTime CreationTime;
    public DateTime LastAccessTime;
    public DateTime LastWriteTime;
    public bool Hidden;
    public string? LinkTarget;

    // ...
}

internal record FileMetadata : FileSystemMetadata
{
    public long Size;
    public bool ReadOnly;

    // ...
}

internal record DirectoryMetadata : FileSystemMetadata
{
    
    // ...
}

internal record DirectoryEntry
{
    public FileInformation[] Files;
    public DirectoryInformation[] Directories;
    public string? Parent;

	// ...

    public record FileInformation
    {
        public string Name;
        public FileMetadata Meta;
		
        // ...
    }

    public record DirectoryInformation
    {
        public string Name;
        public DirectoryMetadata Meta;
        
        // ...
    }
}
```

### FileDownloadRequest

客户端请求下载文件，服务端打开文件流并创建Guid作为handle发回客户端

##### 请求

```json
{
    "action": "file_download_request",
    "params":{
		"path": "..."
    }
}
```

| 参数名  | 值   | 含义      |
|------|-----|---------|
| path | str | 相对路径，非空 |

##### 应答

```json
{
    "status": "ok",
    "retcode": 0,
    "data": {
        "file_id": "xxxx",
        "size": 1919810,
        "sha1": "114514114514114514114514114514"
    }
}
```

| 参数名     | 值    | 含义       |
|---------|------|----------|
| file_id | str  | 下载文件的句柄  |
| sha1    | str  | SHA-1校验码 |
| size    | long | 文件大小     |

### FileDownloadRange

客户端请求下载 句柄为file_id文件 的一部分

##### 请求

```json
{
    "action": "file_download_range",
    "params":{
        "file_id": "...",
		"range": "0..114514"
    }
}
```

| 参数名     | 值                 | 含义          |
|---------|-------------------|-------------|
| file_id | str               | 下载文件句柄      |
| range   | Pattern[int..int] | 下载文件范围，左闭右开 |

##### 响应

```json
{
    "status": "ok",
    "retcode": 0,
    "data":{
        "content": "????????????????????????????????????????????????????????????????????????????????????????????????????????"
    }
}
```

| 参数名  | 值   | 含义            |
|------|-----|---------------|
| data | str | 字符串形式的bytes[] |

### FileDownloadClose

客户端终止或者完成下载文件，请求服务端关闭和释放相关文件资源

##### 请求

```json
{
    "action": "file_download_close",
    "params":{
        "file_id": "..."
    }
}
```

| 参数名     | 值   | 含义     |
|---------|-----|--------|
| file_id | str | 下载文件句柄 |

##### 响应

```json
{
    "status": "ok",
    "retcode": 0,
    "data": {}
}
```

| 参数名 | 值 | 含义 |
|-----|---|----|

### GetJavaList

获取java列表不一定是实时的，他是基于时间缓存的，一次请求后最多**<u>60s</u>**
就会使得下次请求重新扫描java列表（由于人们并不会频繁的为计算机增减jre，故使用IAsyncTimedCacheable去缓存java扫描结果以优化整体性能，尤其是请求高峰期）。

具体代码细节参考Daemon\Application.cs的ConfigureContainer中IAsyncTimedCacheable<List<JavaScanner.JavaInfo>>单例。

##### 请求

```json
{
    "action": "get_java_list",
    "params":{}
}
```

该请求无参数

##### 响应

```json
{
    "status": "ok",
    "retcode": 0,
    "data": {
        "java_list": [
            {
                "path": "C:\\Program Files\\Common Files\\Oracle\\Java\\javapath\\java.exe",
                "version": "21.0.1",
                "architecture": "x64"
            },
            {
                "path": "C:\\Program Files\\Common Files\\Oracle\\Java\\javapath_target_233021531\\java.exe",
                "version": "21.0.1",
                "architecture": "x64"
            },
            ...
        ]
    }
}
```

| 参数名       | 值          | 含义          |
|-----------|------------|-------------|
| java_list | JavaInfo[] | 包含java信息的列表 |

其中JavaInfo定义如下

```c#
public struct JavaInfo
{
    public string Path { get; set; }
    public string Version { get; set; }
    public string Architecture { get; set; }

    public override string ToString()
    {
        return JsonConvert.SerializeObject(this, Formatting.Indented);
    }
}
```

### Ping

最简单的请求：客户端发送一个包，服务端返回一个应答。

##### 请求

```json
{
    "action": "ping",
    "params":{}
}
```

##### 应答

```json
{
  "status": "ok",
  "retcode": 0,
  "data": {
    "time": 1723550404
  }
}
```

| 参数名  | 值    | 含义                       |
|------|------|--------------------------|
| time | long | heartbeat请求应答的Unix时间戳(s) |

