# 事件列表

## 实例日志更新 {#instance-log}

<primary-label ref="instance-event"/>
<secondary-label ref="1.0"/>

- **事件名称**：`instance_log`
- **C# 名称**：`InstanceLog`
- **事件说明**：instance 进程输出日志时触发。
- **订阅权限**：当前 handler 声明为 `*`。

### 订阅 meta {collapsible="true"}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `instance_id` | uuid string | instance ID |

### 推送 data {collapsible="true"}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `log` | string | 输出日志文本 |

## Daemon 报告 {#daemon-report}

<primary-label ref="system-event"/>
<secondary-label ref="1.0"/>

- **事件名称**：`daemon_report`
- **C# 名称**：`DaemonReport`
- **事件说明**：daemon 周期性推送系统和 daemon 状态。当前周期约为 3 秒。
- **订阅权限**：当前 handler 声明为 `*`。

### 订阅 meta {collapsible="true"}

无过滤器，`meta` 可为 `null`。

### 推送 data {collapsible="true"}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `report` | [`DaemonReport`](models.md#daemon_report) | daemon 报告对象 |
