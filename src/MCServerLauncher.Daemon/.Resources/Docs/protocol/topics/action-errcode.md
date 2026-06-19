# 返回码

返回码是 action 响应中的 `retcode` 数字。`0` 表示成功，其余值表示错误或错误类别。

## 成功

| 响应码 | 英文名称 | 说明 |
| --- | --- | --- |
| `0` | OK | action 执行成功 |

## `10000` Request Error - 请求错误

| 响应码 | 英文名称 | 中文名称 | 说明 |
| --- | --- | --- | --- |
| `10000` | Request Error | 请求错误 | 请求错误类别 |
| `10001` | Bad Request | 无效请求 | 无法解析请求或缺失内容 |
| `10002` | Unknown Action | 未知操作 | 客户端调用未知 action |
| `10003` | Permission Denied | 权限不足 | token 权限不足 |
| `10004` | Action Unavailable | 暂不可用 | 请求的 action 当前不可用 |
| `10005` | Rate Limit Exceeded | 频率过快 | 请求频率或会话数量超过限制 |
| `10006` | Param Error | 参数错误 | 参数缺失、类型错误或格式错误 |

## `20000` Server Error - daemon 错误

| 响应码 | 英文名称 | 中文名称 | 说明 |
| --- | --- | --- | --- |
| `20001` | Unexpected Error | 意外错误 | daemon 内部发生未处理错误 |

## `21000` File Error - 文件错误

| 响应码 | 英文名称 | 中文名称 | 说明 |
| --- | --- | --- | --- |
| `21000` | File Error | 文件错误 | 文件错误类别 |
| `21001` | File Not Found | 文件不存在 | 访问的文件或目录不存在 |
| `21002` | File Already Exists | 文件已存在 | 目标文件或目录已存在 |
| `21003` | File In Use | 文件已占用 | 文件正在被使用 |
| `21004` | It's A Directory | 这是目录 | 需要文件时传入了目录 |
| `21005` | It's A File | 这是文件 | 需要目录时传入了文件 |
| `21006` | File Access Denied | 无法访问文件 | 系统拒绝读写、创建、删除或移动 |
| `21007` | Disk Full | 磁盘已满 | daemon 可用磁盘空间不足 |

## `21100` Upload/Download Error - 上传下载错误

| 响应码 | 英文名称 | 中文名称 | 说明 |
| --- | --- | --- | --- |
| `21100` | Upload/Download Error | 上传下载错误 | 上传下载错误类别 |
| `21101` | Already Uploading/Downloading | 已在传输 | 同一路径或会话已在传输 |
| `21102` | Not Uploading/Downloading | 未在传输 | 上传或下载会话不存在或已超时 |
| `21103` | File Too Big | 文件过大 | 文件超过实现允许的大小 |

## `30000` Instance Error - 实例错误

| 响应码 | 英文名称 | 中文名称 | 说明 |
| --- | --- | --- | --- |
| `30000` | Instance Error | 实例错误 | instance 错误类别 |
| `30001` | Instance Not Found | 实例不存在 | 访问的 instance 不存在 |
| `30002` | Instance Already Exists | 实例已存在 | 访问的 instance 已存在 |
| `30003` | Bad Instance State | 实例状态错误 | instance 当前状态不支持该操作 |
| `30004` | Bad Instance Type | 实例类型错误 | instance 类型不支持该操作 |

## `31000` Instance Action Error - 实例操作错误

| 响应码 | 英文名称 | 中文名称 | 说明 |
| --- | --- | --- | --- |
| `31001` | Instance Action Error | 实例操作错误 | instance 操作错误类别 |
| `31002` | Installation Error | 安装错误 | instance 安装或更新失败 |
| `31003` | Process Error | 进程错误 | instance 启动、停止、输入或进程控制失败 |
