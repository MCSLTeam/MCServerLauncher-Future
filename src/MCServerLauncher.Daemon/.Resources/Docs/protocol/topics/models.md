# 数据模型

本页列出现行 JSON 协议中常见的模型。字段名默认由 C# PascalCase/camelCase 转换为 snake_case。显式标注 `[JsonPropertyName]` 的字段以代码为准。

## uuid {#uuid}

UUID 在线上使用 JSON 字符串，例如：

```json
"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
```

## JavaInfo {#java_info}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `path` | string | Java 可执行文件路径 |
| `version` | string | Java 版本字符串 |
| `architecture` | string | 架构，例如 `x64` 或 `x86` |

## 文件元数据 {#file_metadata}

### FileSystemMetadata

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `creation_time` | int64 | 创建时间，Unix seconds |
| `last_access_time` | int64 | 上次访问时间，Unix seconds |
| `last_write_time` | int64 | 上次写入时间，Unix seconds |
| `hidden` | bool | 是否隐藏 |

### FileMetadata

继承 `FileSystemMetadata`，并包含：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `read_only` | bool | 是否只读 |
| `size` | int64 | 文件大小，单位字节 |

### DirectoryMetadata

继承 `FileSystemMetadata`，当前没有额外字段。

### DirectoryEntry {#directory_entry}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `parent` | string \| null | 当前目录相对根路径 |
| `files` | FileInformation[] | 文件列表 |
| `directories` | DirectoryInformation[] | 目录列表 |

`FileInformation` 字段：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `name` | string | 文件名 |
| `meta` | FileMetadata | 文件元数据 |

`DirectoryInformation` 字段：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `name` | string | 目录名 |
| `meta` | DirectoryMetadata | 目录元数据 |

## SystemInfo {#system_info}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `os` | OsInfo | 操作系统信息 |
| `cpu` | CpuInfo | CPU 信息 |
| `mem` | MemInfo | 内存信息，单位 KB |
| `drive` | DriveInformation | daemon 所在磁盘信息 |
| `drives` | DriveInformation[] | 磁盘列表。为空时 daemon 会回退为 `[drive]` |
| `daemon_version` | string \| null | daemon 版本 |

### OsInfo

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `name` | string | 系统名称 |
| `arch` | string | 系统架构 |

### CpuInfo

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `vendor` | string | CPU 厂商 |
| `name` | string | CPU 名称 |
| `count` | int32 | 处理器数量兼容字段 |
| `usage` | float64 | CPU 使用率 |
| `core_count` | int32 | 核心数 |
| `thread_count` | int32 | 线程数 |

### MemInfo

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `total` | uint64 | 总内存，单位 KB |
| `free` | uint64 | 可用内存，单位 KB |

### DriveInformation

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `drive_format` | string | 磁盘格式 |
| `total` | uint64 | 总容量，单位字节 |
| `free` | uint64 | 可用容量，单位字节 |
| `name` | string | 磁盘名称，默认空字符串 |

## DaemonReport {#daemon_report}

`daemon_report` event 的 `report` 字段使用该模型。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `os` | OsInfo | 操作系统信息 |
| `cpu` | CpuInfo | CPU 信息 |
| `mem` | MemInfo | 内存信息 |
| `drive` | DriveInformation | daemon 所在磁盘信息 |
| `start_time_stamp` | int64 | daemon 启动时间，Unix milliseconds |
| `drives` | DriveInformation[] | 磁盘列表 |
| `daemon_version` | string \| null | daemon 版本 |

## InstanceConfig {#instance_config}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `name` | string | instance 名称 |
| `target` | string | 启动目标，例如 jar 文件名、脚本名或可执行文件名 |
| `instance_type` | [`InstanceType`](#instance_type) | instance 类型 |
| `target_type` | [`TargetType`](#target_type) | 启动目标类型 |
| `uuid` | uuid string | instance ID。创建时可由客户端提供，也可由 daemon 分配/调整 |
| `mc_version` | string | 版本字符串。对 Minecraft instance 表示 Minecraft 版本 |
| `input_encoding` | string | 控制台输入编码的 .NET WebName，例如 `utf-8` |
| `output_encoding` | string | 控制台输出编码的 .NET WebName，例如 `utf-8` |
| `java_path` | string | Java 路径。非 Jar 目标可为空 |
| `arguments` | string[] | 启动参数 |
| `env` | map<string, PlaceHolderString> | 环境变量 |
| `event_rules` | [`EventRule`](#event_rule)[] | 事件规则 |

## InstanceFactorySetting {#instance_factory_setting}

继承 `InstanceConfig`，并额外包含安装来源字段。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `source` | string | 安装来源。可以是 URL、路径或脚本来源 |
| `source_type` | [`SourceType`](#source_type) | 来源类型 |
| `mirror` | [`InstanceFactoryMirror`](#instance_factory_mirror) | 镜像设置 |
| `use_post_process` | bool | 是否使用后处理 |

## InstanceReport {#instance_report}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `status` | [`InstanceStatus`](#instance_status) | 运行状态 |
| `config` | [`InstanceConfig`](#instance_config) | instance 配置 |
| `properties` | map<string, string> | instance 属性 |
| `players` | [`Player`](#player)[] | 玩家列表 |
| `performance_counter` | [`InstancePerformanceCounter`](#instance_performance_counter) | 性能计数 |

## Player {#player}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `name` | string | 玩家名 |
| `uuid` | uuid string | 玩家 UUID |

## InstancePerformanceCounter {#instance_performance_counter}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `cpu` | float64 | CPU 使用率，范围归一到 `0..100` |
| `memory` | int64 | 内存使用量，负数会归零 |

## InstanceInstallMetadata {#instance_install_metadata}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `installer_kind` | string | installer 类型 |
| `installer_source_path` | string \| null | installer 来源路径 |
| `generated_paths` | string[] | installer 生成的路径 |
| `resolved_launch_target` | string \| null | 解析后的启动目标 |
| `installed_at` | string | 安装时间，`DateTimeOffset` JSON 值 |

## GetInstanceSettingsResult {#get_instance_settings_result}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `config` | InstanceConfig | 当前配置 |
| `working_directory` | string | instance 工作目录 |
| `current_target_exists` | bool | 当前启动目标是否存在 |
| `can_edit` | bool | 是否允许编辑 |
| `edit_blocked_reason` | string \| null | 禁止编辑原因 |
| `install_metadata` | InstanceInstallMetadata \| null | 安装元数据 |

## UpdateInstanceSettingsParameter {#update_instance_settings_parameter}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `id` | uuid string | instance ID |
| `name` | string | 新名称 |
| `instance_type` | InstanceType | 新 instance 类型 |
| `java_path` | string \| null | Java 路径 |
| `arguments` | string[] | 启动参数 |
| `version` | string \| null | 版本字符串 |
| `replacement_core` | InstanceCoreReplacementRequest \| null | 替换核心请求 |
| `force_rerun_installer` | bool | 是否强制重新运行 installer |

### InstanceCoreReplacementRequest

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `uploaded_source_path` | string | 已上传核心路径 |
| `preferred_target_name` | string \| null | 首选目标文件名 |

## UpdateInstanceSettingsResult {#update_instance_settings_result}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `config` | InstanceConfig | 更新后的配置 |
| `requires_restart` | bool | 是否需要重启 instance |
| `reinstalled` | bool | 是否重新安装 |
| `deleted_generated_paths` | string[] | 删除的生成路径 |
| `preserved_original_paths` | string[] | 保留的原始路径 |

## EventType {#event_type}

| C# 值 | JSON 值 | 说明 |
| --- | --- | --- |
| `InstanceLog` | `instance_log` | instance 日志事件 |
| `DaemonReport` | `daemon_report` | daemon 报告事件 |

## InstanceLogEventMeta {#instance_log_event_meta}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `instance_id` | uuid string | instance ID |

## InstanceLogEventData {#instance_log_event_data}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `log` | string | 日志文本 |

## DaemonReportEventData {#daemon_report_event_data}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `report` | DaemonReport | daemon 报告 |

## EventRule {#event_rule}

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `id` | uuid string | 规则 ID |
| `name` | string | 规则名称 |
| `description` | string | 描述 |
| `is_enabled` | bool | 是否启用 |
| `trigger_condition` | string | `All` 或 `Any` |
| `triggers` | TriggerDefinition[] | 触发器 |
| `action_execution_mode` | string | `Sequential` 或 `Parallel` |
| `rulesets` | RulesetDefinition[] | 条件组 |
| `actions` | ActionDefinition[] | 动作列表 |

### TriggerDefinition

所有 trigger 都有 `id` 和 `type`。

| `type` | 额外字段 |
| --- | --- |
| `ConsoleOutput` | `pattern: string`, `is_regex: bool` |
| `Schedule` | `cron_expression: string` |
| `InstanceStatus` | `target_status: string` |

### RulesetDefinition

所有 ruleset 都有 `id` 和 `type`。

| `type` | 额外字段 |
| --- | --- |
| `AlwaysTrue` | 无 |
| `AlwaysFalse` | 无 |
| `InstanceStatus` | `target_status: string` |

### ActionDefinition

所有 action definition 都有 `id` 和 `type`。

| `type` | 额外字段 |
| --- | --- |
| `SendCommand` | `command: string` |
| `ChangeInstanceStatus` | `action: string`，例如 `Start`, `Stop`, `Restart`, `Kill` |
| `SendNotification` | `title: string`, `message: string`, `severity: string` |

## InstanceStatus {#instance_status}

| C# 值 | JSON 值 | 说明 |
| --- | --- | --- |
| `Running` | `running` | instance 正在运行 |
| `Stopped` | `stopped` | instance 未运行 |
| `Crashed` | `crashed` | instance 已崩溃 |

## TargetType {#target_type}

| C# 值 | JSON 值 | 说明 |
| --- | --- | --- |
| `Jar` | `jar` | Java Jar |
| `Script` | `script` | 脚本文件 |
| `Executable` | `executable` | 可执行文件 |

## SourceType {#source_type}

| C# 值 | JSON 值 | 说明 |
| --- | --- | --- |
| `None` | `none` | 初始化或非法值 |
| `Archive` | `archive` | 压缩包 |
| `Core` | `core` | 核心文件 |
| `Script` | `script` | 安装脚本 |

## InstanceFactoryMirror {#instance_factory_mirror}

| C# 值 | JSON 值 | 说明 |
| --- | --- | --- |
| `None` | `none` | 不使用镜像 |
| `BmclApi` | `bmcl_api` | 使用 BMCLAPI 镜像 |

## InstanceType {#instance_type}

| C# 值 | JSON 值 |
| --- | --- |
| `Universal` | `universal` |
| `SteamServer` | `steam_server` |
| `MCJava` | `mc_java` |
| `MCFabric` | `mc_fabric` |
| `MCForge` | `mc_forge` |
| `MCNeoForge` | `mc_neo_forge` |
| `MCQuilt` | `mc_quilt` |
| `MCCleanroom` | `mc_cleanroom` |
| `MCSpongeVanilla` | `mc_sponge_vanilla` |
| `MCSpongeForge` | `mc_sponge_forge` |
| `MCSpongeNeo` | `mc_sponge_neo` |
| `MCVanilla` | `mc_vanilla` |
| `MCCraftBukkit` | `mc_craft_bukkit` |
| `MCSpigot` | `mc_spigot` |
| `MCPaper` | `mc_paper` |
| `MCLeaf` | `mc_leaf` |
| `MCLeaves` | `mc_leaves` |
| `MCFolia` | `mc_folia` |
| `MCCanvas` | `mc_canvas` |
| `MCPufferfish` | `mc_pufferfish` |
| `MCPurpur` | `mc_purpur` |
| `MCMohist` | `mc_mohist` |
| `MCBanner` | `mc_banner` |
| `MCYouer` | `mc_youer` |
| `MCThermos` | `mc_thermos` |
| `MCCrucible` | `mc_crucible` |
| `MCTaiyitist` | `mc_taiyitist` |
| `MCCatServer` | `mc_cat_server` |
| `MCArclight` | `mc_arclight` |
| `MCBungeeCord` | `mc_bungee_cord` |
| `MCVelocity` | `mc_velocity` |
| `MCWaterfall` | `mc_waterfall` |
| `MCTravertine` | `mc_travertine` |
| `MCViaVersion` | `mc_via_version` |
| `MCGeyser` | `mc_geyser` |
| `MCDReforged` | `mc_d_reforged` |
| `MCBedrock` | `mc_bedrock` |
| `MCNukkit` | `mc_nukkit` |
| `MCBDS` | `mcbds` |
| `MCCloudburst` | `mc_cloudburst` |
| `MCPocketMine` | `mc_pocket_mine` |
| `Terraria` | `terraria` |
| `TShock` | `t_shock` |
| `TDSM` | `tdsm` |
