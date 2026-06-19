# Action 列表

本页按当前 `MCServerLauncher.Common.ProtoType.Action.ActionType` 和 daemon action handler 实现列出 action。JSON 名称由 C# enum 名称转换为 snake_case。

## 订阅事件 {#subscribe_event}

<primary-label ref="system-action"/>
<secondary-label ref="1.0"/>

- **操作名称**：`subscribe_event`
- **C# 名称**：`SubscribeEvent`
- **所需权限**：`*`

### 请求 {collapsible="true"}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `type` | [`EventType`](models.md#event_type) | 要订阅的事件类型 |
| `meta` | object \| null | 事件过滤元数据。`instance_log` 需要 `InstanceLogEventMeta`，`daemon_report` 可为 `null` |

### 响应 {collapsible="true"}

无数据，`data` 为 `{}`。

## 取消订阅事件 {#unsubscribe_event}

<primary-label ref="system-action"/>
<secondary-label ref="1.0"/>

- **操作名称**：`unsubscribe_event`
- **C# 名称**：`UnsubscribeEvent`
- **所需权限**：`*`

### 请求 {collapsible="true"}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `type` | [`EventType`](models.md#event_type) | 要取消订阅的事件类型 |
| `meta` | object \| null | 与订阅时相同的过滤元数据。为 `null` 时取消该事件类型的全部订阅 |

### 响应 {collapsible="true"}

无数据，`data` 为 `{}`。

## Ping {#ping}

<primary-label ref="system-action"/>
<secondary-label ref="1.0"/>

- **操作名称**：`ping`
- **C# 名称**：`Ping`
- **所需权限**：`*`

### 请求 {collapsible="true"}

无参数。

### 响应 {collapsible="true"}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `time` | int64 | daemon 返回时的 Unix milliseconds 时间戳 |

## 获取系统信息 {#get_system_info}

<primary-label ref="system-action"/>
<secondary-label ref="1.0"/>

- **操作名称**：`get_system_info`
- **C# 名称**：`GetSystemInfo`
- **所需权限**：`*`

### 请求 {collapsible="true"}

无参数。

### 响应 {collapsible="true"}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `info` | [`SystemInfo`](models.md#system_info) | daemon 当前系统信息 |

## 获取权限 {#get_permissions}

<primary-label ref="system-action"/>
<secondary-label ref="1.0"/>

- **操作名称**：`get_permissions`
- **C# 名称**：`GetPermissions`
- **所需权限**：`*`

### 请求 {collapsible="true"}

无参数。

### 响应 {collapsible="true"}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `permissions` | string[] | 当前连接 token 拥有的权限字符串 |

## 获取 Java 环境 {#get_java_list}

<primary-label ref="system-action"/>
<secondary-label ref="1.0"/>

- **操作名称**：`get_java_list`
- **C# 名称**：`GetJavaList`
- **所需权限**：`mcsl.daemon.java_list`

### 请求 {collapsible="true"}

无参数。

### 响应 {collapsible="true"}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `java_list` | [`JavaInfo`](models.md#java_info)[] | daemon 扫描到的 Java 列表 |

## 文件上传请求 {#file_upload_request}

<primary-label ref="file-action"/>
<secondary-label ref="1.0"/>

- **操作名称**：`file_upload_request`
- **C# 名称**：`FileUploadRequest`
- **所需权限**：`mcsl.daemon.file.upload`

### 请求 {collapsible="true"}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `path` | string \| null | daemon 侧目标路径。为 `null` 时上传到 daemon 上传缓存目录 |
| `size` | int64 | 文件大小，单位字节 |
| `sha1` | string \| null | 可选，完整文件 SHA-1。提供时 daemon 会在完成后校验 |
| `timeout` | int64 \| null | 可选，会话超时毫秒数 |

### 响应 {collapsible="true"}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `file_id` | uuid string | 上传会话 ID |

## 上传文件分片 {#file_upload_chunk}

<primary-label ref="file-action"/>
<secondary-label ref="1.0"/>

- **操作名称**：`file_upload_chunk`
- **C# 名称**：`FileUploadChunk`
- **所需权限**：`mcsl.daemon.file.upload`

### 请求 {collapsible="true"}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `file_id` | uuid string | 上传会话 ID |
| `offset` | int64 | 分片写入偏移 |
| `data` | string | Base64 编码的分片内容 |

### 响应 {collapsible="true"}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `done` | bool | 是否完成上传 |
| `received` | int64 | daemon 已收到的字节数 |

## 取消上传文件 {#file_upload_cancel}

<primary-label ref="file-action"/>
<secondary-label ref="1.0"/>

- **操作名称**：`file_upload_cancel`
- **C# 名称**：`FileUploadCancel`
- **所需权限**：`mcsl.daemon.file.upload`

### 请求 {collapsible="true"}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `file_id` | uuid string | 上传会话 ID |

### 响应 {collapsible="true"}

无数据，`data` 为 `{}`。

## 文件下载请求 {#file_download_request}

<primary-label ref="file-action"/>
<secondary-label ref="1.0"/>

- **操作名称**：`file_download_request`
- **C# 名称**：`FileDownloadRequest`
- **所需权限**：`mcsl.daemon.file.download`

### 请求 {collapsible="true"}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `path` | string | daemon 侧文件路径 |
| `timeout` | int64 \| null | 可选，会话超时毫秒数 |

### 响应 {collapsible="true"}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `file_id` | uuid string | 下载会话 ID |
| `size` | int64 | 文件大小，单位字节 |
| `sha1` | string | 文件 SHA-1 |

## 下载文件范围 {#file_download_range}

<primary-label ref="file-action"/>
<secondary-label ref="1.0"/>

- **操作名称**：`file_download_range`
- **C# 名称**：`FileDownloadRange`
- **所需权限**：`mcsl.daemon.file.download`

### 请求 {collapsible="true"}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `file_id` | uuid string | 下载会话 ID |
| `range` | string | 范围字符串，格式为 `<from>..<to>` |

### 响应 {collapsible="true"}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `content` | string | 当前实现返回 BigEndianUnicode 字符串内容 |

## 关闭下载文件 {#file_download_close}

<primary-label ref="file-action"/>
<secondary-label ref="1.0"/>

- **操作名称**：`file_download_close`
- **C# 名称**：`FileDownloadClose`
- **所需权限**：`mcsl.daemon.file.download`

### 请求 {collapsible="true"}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `file_id` | uuid string | 下载会话 ID |

### 响应 {collapsible="true"}

无数据，`data` 为 `{}`。

## 文件信息 {#file-info-actions}

<primary-label ref="file-action"/>
<secondary-label ref="1.0"/>

| 操作名称 | C# 名称 | 权限 | 参数 | 响应 |
| --- | --- | --- | --- | --- |
| `get_file_info` | `GetFileInfo` | `mcsl.daemon.file.info.file` | `path: string` | `meta: FileMetadata` |
| `get_directory_info` | `GetDirectoryInfo` | `mcsl.daemon.file.info.directory` | `path: string` | `parent: string?`, `files`, `directories` |

## 文件操作 {#file-operation-actions}

<primary-label ref="file-action"/>
<secondary-label ref="1.0"/>

| 操作名称 | C# 名称 | 权限 | 参数 |
| --- | --- | --- | --- |
| `delete_file` | `DeleteFile` | `mcsl.daemon.file.delete.file` | `path: string` |
| `delete_directory` | `DeleteDirectory` | `mcsl.daemon.file.delete.directory` | `path: string`, `recursive: bool` |
| `rename_file` | `RenameFile` | `mcsl.daemon.file.rename.file` | `path: string`, `new_name: string` |
| `rename_directory` | `RenameDirectory` | `mcsl.daemon.file.rename.directory` | `path: string`, `new_name: string` |
| `create_directory` | `CreateDirectory` | `mcsl.daemon.file.create.directory` | `path: string` |
| `move_file` | `MoveFile` | `mcsl.daemon.file.move.file` | `source_path: string`, `destination_path: string` |
| `move_directory` | `MoveDirectory` | `mcsl.daemon.file.move.directory` | `source_path: string`, `destination_path: string` |
| `copy_file` | `CopyFile` | `mcsl.daemon.file.copy.file` | `source_path: string`, `destination_path: string` |
| `copy_directory` | `CopyDirectory` | `mcsl.daemon.file.copy.directory` | `source_path: string`, `destination_path: string` |

所有这些操作成功时返回空对象 `{}`。

## Instance 操作 {#instance-actions}

<primary-label ref="instance-action"/>
<secondary-label ref="1.0"/>

| 操作名称 | C# 名称 | 权限 | 参数 | 响应 |
| --- | --- | --- | --- | --- |
| `add_instance` | `AddInstance` | `*` | `setting: InstanceFactorySetting` | `config: InstanceConfig` |
| `remove_instance` | `RemoveInstance` | `*` | `id: uuid` | 空对象 |
| `start_instance` | `StartInstance` | `*` | `id: uuid` | 空对象 |
| `stop_instance` | `StopInstance` | `*` | `id: uuid` | 空对象 |
| `kill_instance` | `KillInstance` | `*` | `id: uuid` | 空对象 |
| `send_to_instance` | `SendToInstance` | `*` | `id: uuid`, `message: string` | 空对象 |
| `get_instance_report` | `GetInstanceReport` | `*` | `id: uuid` | `report: InstanceReport` |
| `get_all_reports` | `GetAllReports` | `*` | 无参数 | `reports: map<uuid, InstanceReport>` |
| `get_instance_log_history` | `GetInstanceLogHistory` | `*` | `id: uuid` | `logs: string[]` |
| `get_instance_settings` | `GetInstanceSettings` | `*` | `id: uuid` | [`GetInstanceSettingsResult`](models.md#get_instance_settings_result) |
| `update_instance_settings` | `UpdateInstanceSettings` | `*` | [`UpdateInstanceSettingsParameter`](models.md#update_instance_settings_parameter) | [`UpdateInstanceSettingsResult`](models.md#update_instance_settings_result) |

## Event rule 操作 {#event-rule-actions}

<primary-label ref="instance-action"/>
<secondary-label ref="1.0"/>

| 操作名称 | C# 名称 | 权限 | 参数 | 响应 |
| --- | --- | --- | --- | --- |
| `get_event_rules` | `GetEventRules` | `*` | `instance_id: uuid` | `rules: EventRule[]` |
| `save_event_rules` | `SaveEventRules` | `*` | `instance_id: uuid`, `rules: EventRule[]` | 空对象 |
