# 权限

**权限（Permission）** 用于限制 WebSocket 连接可调用的 action。daemon 在 action handler 上声明所需权限，并用当前连接 token 中的权限集合进行匹配。

## 权限字符串

单个权限由多个节点组成，节点之间用 `.` 分隔。

示例：

```text
mcsl.daemon.java_list
mcsl.daemon.file.download
mcsl.daemon.file.move.file
```

子令牌申请接口中的 `permissions` 字段是逗号分隔字符串：

```text
mcsl.daemon.java_list,mcsl.daemon.file.download
```

## 节点字符

当前实现接受大小写字母、连字符、下划线，以及通配符节点 `*` 和 `**`。

## 匹配规则

### 完全匹配

连接拥有的权限与 handler 要求权限相同时匹配。

```text
拥有: mcsl.daemon.java_list
需要: mcsl.daemon.java_list
```

### 父节点匹配子节点

拥有的权限是需要权限的父节点时匹配。

```text
拥有: mcsl.daemon.file
需要: mcsl.daemon.file.download
```

### 单节点通配符 `*`

`*` 匹配一个节点。

```text
拥有: mcsl.daemon.file.*
需要: mcsl.daemon.file.download
```

### 多节点通配符 `**`

`**` 匹配一个或多个节点。

```text
拥有: mcsl.daemon.**.download
需要: mcsl.daemon.file.download
```

## Handler 权限

当前部分 handler 声明为 `*`，表示任意已连接 client 均可调用。其他 handler 使用具体权限节点，例如：

| 权限 | 用途 |
| --- | --- |
| `mcsl.daemon.java_list` | 获取 Java 列表 |
| `mcsl.daemon.file.upload` | 文件上传 |
| `mcsl.daemon.file.download` | 文件下载 |
| `mcsl.daemon.file.info.file` | 获取文件信息 |
| `mcsl.daemon.file.info.directory` | 获取目录信息 |
| `mcsl.daemon.file.delete.file` | 删除文件 |
| `mcsl.daemon.file.delete.directory` | 删除目录 |
| `mcsl.daemon.file.rename.file` | 重命名文件 |
| `mcsl.daemon.file.rename.directory` | 重命名目录 |
| `mcsl.daemon.file.create.directory` | 创建目录 |
| `mcsl.daemon.file.move.file` | 移动文件 |
| `mcsl.daemon.file.move.directory` | 移动目录 |
| `mcsl.daemon.file.copy.file` | 复制文件 |
| `mcsl.daemon.file.copy.directory` | 复制目录 |
