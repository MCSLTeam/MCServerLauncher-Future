# Daemon Application Core、V2 Cutover 与 Startup Plugin Host 实施计划

> **Current authority:** 本文整体取代 2026-06-27 至 2026-06-30 的旧版增量迁移设计。2026-07-10 项目负责人在架构访谈中冻结的决策优先于旧 plan、旧 review 中的兼容性建议。
>
> **Plan status:** Architecture approved. Phase 0 through Phase 3 and Phase 4A daemon V2 are complete and independently reviewed; the Phase 4A independent Sol max final review has 0 open P0/P1/P2 findings. Phase 4B daemon-client/WPF cutover and Phase 4C sole-endpoint switch/V1 deletion remain pending. The `/api/v2` TouchSocket feature is production-composed, but `/api/v1` remains a branch-internal migration path until 4C; therefore Phase 4 is not release-complete or product-complete and the branch is not releasable. Resume with Phase 4B without weakening the release-atomic Phase 4 exit.
>
> **Touched areas for implementation:** `docs`, `agent-docs`, `backend`, `protocol`, `serialization`, `storage`, `frontend`, `tests`, `benchmarks`, `workflow`, `integrations`.

## 1. 根本诉求

本任务不是“给现有 action/event 协议外挂一个插件加载器”，也不是“把 daemon internals 搬进一个 NuGet”。根本诉求是建立一个唯一、稳定、transport-neutral 的 daemon application boundary：

```text
daemon internal implementation
          ↓ implements
MCServerLauncher.Daemon.API application contracts
          ↑ consumed by
daemon console / V2 dispatcher / DaemonClient / WPF / trusted plugins
```

最终必须同时满足：

1. 业务行为只有 application core 一份实现；transport、console、plugin 都只是入口。
2. `IInstanceManager`、`IInstance`、`InstanceProcess`、`FileManager`、`WsContext`、`IResolver`、TouchSocket 类型不进入公共 API。
3. daemon、DaemonClient、WPF 一次性切换到 V2；最终产品不保留 V1 fallback、双 dispatcher 或兼容开关。
4. 插件首期只做 startup-only trusted in-process host，并以一个真实健康状态插件证明 API 可用。
5. hot read 使用深度不可变 COW snapshot；不让插件读取 mutable runtime collection。
6. typed RPC、typed local event、runtime OpenRPC 与 checked-in Apifox 从同一 frozen catalog 派生。

## 2. 对旧设计的审核结论

旧 plan 的方向正确，但实施模型已被本轮决策推翻，不能继续局部修补。

| 旧设计 | 审核结论 | 新设计 |
|---|---|---|
| V1/V2 additive、DaemonClient/WPF 保留 fallback | 会形成两个 daemon 和永久兼容债 | 一次性 V2 cutover，随后删除 V1 |
| 先建 wide plugin API，再逐步迁 core | 插件会建立在旧 action/event seam 上 | application core first，plugin host last |
| `IManageApi`/`IStoreApi`/`IHookRegistry`/factory/TouchSocket 扩展首期并行 | 首期没有真实消费者，表面积失控 | 首期仅 `rpc.register`、`event.publish`、`instance.query` |
| `Task<Result<Unit, Error>> KillAsync` | 与已冻结的 `void KillInstance(Guid)` signal 语义冲突 | manager 继续 void signal；application/V2 success 仅表示调用正常返回 |
| Apifox baseline 与 live OpenRPC 分工 | 仍是两个可漂移来源 | typed frozen catalog 是唯一事实源 |
| plugin unload 与动态清理 | startup-only 场景不需要，ALC unload 代价高 | 首期不热加载、不热卸载；shutdown 只做逆序 Stop |
| `Result<T, Error>`、`ActionError`、JSON-RPC error 并存 | 错误语义持续漂移 | `Result<T, DaemonError>` 为唯一 application error |
| 通用 service locator `IPluginServices.GetRequired<T>` | 隐藏依赖并扩大可见面 | plugin context 只给三个显式 capability surface |
| runtime config/file watcher 参与状态变更 | 破坏 application command 的写入权威 | 外部配置编辑仅在重启时生效 |

安全不再依赖 runtime fallback。迁移安全由 characterization tests、完整 parity inventory、同一 change set 内的 client cutover 和明确删除清单保证。

## 3. 六大设计原则

1. **单一核心服务层**：所有入口只调用 application core；不得在 V2 handler、console 或 plugin adapter 中复制业务规则。
2. **每个并存都必须有退场条件**：本计划最终态不允许 V1/V2、旧/新 error、旧/新 event bus 并存；阶段内临时并存不得跨越 Phase exit。
3. **一致性优先于新颖**：统一 async、`CancellationToken`、`Result<T, DaemonError>`、命名、nullability、source-generated STJ；不为了展示新语法改变项目风格。
4. **YAGNI 收敛**：首期不做 hook、factory/installer extension、storage write API、plugin dependency、hot unload、batch JSON-RPC、raw transport escape hatch。
5. **强类型且最小横切**：method、event、capability、permission 使用受验证的 value type/descriptor；不以裸字符串和通用 DI/service locator 组织公共 API。
6. **新特性必须改善可读性或性能**：`FrozenDictionary`、`ImmutableDictionary`、`System.Threading.Lock`、`Volatile.Read`、source-gen STJ 有明确收益；无收益的 C# 14 语法糖不进入本任务。

每个新抽象必须至少有两个真实调用方或实现。已知满足该门槛的抽象：

- `IDaemonApplication`：daemon local implementation + DaemonClient remote implementation；
- `StatePublisher<T>`：daemon instance catalog + DaemonClient remote snapshot mirror；
- frozen protocol descriptor：daemon dispatcher + DaemonClient serializer + docs generator；
- typed event abstraction：built-in daemon events + plugin events + remote bridge。

## 4. 范围冻结

### 4.1 首期必须交付

- `MCServerLauncher.Daemon.API` 单一 `net10.0` NuGet；
- transport-neutral application services；
- `DaemonError` / `PluginError`；
- `StatePublisher<T>` / `PublishedState<T>` / instance snapshot source；
- `/api/v2` JSON-RPC 2.0 profile 与 binary transfer session；
- typed frozen RPC/event catalog、runtime OpenRPC、generated checked-in Apifox；
- DaemonClient 与 WPF 全量 V2 cutover；
- V1 action/event/client/generator 删除；
- MessagePipe-backed local typed event bus；
- startup-only trusted plugin host；
- instance health acceptance plugin；
- NuGet/package validation、protocol tests、published-host plugin smoke test 和 benchmarks。

### 4.2 首期明确不做

- V1 fallback、legacy endpoint、legacy envelope auto-detection；
- JSON-RPC batch；
- Native AOT 或 trimmed plugin-enabled publish；
- no-plugin/AOT 第二 SKU；
- runtime plugin install/reload/unload；
- plugin-to-plugin dependency/service import；
- 新 `InstanceType`、factory、installer extension；
- plugin instance filesystem write API；
- hook system；
- `MCServerLauncher.Daemon.API.TouchSocket`；
- root `IServiceProvider`、TouchSocket、Serilog 或 MessagePipe 公共类型；
- reflection serialization fallback；
- runtime `daemon_instance.json` reload；
- assembly metadata manifest fallback；
- legacy relay envelope。

### 4.3 必做但延后的下一里程碑

首期完成后必须实施 §15 的 manifest dependency DAG 与 `PluginA.Contracts` 直接进程内服务调用。首期不预埋未使用的 service registry、dependency resolver 或 unload proxy。

## 5. 目标项目与依赖方向

```text
MCServerLauncher.Common
    domain/wire DTO, JSON-RPC envelope, binary frame contracts
                ↑
MCServerLauncher.Daemon.API
    application interfaces, errors, state/snapshot abstractions,
    typed RPC/event definitions, plugin SDK
      ↑                    ↑                    ↑
MCServerLauncher.Daemon  MCServerLauncher.DaemonClient  external plugin
      ↑                    ↑
      └────────────── MCServerLauncher.WPF
```

约束：

- `Daemon.API` 只依赖 Common、RustyOptions、BCL 和 `Microsoft.Extensions.Logging.Abstractions`。
- `Daemon.API` 不依赖 daemon executable、DaemonClient、WPF、TouchSocket、MessagePipe、Serilog 或自定义 generator。
- shared serialized DTO 继续属于 Common；application/plugin interfaces 属于 Daemon.API。
- DaemonClient 实现与 local daemon 相同的 application interfaces；WPF 不再引用 `ActionType` 或手写 V1 packet。
- plugin SDK 与 application API 在一个 NuGet 中，不再拆 `Manage`/`Store`/`Hooks`/TouchSocket 子包。
- `MCServerLauncher.Daemon.API` 是插件作者唯一显式引用的 SDK entry package；Common 作为版本锁定的传递 NuGet 依赖发布。两包使用同一 release/version line，Daemon.API package validation 与临时 consumer restore 同时校验该依赖图；不把 Common 源码私嵌进 Daemon.API nupkg。
- daemon release RID 冻结为现有 workflow matrix：`win-x64`、`win-x86`、`win-arm64`、`linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64`。WPF 只随 Windows RID 发布。旧 `win-arm`/`linux-arm` pubxml 不构成受支持产品面，并在 packaging cleanup 删除。

### 5.1 API versioning

- package 使用 SemVer；manifest 使用 NuGet version range，并由 daemon-internal `NuGet.Versioning` 解析，不手写 range parser；
- `AssemblyVersion` 在同一 major 内固定；
- 已发布 interface 在同一 major 内禁止增加成员；新能力通过新 interface；
- RustyOptions 出现在 public signatures，因此是 API ABI 的一部分：同一 Daemon.API major 固定其 compatible package/assembly line，升级必须经过 package validation 与 plugin compile matrix；
- 启用 .NET package validation，并维护 baseline package；
- Daemon.API 包含 opt-in `buildTransitive` plugin-bundle target。标记 `MCSLPluginBundle=true` 的 plugin project 在 publish 时保留 shared contracts 的 compile assets，但从 `ResolvedFileToPublish` 删除 Daemon.API、Common、RustyOptions 与 Logging.Abstractions；host integration test 校验 bundle 不携带这些副本。
- host 与 plugin API major 不兼容时直接 skip plugin，不做 best-effort load。

## 6. Application Core

### 6.1 入口形状

`IDaemonApplication` 只做显式 composition facade，不提供 `Services` 或泛型 resolver：

```csharp
public interface IDaemonApplication
{
    IInstanceApplication Instances { get; }
    IFileApplication Files { get; }
    ISystemApplication System { get; }
    IEventRuleApplication EventRules { get; }
}
```

domain interface 可单独注入和测试。新 async 方法统一：

- application method 统一返回 `Task<Result<T, DaemonError>>`；remote/local implementation 不各选一种 awaitable；
- `CancellationToken` 位于最后；
- 正常取消继续抛 `OperationCanceledException`，不包装成 `DaemonError`；
- query 的 `not found` 若调用者需要区分，使用稳定 `DaemonError`；hot-path existence check 使用 snapshot `TryGet`；
- `Unit` 表示成功但无业务 payload。

### 6.2 业务所有权

| 领域 | application owner | 当前实现来源 |
|---|---|---|
| instance create/remove/start/stop/halt/send/settings/report/log | `IInstanceApplication` | `InstanceManager`, `InstanceUpdateCoordinator`, factory internals |
| file info/mutation/upload/download session | `IFileApplication` | `FileManager` and contained session types |
| system info/java list | `ISystemApplication` | current timed cells and Java scanner |
| event rule get/update | `IEventRuleApplication` | current event-rule handlers |

console command、event-rule action 与 V2 handler 必须调用这些 service，不再直接调用 manager/static storage。

文件传输 application boundary 使用显式 session DTO/control methods，而不是公开 stream、socket、file handle 或 `IDisposable` session object：`OpenUploadAsync`、`WriteUploadChunkAsync`、`CloseUploadAsync`、`CancelUploadAsync`、`OpenDownloadAsync`、`ReadDownloadAsync`、`CloseDownloadAsync`。DaemonClient 可以在 transport implementation 内提供高层 upload/download convenience object，但 Daemon.API public surface 只出现 immutable request/result DTO、session id 和 `ImmutableArray<byte>` chunk value。

### 6.3 Halt 的特殊契约

`IInstanceManager.KillInstance(Guid)` 保持已冻结实现：

- 返回 `void`；
- 从 `Instances` 查找；
- 读取 `Process` 一次；
- `process?.KillProcess()`；
- missing instance/process 是 no-op；
- 不增加 `TryKillInstance`，不做 status/exit pre-check，不吞真实 kill exception。

application/V2 可以返回一个 transport/application completion result，但其 `Ok(Unit)` **只表示 void signal 调用正常返回**，不表示 instance 存在、进程存在或已经退出。命名使用 `HaltInstanceAsync`，文档明确 signal semantics。

### 6.4 配置写入权威

- application command 是运行期唯一 instance config 写入口；
- `daemon_instance.json` 外部编辑只在 daemon 下次启动时读取；
- 删除 `FileSystemWatcherPlugin` / `InstancesManagerFsWatcher` 对运行期 instance catalog 的旁路修改；
- application command 完成持久化后才发布新 snapshot；失败不发布半成品；
- daemon 启动加载继续对单个坏配置记录 Error 并 skip，不阻断其他实例。

## 7. 单一错误模型与 Result 不变性

### 7.1 类型模型

```text
DaemonError (closed SDK-owned hierarchy)
├─ validation / not-found / conflict / permission / storage / internal-known errors
└─ PluginError (sealed)
```

`DaemonError` 至少包含稳定 `Code`、安全 `Message`、`Kind` 和可选 cloned `JsonElement Details`。`PluginError` 额外包含 host 写入的 origin plugin id/version 与 plugin-local code。

规则：

- `PluginError` 是 Daemon.API 定义的 `sealed` 类型；插件不得继续派生；
- plugin identity 只能由 plugin-scoped `IPluginErrorFactory` 写入；插件只提供 local code/message/typed details；
- full exception、stack、assembly/path 不进入 wire；unexpected exception 只以 correlation id 对外，完整异常写 Error log；
- V2 error 中 execution owner 来自 frozen RPC descriptor，绝不信任错误对象自报；
- B 调 A 并传播错误时，未来必须保留 `origin=A`、`execution_owner=B`。

### 7.2 不修改 RustyOptions

C# 泛型变体只适用于 interface/delegate；现有 concrete `Result<T,E>` 不能让 `Result<T, PluginError>` 自动变成 `Result<T, DaemonError>`。本项目不 fork RustyOptions，不引入 `IResult<out ...>`，也不增加第五种 result wrapper。

公共 application/plugin/RPC boundary 直接声明：

```csharp
Result<T, DaemonError>
```

实际 error value 可以是 `PluginError`。SDK 提供 `IPluginErrorFactory` extension/helper，直接构造 `Result<T, DaemonError>`，插件作者不需要反复写 `MapErr(e => (DaemonError)e)`。插件私有实现内部若使用 `Result<T, PluginError>`，转换只允许留在它自己的 public-boundary adapter 中。

V2 cutover 完成后删除旧 `Error`、`ActionError`、`ActionRetcode`、`ActionRequestStatus` 的活跃语义与代码。

## 8. Published State 与 Instance Snapshot

### 8.1 关系与命名

- `StatePublisher<T>`：唯一写端，负责串行化 COW update 并原子发布；
- `PublishedState<T>`：某一已发布版本的只读、可长期持有 handle，包含 `Version` 与 `Value`；
- reader 读取的是已完成构造的 immutable version，不是 optimistic lock，也不需要 retry；
- writer lock 不是 reader lock。读路径无锁，写路径使用极短 `System.Threading.Lock`。

建议公共形状：

```csharp
public sealed class StatePublisher<T> where T : class
{
    public PublishedState<T> Current { get; }
    public PublishedState<T> Update(Func<T, T> update);
}

public sealed class PublishedState<T> where T : class
{
    public long Version { get; }
    public T Value { get; }
}
```

实现约束：

- `Current` 使用 `Volatile.Read`，稳态 read 零分配；
- `Update` 在短 `Lock` 临界区内只做纯同步 COW 计算和 publish；
- I/O、await、plugin callback 和日志格式化不得发生在锁内；
- 每次 successful publish 版本严格递增；失败不改变版本；
- old `PublishedState<T>` 永远保持可读且不被原位修改；
- `StatePublisher<T>`/`PublishedState<T>` 不实现 `IDisposable`；
- `T` 不得包含 disposable、resource handle 或可变 collection；
- 不引入 snapshot 第三方 NuGet。

### 8.2 Instance snapshot

```csharp
public interface IInstanceSnapshotSource
{
    PublishedState<InstanceCatalogSnapshot> Current { get; }
    bool TryGet(Guid instanceId, out InstanceSnapshot snapshot);
}
```

`InstanceCatalogSnapshot` 使用 `ImmutableDictionary<Guid, InstanceSnapshot>`。`InstanceSnapshot` 只包含低频变化、深度不可变的 public facts：id、name、instance type、version 与 status；高频 performance counter、log 和 player list 继续通过显式 query/event 获取，避免 COW write amplification。snapshot 不得持有：

- `InstanceConfig`；
- `IInstance` / `InstanceProcess`；
- mutable array/list/dictionary；
- file/session/process/socket/logger/DI handle。

snapshot 在 startup load、create/remove、settings update 与 status change 的 authoritative commit point 更新。`TryGet` 读取 `Current` 一次后查 dictionary，不创建临时 dictionary/array/closure。

### 8.3 DaemonClient remote mirror

为使 local daemon 与 DaemonClient 真正实现同一 snapshot contract，catalog 增加：

- RPC `mcsl.instance.catalog.get`：返回当前完整 version + immutable items；
- event `mcsl.event.instance.catalog.changed`：返回 `version`、`upsert|remove`、instance id 与 upsert snapshot。

DaemonClient 连接时先订阅 changed event 并暂存 delta，再读取 full snapshot，最后按 version 顺序应用暂存 delta。若观察到 version gap、duplicate with different payload 或 reconnect，停止增量应用并重新读取 full snapshot；这属于 snapshot consistency protocol，不是 V1 compatibility fallback。remote mirror 使用自己的 `StatePublisher<InstanceCatalogSnapshot>`，每次只发布完整一致的新版本。

## 9. Frozen RPC/Event Catalog

### 9.1 唯一事实源

```text
typed built-in definitions + successful startup plugin drafts
                         ↓ Freeze()
                 FrozenProtocolCatalog
             ↙             ↓              ↘
      V2 dispatcher   runtime OpenRPC   remote event bridge
             ↓
  checked-in Apifox generator uses the same built-in definitions
```

- built-in definitions 属于 Daemon.API，Daemon、DaemonClient、docs tool 共用；
- plugin definition 在 startup draft 中加入；全部插件处理完成后只 freeze 一次；
- runtime lookup 使用 `FrozenDictionary<RpcMethod,...>` / `FrozenDictionary<EventName,...>`；
- daemon 开始接受连接后禁止注册、替换或删除 definition；
- built-in names 永远优先且保留；plugin 只能注册自己的 `plugin.<normalized-id>.rpc|event.*` namespace；draft 内 duplicate 或越界 name 导致该 owner 失败；
- discovery 先按 normalized plugin id、再按 canonical full path 做 ordinal 排序；同一 normalized id 出现多个 bundle 时所有 candidate 都拒绝，不使用 first-found；
- namespace validation 后若仍出现 cross-owner collision，所有参与 collision 的 plugin draft 都失败，built-in catalog 不受影响；不按 filesystem order 选择 winner；
- 缺失 `JsonTypeInfo`、permission/capability mismatch 同样导致该 plugin transaction 失败；
- checked-in `apifox.json` 只包含 built-ins，但由同一 built-in catalog 生成，禁止手改；
- `rpc.discover` 返回最终 runtime catalog 的 OpenRPC，并以 `x-mcsl-events` 描述 server events。

.NET 10 使用 official `System.Text.Json.Schema` exporter 从注册的 `JsonTypeInfo` 提取 schema；不扫描 plugin assembly，不反射猜 DTO。

### 9.2 命名

- built-in RPC：`mcsl.<domain>.<action>`，例如 `mcsl.instance.start`；
- discovery：`rpc.discover`；
- built-in event：`mcsl.event.<domain>.<name>`；
- plugin RPC：`plugin.<plugin-id>.rpc.<name>`；
- plugin event：`plugin.<plugin-id>.event.<name>`；
- `mcsl.*`、`rpc.*` 保留给 host；plugin id 必须是 lowercase dotted identifier。

wire 使用 string；C# 使用 validated `RpcMethod`、`EventName`、`PermissionName`、`PluginCapability` value type 和常量，不使用 extensible enum。

### 9.3 V1 parity inventory

Phase 0 建立 checked-in inventory，至少冻结以下迁移：

| V1 action/surface | V2 definition / transport | owner |
|---|---|---|
| `Ping` | `mcsl.daemon.ping` | transport |
| `GetSystemInfo` | `mcsl.system.info.get` | `ISystemApplication` |
| `GetPermissions` | `mcsl.auth.permissions.get` | connection/auth adapter |
| `GetJavaList` | `mcsl.java.list` | `ISystemApplication` |
| `SubscribeEvent` / `UnsubscribeEvent` | `mcsl.event.subscribe` / `mcsl.event.unsubscribe` | connection adapter |
| directory/file info | `mcsl.directory.info.get` / `mcsl.file.info.get` | `IFileApplication` |
| upload request/chunk/cancel | `mcsl.file.upload.open/close/cancel` + binary frame | `IFileApplication` |
| download request/range/close | `mcsl.file.download.open/read/close` + binary frame | `IFileApplication` |
| file/directory delete | `mcsl.file.delete` / `mcsl.directory.delete` | `IFileApplication` |
| file/directory rename | `mcsl.file.rename` / `mcsl.directory.rename` | `IFileApplication` |
| directory create | `mcsl.directory.create` | `IFileApplication` |
| file/directory move | `mcsl.file.move` / `mcsl.directory.move` | `IFileApplication` |
| file/directory copy | `mcsl.file.copy` / `mcsl.directory.copy` | `IFileApplication` |
| `AddInstance` / `RemoveInstance` | `mcsl.instance.create` / `mcsl.instance.remove` | `IInstanceApplication` |
| start/stop/kill/send | `mcsl.instance.start/stop/halt/command.send` | `IInstanceApplication` |
| report one/all | `mcsl.instance.report.get/list` | `IInstanceApplication` |
| immutable catalog sync | `mcsl.instance.catalog.get` + `mcsl.event.instance.catalog.changed` | `IInstanceSnapshotSource` |
| log history | `mcsl.instance.log.get` | `IInstanceApplication` |
| settings get/update | `mcsl.instance.settings.get/update` | `IInstanceApplication` |
| event rules get/save | `mcsl.instance.event-rules.get/update` | `IEventRuleApplication` |
| `InstanceLog` event | `mcsl.event.instance.log` | typed event bridge |
| `DaemonReport` event | `mcsl.event.daemon.report` | typed event bridge |
| legacy notification packet | `mcsl.event.notification` | typed event bridge |
| legacy relay packet | deleted; no daemon producer exists | none |

Inventory 对每项记录 params/result type、permission、application method、source `JsonTypeInfo`、DaemonClient wrapper、WPF call sites 和 tests。不得以“名字已映射”代替行为 parity。

## 10. Local Typed Event Bus

### 10.1 公共 ABI

Daemon.API 只暴露项目自有最小接口，例如：

```csharp
IPluginEventPublisher<TEvent>
PluginEventDescriptor<TEvent>
```

MessagePipe 类型不得出现在 public property、parameter、return type、generic constraint、base type 或 plugin Contracts dependency graph。

### 10.2 MessagePipe 选型与边界

采用 MessagePipe `1.8.2`，但只作为 daemon implementation dependency：

- 仅使用 keyless in-memory typed async event slot；
- `EnableAutoRegistration=false`；
- Release 关闭 capture stack trace；
- 不使用 `GlobalMessagePipe`、request mediator、buffered/keyed/distributed/interprocess API；
- 不使用 fire-and-forget `Publish`；所有 publish 必须 awaited；
- 不让 plugin 取得 MessagePipe publisher/subscriber 或 root DI；
- 不增加 ILLink suppression，不 fork MessagePipe。

MessagePipe 1.8.2 的 reflection/DI 路径不能通过当前 `PublishTrimmed=true` warnings-as-errors gate。因此产品路线明确为：

- plugin-enabled daemon 是 **untrimmed JIT**；
- 保留 `EnableTrimAnalyzer`、source-generated STJ、single-file host 与 sidecar plugin；
- 正式取消 Native AOT 和 trimmed publish 产品目标；
- 不维护运行期 fallback bus。

### 10.3 冻结的 dispatch 语义

- 单次 publish 按注册顺序串行 await subscriber；首期无 priority、无并行 fan-out；
- concurrent publishers 可以并发进入同一 subscriber；handler 必须 thread-safe；
- subscriber exception 由 host owner boundary 捕获，记录 component owner/event/exception 的 Error log，然后继续后续 subscriber；
- 与传入 token 对应的 cancellation 继续传播并停止该次 publish；其他 `OperationCanceledException` 按 subscriber failure 记录；
- event 是 transient notification，不持久化、不重试、不提供 acknowledgement，也不承担 veto；
- slow subscriber 记录 warning；remote bridge 不在 subscriber 内等待网络发送；
- subscription token 只由 daemon-internal owner ledger 持有；首期 subscriber 仅限 built-in consumers 与 remote bridge。不向 `IDaemonPlugin` 增加 `IDisposable`。

`event.publish` capability 只管理 plugin event declaration/publication。首期不公开 plugin-facing subscriber API；Phase 7 随 Contracts/DAG 和真实 consumer 一起增加 `event.subscribe` capability 与 `IPluginEventSubscriber<TEvent>`。

## 11. JSON-RPC V2 Profile

### 11.1 Endpoint 与 envelope

- 唯一 endpoint：`/api/v2`；
- cutover Phase exit 时 `/api/v1` 必须不存在；
- `jsonrpc` 必须精确为 `"2.0"`；
- request id 接受 string 或 signed 64-bit integer，response 原样回显；DaemonClient 使用 GUID string；fractional/out-of-range number 与显式 `null` id 拒绝；
- `params` 仅接受 object；无参数 method 允许省略或 `{}`，拒绝 array/scalar/explicit null；
- `Unit` success 序列化为 `{}`，不是 `null`；
- client notification 仅在 descriptor 明确 `AllowNotification=true` 时执行；否则不执行、不回包，只记录 metric/debug；
- 首期 built-in command/query 均要求 id；server event 使用 outbound notification。

### 11.2 Batch

首期不支持 batch。顶层 JSON array 返回一个 `-32600 Invalid Request`、`id:null`，不执行任何元素。不得顺序执行、部分执行或静默取第一项。

### 11.3 Error mapping

| 情况 | JSON-RPC code |
|---|---:|
| parse error | `-32700` |
| invalid request/profile | `-32600` |
| method not found | `-32601` |
| invalid params/schema | `-32602` |
| unexpected internal exception | `-32603` |
| permission denied | `-32001` |
| expected daemon domain error | `-32000` |
| plugin error | `-32005` |

`error.data` 固定包含：

```json
{
  "daemon_error_code": "instance.not_found",
  "daemon_error_kind": "not_found",
  "correlation_id": "...",
  "details": {},
  "origin_plugin": { "id": "...", "version": "..." },
  "execution_owner": { "id": "...", "version": "..." }
}
```

`daemon_error_kind` 与 `correlation_id` 必填。`daemon_error_kind` 是稳定 wire authority，只允许 `validation`、`not_found`、`conflict`、`permission`、`storage`、`transport`、`internal`；client 不得从 `daemon_error_code` 字符串猜测错误类型。`daemon_error_code`、`details`、`origin_plugin`、`execution_owner` 等可选字段缺失时 omit，不输出 CLR type、assembly、path、stack、exception 或 recursive inner-error graph。Phase 5 引入真实 `PluginError` 前，`-32005` 仍使用 `internal`，并保留 origin/execution owner。

### 11.4 Remote events 与 subscription

server event 是 JSON-RPC notification，`method` 等于 catalog event name：

```json
{
  "jsonrpc": "2.0",
  "method": "mcsl.event.instance.log",
  "params": {
    "sequence": 123,
    "timestamp": 1783677000000,
    "meta": { "instance_id": "..." },
    "data": { "log": "..." }
  }
}
```

语义冻结：

- `sequence` 是 daemon event bridge 的全局单调 `long`；
- `timestamp` 是 Unix epoch milliseconds；
- field missing = 该 event 没有这一部分；field present with `null` = 显式 null；object/value = typed payload；
- `mcsl.event.subscribe`/`unsubscribe` 接受 event name 与可选 meta filter；
- filter `meta` 缺失表示 wildcard，显式 null 只匹配显式-null meta；对象必须用 descriptor 的 concrete immutable meta type + `JsonTypeInfo<TMeta>` deserialize，拒绝 unmapped member，再用同一 type info reserialize 为 canonical UTF-8 bytes，按 byte equality 匹配；不得用 `JsonElement` 作为 filterable meta contract；
- unknown event、invalid meta schema、permission failure 返回 `DaemonError`；
- connection close 清除全部 subscription；reconnect 后 DaemonClient 显式重订阅，不依赖 server 隐式恢复；
- legacy `NotificationPacket` 迁为 `mcsl.event.notification`；legacy `RelayPacket` 因无 daemon producer 直接删除。

### 11.5 Backpressure 与 ordering

- 每连接一个 single-reader bounded `Channel<OutboundMessage>`，capacity 固定为 256；RPC response、server event 与 binary frame 全部经此队列，只有 connection-owned writer 可以调用 WebSocket send；
- `OutboundMessage` 是一个不可变的单帧或有序 frame group，`OutboundFrame` 明确 opcode 与 immutable payload；event payload 对所有匹配连接只序列化一次，每连接队列保存引用同一 buffer 的 message value；
- local publisher 只做 non-blocking enqueue，不等待 WebSocket send；
- queue full 时原子标记 connection closing，并由 connection-owned writer 发起 `slow_consumer` close；publisher 不等待 close handshake；
- single-frame send timeout 固定 30 seconds，并以 injectable `TimeProvider`/fake sender 测试；timeout 以 `slow_consumer` 关闭并清理 subscription，不静默 drop/coalesce；
- 同一连接按 enqueue 顺序发送；不同连接互不阻塞；
- response + related binary frame 必须作为一个 two-frame `OutboundMessage` enqueue，由 single writer 连续发送，禁止其他 producer 插入两者之间；
- queue writer/connection disposal 的 race 必须有并发测试，不能依赖 catch-all 忽略。

### 11.6 Binary transfer session

大块文件内容不进入 JSON/base64。

- upload control：`mcsl.file.upload.open`、`close`、`cancel`；
- download control：`mcsl.file.download.open`、`read`、`close`；
- upload chunk 与 download chunk 使用同一 WebSocket binary frame；
- binary header 固定 32 bytes：byte 0 `version=1`；byte 1 `kind` (`1=upload_chunk`, `2=download_chunk`)；bytes 2-3 reserved zero；bytes 4-19 session UUID 的 RFC 4122 network byte order；bytes 20-27 non-negative Int64 offset little-endian；bytes 28-31 UInt32 payload length little-endian；禁止依赖 runtime struct layout；
- payload length 必须等于 frame 剩余长度且不超过 open response 给出的 `max_chunk_size`；首期 max 为 1 MiB；reserved/noncurrent version/unknown kind 立即终止 session；
- upload offset 必须严格连续；duplicate/gap/out-of-order frame 终止 session；
- open 声明 size 与 SHA-256，close 校验实际 size/hash 后才 commit；
- download client 校验 size/hash；
- session 绑定 connection 与 permission，不允许跨连接复用；
- session open 返回 `expires_at`；首期 absolute lifetime 固定 30 minutes，使用 injectable `TimeProvider` 测试；connection close/daemon stop 取消全部 session；
- upload binary frame 通过 connection-owned JSON-RPC control notification `mcsl.file.upload.ack` 返回 typed ack/error；它绑定 session/connection、绕过 public event subscription/filter、由 DaemonClient transport 自动处理；同一 session 在 ack 前只允许一个 in-flight chunk；
- `mcsl.file.download.read` 先在 connection send queue 写 JSON-RPC result，再紧接一个带相同 session id/offset 的 binary frame；同一 session 只允许一个 in-flight read；
- server 不再发送 heuristic text envelope；
- parser 使用 span/`BinaryPrimitives`，不得为 header/checksum创建临时 array。

## 12. DaemonClient 与 WPF Cutover

### 12.1 DaemonClient

- internal transport 只解析 JSON-RPC response、outbound server notification 与 versioned binary frame；
- pending request 以 JSON-RPC id 关联，connection close/cancellation 精确完成 pending task；
- 使用 frozen definitions 自带的 `JsonTypeInfo`，不做 `Deserialize<object>` 或 reflection fallback；
- 实现 `IDaemonApplication` remote proxy 和 typed event subscription；
- reconnect 后只恢复调用者仍持有的 typed subscriptions；不恢复 closed file session；
- initial `ConnectAsync` 只有在 auth、required built-in subscriptions 与 §8.3 full snapshot/delta reconciliation 完成后才标记 connected/暴露 ready application；reconnect 同样先 resync 再恢复 connected state；
- disconnected 时 `Current` 可保留最后一个 immutable snapshot 供 UI 展示，但 connection state 必须独立可见，command 返回 typed transport error，禁止把 stale snapshot 冒充 live state；
- 删除 `RequestAsync(ActionType, ...)`、`EventType` switch、root-property heuristic packet detection、legacy notification/relay callbacks 和 V1 serializer caches；
- public breaking change 直接发布新 major，不保留 `[Obsolete]` wrapper。
- 现有 `RestartInstanceAsync` 不是 V1 action，也不新增 daemon/application method；它保留为 daemon-client convenience composition：typed stop -> 现有一秒 delay -> typed start，并以可注入 `TimeProvider` 做 characterization/test。若未来需要 daemon-authoritative restart，另行设计，不在本 cutover 偷增能力。

### 12.2 WPF

- WPF view model/service 只依赖 `IDaemonApplication`/DaemonClient typed event API；
- instance console 的 log subscription 改为 `mcsl.event.instance.log` typed subscription；
- notification flow 改为 typed notification event；
- create/update/file workflows 使用 application contracts；
- 不在 WPF 内构造 method string、JSON envelope 或 permission string；
- build 继续使用 `/m:1`，user-facing error 通过现有 i18n resource 映射 `DaemonError.Code`。

Phase exit 必须证明 repo 中除 migration fixture/changelog 外不存在 `ActionType`、`ActionResponse`、`ActionError`、`EventType` V1 runtime reference。

## 13. Startup-only Plugin Host

### 13.1 Manifest 与 bundle

manifest 仅支持 JSON：

```json
{
  "id": "community.instance-health",
  "version": "1.0.0",
  "entry_assembly": "Community.InstanceHealth.dll",
  "entry_type": "Community.InstanceHealth.InstanceHealthPlugin",
  "api_version": "[1.0.0,2.0.0)",
  "capabilities": [
    "rpc.register",
    "event.publish",
    "instance.query"
  ]
}
```

bundle layout：

```text
plugins/<plugin-id>/
  plugin.json
  <entry>.dll
  <entry>.deps.json
  <private dependencies>
```

一个 plugin id 只允许一个目录/版本。非法 id、重复 id、版本不兼容、manifest/entry 缺失都 Error log + skip；不猜 entry type，不读 assembly attribute fallback。

plugin 项目设置 `MCSLPluginBundle=true`，由 Daemon.API 的 `buildTransitive` target 在 publish 输出中剔除 shared contracts；普通 `dotnet publish` 的未过滤输出不被视为合法 bundle。sample、fixture 与开发文档统一使用该规则，并以 bundle file-list test 断言 Daemon.API/Common/RustyOptions/Logging.Abstractions 不存在。

### 13.2 ALC 与 type identity

- 每插件一个 non-collectible custom `AssemblyLoadContext` + `AssemblyDependencyResolver`；
- Daemon.API、Common、RustyOptions、`Microsoft.Extensions.Logging.Abstractions` 只从 default/shared ALC 返回同一 Assembly instance；
- plugin bundle 携带这些 shared contract 的私有副本时直接拒绝，不做 version roulette；
- MessagePipe 只在 host/default side，plugin bundle 不引用或携带；
- plugin 不得引用 daemon executable、TouchSocket、Serilog；引用扫描是加载正确性与风险审计，不宣称安全沙箱；
- 首期不设置 collectible/unload probe，不为未来 unload 引入代理。

### 13.3 Capabilities

首期只有：

```text
rpc.register
event.publish
instance.query
```

capability 表示正式支持的 API surface 与审计，不是进程安全边界。plugin context 提供三个显式 capability surface、scoped logger 与 `IPluginErrorFactory`；不给 root DI 或 generic arbitrary-service resolver。

RPC/event registration 在 commit 前校验声明的 capability。`instance.query` 只提供 `IInstanceSnapshotSource`，不提供 manager/control/config mutation。

### 13.4 Lifecycle 与 transaction

```csharp
Result<Unit, DaemonError> Configure(IPluginContext context);
Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken);
Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken);
```

- `Configure` 只声明 RPC/event 并构造 registration draft；禁止 I/O 和 background work；
- 每项 registration 返回 typed Result，整个 plugin draft 最终全量校验；
- `StartAsync` 只在 draft 校验成功后调用；daemon 尚未接受 client connection；
- Configure/Start 返回 Err 时，Error log 使用 plugin-provided message/details，并 discard 全部 draft/event slots；
- Configure/Start 抛 unexpected exception 时，Error log 包含完整 exception + plugin id/version/stage，然后 skip；
- 一个 plugin 失败不得阻断其他 plugin 或 daemon startup；
- owner state machine 固定为 `Discovered -> Configured -> Validated -> Started -> Committed -> Active -> Stopping -> Stopped`，任一前置阶段失败进入 `Failed`；
- Configure all -> global validate all drafts -> Start validated plugins -> aggregate successful drafts -> freeze catalog -> commit/activate successful owners -> open `/api/v2`；
- `IPluginContext.Activation` 是 host-completed barrier，`LifetimeToken` 是 plugin-scoped cancellation；Start 创建的 background task 必须先 await Activation，Start 本身不得 await Activation；
- plugin event publisher 在 Active 前调用立即以 programmer error 失败，不 buffer、不 drop、不向 daemon-internal subscriber 投递；
- Start Err/exception 时 host 取消 LifetimeToken 并清理 host-owned draft/event slots；plugin 必须在返回 Err 前清理 private partial resources，忽略 cancellation 的 trusted plugin 无法由 host 强制回收；
- 只有 Start 成功的 plugin 才进入 committed catalog 与 started list；catalog freeze 前已完成全局冲突/metadata validation，freeze failure 视为 host invariant failure，不静默降级；
- 全部 successful plugin active 后再开放 `/api/v2`；
- daemon shutdown 对 successful plugins 按启动逆序调用 Stop；Stop Err/exception 记录 Error 后继续；
- shutdown 对每个 plugin 先取消其 LifetimeToken，再调用 Stop 等待 private background task 收敛；Stop 使用独立的 bounded shutdown token；
- `IDaemonPlugin` 不实现 `IDisposable`/`IAsyncDisposable`；plugin 私有资源由 Stop 负责，host-owned subscriptions 由 owner ledger 释放；
- plugin 加载失败后不得残留 RPC、event slot、host-owned CTS 或 catalog metadata。

## 14. 首期验收插件

新增一个 external-style instance health plugin fixture/sample，禁止使用 `InternalsVisibleTo` 或引用 daemon executable。

它必须：

1. 声明三个首期 capability；
2. 通过 `IInstanceSnapshotSource.Current`/`TryGet` 读取 immutable snapshot；
3. 注册 typed RPC `plugin.community.instance-health.rpc.get`；
4. 注册并发布 typed event `plugin.community.instance-health.event.changed`；
5. 为 RPC params/result/event meta/data 显式提供 source-generated `JsonTypeInfo`；
6. 用 `PeriodicTimer` + cancellation 运行；background task 先 await `IPluginContext.Activation`，Stop 不依赖 IDisposable；
7. 使用 `IPluginErrorFactory` 产生 `PluginError` value，但 public Result 仍为 `Result<T, DaemonError>`；
8. 可通过 DaemonClient discover、调用 RPC、订阅 event；
9. 构造一个返回 Err 的 fixture 和一个抛异常的 fixture，证明 Error log + skip 且 daemon 正常启动；
10. package dependency graph 中不存在 daemon executable、TouchSocket、Serilog 或 MessagePipe。

## 15. 下一里程碑：Plugin Contracts 与 Dependency DAG

该里程碑在首期验收后实施，不在首期 API 中预埋空接口。

### 15.1 Direct service contract

```text
PluginA.Contracts.dll / NuGet
    IAService + immutable DTO + event contracts + error code constants
             ↑ compile reference only
Plugin B ---------------------------> import IAService

PluginA.dll
    AService : IAService
```

- B 只引用 A.Contracts，不引用 A implementation；
- runtime 是普通 CLR interface dispatch，不经过 JSON-RPC、WebSocket 或 serialization；
- RPC 只给 external client；MessagePipe 只处理一对多 notification；direct contract 处理有明确 provider 的 request/response；
- 只有真正导出服务/event contract 的 plugin 才拆 Contracts assembly。
- Phase 7 同时增加 `event.subscribe` capability 与 project-owned `IPluginEventSubscriber<TEvent>`；MessagePipe 仍不进入 Contracts ABI。

### 15.2 DAG 与 shared contract identity

- manifest 增加 dependency id + SemVer range；
- resolve missing/version conflict/cycle，生成 deterministic DAG；
- provider 先 start，dependent 后 start；shutdown 逆拓扑；
- authoritative `PluginA.Contracts` 只加载一次到 shared/default ALC；A/B private ALC 请求时返回同一 Assembly instance；
- host 检测到 consumer bundle 含 runtime duplicate Contracts assembly 时直接拒绝 B；开发/打包指南要求 project/NuGet reference 使用 `ExcludeAssets=runtime`，不在 loader 中保留第二策略；
- service registry 以 provider id + shared contract `Type` 为 key；
- public contract 继续返回 `Result<T, DaemonError>`；
- 首期仍不承诺 independent hot unload；direct reference 会保持 provider ALC 存活，这是已知且接受的后果。

## 16. 实施阶段

### Phase 0: Governance、characterization 与 deletion inventory

**Files:** `PROJECT_PLAN.md`, `RULES.md`, `EXECUTE_PLAN.md`, `AGENTS.md`, `CLAUDE.md`, `skills/mcsl-future/SKILL.md`, current protocol tests/benchmarks, this plan.

- [x] 更新项目架构：新增 Daemon.API/application core/plugin host，协议改为 V2 JSON-RPC over WebSocket。
- [x] 对齐文档中的实际 TFM：Common 与 Daemon.API 均为 `net10.0`；不为旧文档描述把 Common 降级到 `netstandard2.1`。
- [x] 正式删除 Native AOT/trimmed publish 产品目标；保留 trim analyzer、source-gen JSON、single-file + sidecar plugin。
- [x] 把 protocol docs 规则改为“typed catalog source -> generated Apifox + runtime OpenRPC”。
- [x] 建立 V1 parity inventory，覆盖 §9.3 的所有 action/event/binary/permission/cancellation/null semantics：`2026-06-27-daemon-api-v1-parity-inventory.md`。
- [x] 在改实现前补缺失 characterization tests；migration-only tests 明确排除旧 binary/string、subscription mutation、tracker split 和 reconnect cleanup bug，不把它们升级为 V2 契约。
- [x] 记录 V1 parser/dispatch/client/event/binary benchmark baseline：`benchmarks/baselines/v1.json`，含环境指纹、382-test 基线与 legacy raw-upload frame/ack ShortRun 指标。
- [x] 建立 exact deletion manifest：V1 Common types、daemon action/event runtime、DaemonClient V1 API、WPF call sites、custom action generator、tests/benchmarks/docs。
- [x] 检查 dirty worktree；基线提交 `925666a4` 已包含并冻结 instance lifecycle changes，后续迁移其行为，不覆盖或回退。其余未跟踪/已修改文件按用户状态保留。
- [x] 冻结 daemon release RID 为当前七项 workflow matrix；plugin E2E 的本地强制验收 RID 为 `win-x64`，CI/release 对全部 RID 做 publish 与 bundle-layout gate。

**Exit:** governance 与冻结决策一致；V1 全行为有 owner/mapping/test；没有待实现者猜测的 open question。

### Phase 1: Daemon.API、DaemonError 与 published state

**Create:**

- `src/MCServerLauncher.Daemon.API/MCServerLauncher.Daemon.API.csproj`
- `src/MCServerLauncher.Daemon.API/Application/`
- `src/MCServerLauncher.Daemon.API/Errors/`
- `src/MCServerLauncher.Daemon.API/State/`
- `src/MCServerLauncher.Daemon.API/Protocol/`
- Common neutral application/RPC/event/binary DTO folders.

- [x] 将新 project 加入 solution Libraries，并配置 pack/package validation/API baseline。
- [x] 定义 `DaemonError` closed application hierarchy；plugin subtype/factory 在 Phase 5 随真实 plugin contract 一起加入。
- [x] 定义 `IDaemonApplication` 四个 domain services；签名统一 Result/ct/cancellation 规则。
- [x] 定义 typed `RpcMethod`/`EventName`/`PermissionName`/`PluginCapability` 与 descriptors。
- [x] 实现 `StatePublisher<T>`/`PublishedState<T>`；加入 monotonic version、historical stability、race、zero-allocation tests。
- [x] 定义 deep immutable `InstanceSnapshot`/`InstanceCatalogSnapshot`/`IInstanceSnapshotSource`。
- [x] 移除 Common 的 Serilog package/transitive dependency：将 `PlaceHolderString`、`InstanceVersionDetector`、`SlpClient`、Common `TaskExtensions` 的日志移到 caller boundary 或改为显式 result/callback；Common 不换成另一套 static logger。
- [x] 建立 public surface leakage tests，禁止 daemon internals/TouchSocket/MessagePipe/Serilog/mutable collections/disposable handles。

**Exit:** Daemon.API 可 pack；API tests 通过；Daemon.API 不引用 daemon；snapshot read benchmark 为 0 B/op。

### Phase 2: Local application core 与 authoritative instance state

**Create/update:** `src/MCServerLauncher.Daemon/Application/`, `Management/InstanceManager.cs`, `Management/InstanceUpdateCoordinator.cs`, storage/file session code, console commands, event rule service, bootstrap composition.

- [x] 实现四个 application services；manager/static storage 只留在 implementation side。
- [x] 把所有现有 action behavior 移到/委托给 application services 或明确的 daemon-internal migration adapter，保留 Phase 0 characterization semantics；V1 handler 不再直接调用 manager/static storage。
- [x] 保留 `KillInstance(Guid)` void signal，实现 application halt completion 语义测试。
- [x] 用 `StatePublisher<InstanceCatalogSnapshot>` 发布 startup/create/remove/update/status commits；performance/log/player 不能触发 catalog COW。
- [x] 建立唯一 daemon-internal `FileSessionCoordinator`；`IFileApplication` 与 V1 file/raw-binary adapter 都委托它。Phase 2 负责 path validation、session state、storage I/O、atomic upload commit、offset/size/hash、expiry/daemon-stop cleanup 与每 session 串行化；connection/permission ownership、V2 binary header/control ack/outbound ordering 留到 Phase 4。
- [x] 删除运行期 instance config 旁路：`FileSystemWatcherPlugin`、`InstancesManagerFsWatcher`、bootstrap registration、`InstanceBase` lazy config reload，以及 report-time `ReconcileLoadedInstance`；外部编辑仅在下次 daemon startup 生效。
- [x] console `inst` 与 `EventTriggerService` 改调 application services 和 daemon-internal typed domain-event port，不直接调 manager/`WsContextContainer`；删除 manager 到 `Application.HttpService` 的 service-locator 回调。
- [x] notification/log/status producer 只发布 immutable domain event；保留 V1 remote adapter 将其映射到当前 V1 packet/fan-out。Phase 3 才绑定 MessagePipe/frozen event descriptors 与 `mcsl.event.*` 名称；Phase 4 才删除 V1 adapter、`NotificationPacket`/`RelayPacket` wire contract/parser/callback。Phase 2 只确认 relay 没有 application owner/producer。
- [x] 将 `IInstanceManager` 和实现细节收紧为 daemon-internal；公共 assembly 不可见。
- [x] 加入 local application integration tests、persistence failure atomicity tests、snapshot concurrency tests。

**Exit:** daemon 内所有非-implementation 入口只认 application core/domain-event port；V1 adapter 只做 wire translation，不再拥有业务或 session state；mutable dictionaries 未泄漏；external config edit 不改变运行期 snapshot；Phase 3/4 的 catalog、MessagePipe、V2 framing 与 V1 删除尚未提前实现。Independent Sol review has 0 open P0/P1 findings. Acceptance passed the focused fixed-tree suite at 73/73 twice, Daemon.API tests at 42/42, Release protocol tests at 442/442, the Release `--no-build` protocol gate at 442/442, Daemon.API/daemon/full-solution Release builds at 0 warnings / 0 errors, and `git diff --check`, with no update/upload staging residue. The generated-docs check is not applicable at Phase 2 because `tools/MCServerLauncher.ProtocolDocs` is a Phase 3 create target and is absent from both HEAD and the current tree.

### Phase 3: Frozen catalog、MessagePipe 与 generated docs

**Create/update:** Daemon.API protocol definitions, daemon catalog binding/local event implementation, `tools/MCServerLauncher.ProtocolDocs/`, embedded docs, benchmarks.

- [x] 定义完整 built-in RPC/event catalog 与 explicit `JsonTypeInfo`；绑定到 application services。
- [x] 实现 startup builder -> immutable/frozen catalog；freeze 后 mutation 失败。
- [x] 添加 MessagePipe 1.8.2 仅到 daemon project，按 §10 配置和包装。
- [x] 实现 sequential awaited dispatch、per-owner exception boundary 与 subscription ledger。
- [x] 用 .NET JSON schema exporter 从同一 type info 生成 OpenRPC schema。
- [x] docs tool 从 built-in catalog 生成符合 RULES shape 的 `apifox.json`；加 deterministic `--check` test。
- [x] runtime `rpc.discover` 从 final frozen catalog 生成 OpenRPC，包含 plugin/runtime event extensions。
- [x] benchmark frozen lookup、built-in event 和 MessagePipe wrapper 的 1/8/32 subscribers、exception path、serialization once fan-out。

**Exit:** catalog is the sole source for V2 dispatch-ready bindings/metadata, runtime OpenRPC, and checked-in Apifox; live V1 dispatch remains until the Phase 4 deletion gate. Built-in definitions cover 38 RPCs and 4 events with explicit `JsonTypeInfo` and application bindings; no reflection DTO scan exists; MessagePipe remains daemon-internal and outside the Daemon.API/package graph. Ordered catalog commits use admission/drain ownership without awaiting under mutation locks. The catalog-change schema conditionally requires a snapshot only for upserts. Acceptance passed Daemon.ApiTests 56/56, Release ProtocolTests 548/548 and final Release `--no-build` 548/548; the recorded acceptance invocations of Daemon.API, daemon, daemon client, and full-solution Release builds each emitted 0 warnings / 0 errors, not a repository-wide warning-free claim. ProtocolDocs `--check` matched `51e351a9...`; benchmark project build covered seven new methods. The implementation-time ShortRun is evidence only, not a final performance gate. `git diff --check` passed. Independent Sol max final review found 0 open P0/P1/P2. The pre-existing ProtocolTests source `CS0105` duplicate-using warning is visible on rebuild, was not introduced by Phase 3, and remains separately tracked.

### Phase 4: V2 transport + DaemonClient/WPF one-shot cutover + V1 deletion

这是一个 release-atomic milestone。可以在 branch 内分提交构建 V2，但 Phase exit/product build 不允许保留双协议或 fallback。

**Create/update:**

- `src/MCServerLauncher.Common/ProtoType/Rpc/`
- `src/MCServerLauncher.Daemon/Remote/Rpc/`
- daemon bootstrap endpoint/composition/auth/session code
- `src/MCServerLauncher.DaemonClient/Connection/`, `Serialization/`, `Api/`
- all WPF daemon call sites
- protocol docs/tests/benchmarks.

- [x] 实现 §11 JSON-RPC parser/dispatcher/profile/error mapping，byte-oriented hot path + source-gen STJ。
- [x] 实现 typed remote event bridge、subscription state、256 bounded queue、slow-consumer disconnect 和 serialization-once fan-out。
- [ ] 实现 §8.3 catalog full snapshot/delta mirror；覆盖 subscribe-before-read race、version gap、duplicate conflict 与 reconnect resync。
- [x] 实现 versioned binary header、session ownership/offset/hash/expiry/cleanup。
- [ ] DaemonClient 实现 remote `IDaemonApplication`、typed event subscription、binary sessions、reconnect semantics。
- [ ] WPF 全量切 typed application/event API；error code 映射 i18n。
- [ ] endpoint 切到 `/api/v2`，移除 `/api/v1`。
- [ ] 删除 `src/MCServerLauncher.Daemon/Remote/Action/`、旧 `WsActionPlugin`/`WsEventPlugin`/`IEventService` 路径及 V1-only contexts。
- [ ] 删除 Common V1 `ActionType`/packet/retcode/status/interfaces 与旧 EventType/packet；保留并重命名仍有真实 domain ownership 的 DTO。
- [ ] 删除 DaemonClient `RequestAsync(ActionType)`、V1 parser/events/callbacks/caches 和 compatibility helpers。
- [ ] 删除/替换 WPF V1 call sites。
- [ ] 删除 V1-only `MCServerLauncher.Daemon.Generators` project、registry tests、startup benchmarks 与 generated/legacy runtime switch。
- [ ] rewrite protocol tests/benchmarks to V2；migration fixture 只能留作 test input，不能参与 runtime。
- [ ] 新增 `tools/VerifyNoV1Runtime.ps1`：明确 allowlist migration fixtures/changelog，并以 runtime match 非空作为失败；不把裸 `rg` exit code 当 gate。

Phase 4 内部按以下顺序验收，但三个子出口共同构成一个 release-atomic milestone，任何中间状态都不是可发布产品：

1. **4A daemon V2（complete）:** protocol/dispatcher/event queue/binary session 在 transport harness 中通过，不开放兼容 endpoint。
2. **4B client cutover:** DaemonClient remote application implementation 与 WPF typed call sites 全部通过；DaemonClient 使用 `StatePublisher<InstanceCatalogSnapshot>` 维护 remote mirror。
3. **4C deletion proof:** 切换唯一 endpoint，删除 V1/Common/client/generator 路径并通过 inventory search、full tests 与 build。

**4A sub-exit:** Phase 4A daemon V2 completed across the 11 commits `b394957a^..98f07298`. Final acceptance passed Daemon.ApiTests 56/56, the real TouchSocket host test 1/1, and Release ProtocolTests plus the final Release `--no-build` gate at 782/782. The recorded daemon, full-solution, and benchmark Release builds each emitted 0 warnings / 0 errors; ProtocolDocs `--check` matched catalog hash `74268afdb2bb3dd9be54cc1edf92017f581d305270f6be201f2c033eb2f4b44f`; `git diff --check` passed. Independent Sol max final review reported 0 open P0/P1/P2. The pre-existing ProtocolTests source `CS0105` duplicate-using warning remains separately tracked and is not a Phase 4A regression. Paired V2/V1 performance validation remains a Phase 6 residual. `/api/v2` is production-composed through TouchSocket, while `/api/v1` intentionally remains a branch-internal migration path until 4C; 4B and 4C are pending, so this sub-exit is not a releasable Phase 4 or product completion.

**Exit:** daemon、DaemonClient、WPF 只运行 V2；full solution/build/tests 通过；V1 code deletion manifest 清零。

### Phase 5: Startup plugin host 与 health plugin

**Create/update:** `src/MCServerLauncher.Daemon.API/Plugins/`, `src/MCServerLauncher.Daemon/Plugins/`, daemon bootstrap/lifecycle, sample/fixture plugin projects, `tests/MCServerLauncher.PluginIntegrationTests/`.

- [ ] 实现 JSON manifest/bundle validation、API version range、capability parsing与 duplicate id checks。
- [ ] 在 Daemon.API 中定义 plugin lifecycle/context/registration/event interfaces；不增加 service locator、hooks、factory/store/control capability。
- [ ] 加入 sealed `PluginError`、scoped `IPluginErrorFactory` 与直接返回 `Result<T, DaemonError>` 的 no-map helper。
- [ ] 建立 external compile fixture，证明只引用 Daemon.API/Common 即可编译 plugin。
- [ ] compile fixture 明确证明 public signatures 不出现 `Result<T, PluginError>`/`IResult`，helper 无显式 `MapErr` 即可返回 public Result。
- [ ] 实现 per-plugin ALC/resolver/shared-contract allowlist 与 forbidden duplicate/reference checks。
- [ ] 实现 registration draft、Configure/Start transaction、final catalog freeze、reverse Stop。
- [ ] 按 §9.1 实现 deterministic discovery/conflict policy；测试 duplicate id 全拒绝、own-namespace enforcement、built-in priority 与 residual collision all-fail。
- [ ] 实现 Activation/Lifetime owner state machine；测试 Start 期间 publish 立即失败、background activation 后才 publish、failed Start 无 event/catalog residue。
- [ ] 对 returned Err 与 unexpected exception 分别 Error log + skip；一个坏 plugin 不阻断 daemon/其他 plugin。
- [ ] 实现 owner cleanup，并验证失败无 RPC/event slot/CTS/catalog residue。
- [ ] 实现 §14 health plugin、error fixture、throw fixture。
- [ ] PluginIntegrationTests 从 `MCSL_PUBLISHED_DAEMON` 启动已发布 single-file host，加载 sidecars，验证 discover/RPC/event、bad-plugin Error log + skip、reverse shutdown。

**Exit:** acceptance plugin 完整证明三个 capability；失败隔离和 transaction tests 通过；daemon 正常启动。

### Phase 6: Packaging、performance 与 public documentation

- [ ] pack Daemon.API；运行 package validation 与 dependency graph checks。
- [ ] 更新 third-party notices/license inventory，记录 MessagePipe 1.8.2 与 daemon-internal NuGet.Versioning。
- [ ] 更新 README/README_ZH、daemon manual、plugin developer guide、sample build/publish instructions。
- [ ] `dotnet publish` untrimmed single-file supported RIDs；不运行/承诺 Native AOT 或 `PublishTrimmed=true`。
- [ ] 清理不在冻结 matrix 的 legacy `win-arm`/`linux-arm` publish profiles；release workflow 对七个 daemon RID 显式传 `PublishTrimmed=false`，Windows 三个 RID 同时打 WPF。
- [ ] BenchmarkDotNet 输出 JSON；新增 gate harness 对比 Phase 0 baseline：allocation 始终比较；mean 只在 SDK/runtime/OS/architecture/CPU 指纹一致或显式同机 A/B 时比较。等价 request dispatch mean/allocated bytes 不得回退超过 25%，环境不匹配时 gate 必须要求重新捕获 paired baseline，而不是比较无效绝对时间。
- [ ] snapshot Current/TryGet steady state 0 B/op；event payload 每次 publish 只序列化一次。
- [ ] 记录 final public API baseline、protocol catalog hash、Apifox generation check。
- [ ] 更新本 plan Changelog 与 phase status。

**Exit:** release artifacts、docs、NuGet、benchmarks 与 implementation 一致，无 known error/fallback/dead V1 code。

### Phase 7: Mandatory follow-up dependency/contracts milestone

- [ ] 新建独立 plan，按 §15 实现 manifest DAG、SemVer ranges、deterministic start/reverse stop。
- [ ] 实现 authoritative shared Contracts assembly identity 与 duplicate rejection tests。
- [ ] 实现 direct typed service export/import；禁止 RPC 作为 plugin-to-plugin 主路径。
- [ ] 发布一个 A provider + B consumer fixture，证明普通 interface call、error origin propagation、无 MessagePipe/RPC dependency。

## 17. Test Matrix

| Area | Required evidence |
|---|---|
| API boundary | project dependency graph, public surface reflection, external plugin compile, package validation |
| Error model | all application errors typed, plugin origin/owner, no exception leakage, cancellation passthrough |
| Published state | zero-allocation read, monotonic version, retained history, 32+ concurrent readers/writers, no torn state |
| Application core | all V1 behavior inventory mapped once, halt signal, persistence atomicity, console/event-rule delegation |
| Catalog/docs | conflict/capability/type-info validation, freeze immutability, deterministic OpenRPC/Apifox |
| JSON-RPC | spec errors, id echo, object params, notification policy, batch rejection, permission, cancellation |
| Remote event | missing/null/object meta, wildcard/exact filter, canonical property order/numeric normalization, unknown-field rejection, ordering, one serialization, reconnect, slow consumer |
| Binary | header endianness, malformed frames, offset gap/duplicate, SHA-256/size, ownership, expiry, disconnect cleanup, ack without public subscription, response+binary non-interleaving |
| DaemonClient | typed round trips for every catalog method, pending request cleanup, reconnect subscription behavior |
| WPF | typed API use, localized error mapping, `/m:1` build, instance console log smoke test |
| Plugin host | manifest/API/ALC/capability failures, returned Err, thrown exception, transaction rollback, reverse Stop |
| Health plugin | published host loads sidecar, discover/RPC/event/snapshot/JsonTypeInfo end to end |
| Performance | V2 vs V1 captured baseline, state read, catalog lookup, MessagePipe 1/8/32, remote fan-out |

补充测试纪律：

- catalog coverage test 枚举每个 built-in definition，要求 params/result `JsonTypeInfo`、permission、daemon binding、DaemonClient typed mapping 和 golden case 全部存在；禁止维护独立手写数量常量；
- remote snapshot tests 覆盖 subscribe/read interleaving、delta gap/refetch、duplicate conflict、reconnect 与 retained historical state；
- concurrency tests 使用 `Barrier`/可控 fake sender、bounded iterations、明确 timeout 与 invariant collection；不得以 `Task.Delay` 猜 race 是否发生；
- outbound writer tests 并发注入普通 event 与 `download.read`，断言 two-frame message 连续发送；upload 未订阅任何 public event 时仍收到 control ack；
- WPF 至少有 headless view-model/service test：fake `IDaemonApplication` + typed log/notification stream，验证 append、unsubscribe 与 localized error mapping；
- API leakage 同时检查 exported reflection surface、NuGet assets/transitive graph 与 packed artifact，不以 source grep 代替。

## 18. Deletion Checklist

Phase 4 不完成以下删除就不算 cutover：

- [ ] `/api/v1` registration and docs
- [ ] Common `ActionType`, V1 action request/response/status/retcode interfaces and packets
- [ ] Common V1 `EventType`/`EventPacket`/legacy meta/data marker path
- [ ] legacy `RelayPacket`
- [ ] daemon `Remote/Action` handlers/executor/registry/error/response utilities
- [ ] old `WsActionPlugin` and `WsEventPlugin`
- [ ] old `IEventService` fan-out path
- [ ] generated-vs-legacy action registry runtime switch
- [ ] V1-only custom action source generator project and release tracking
- [ ] DaemonClient `RequestAsync(ActionType)` and V1 inbound heuristic parser
- [ ] DaemonClient legacy notification/relay/event callbacks and serializer caches
- [ ] WPF V1 action/event references
- [ ] V1-only tests, fixtures used as runtime contract, benchmarks and embedded docs
- [ ] old `Error`/`ActionError`/`ActionRetcode` after all application migrations
- [ ] runtime instance filesystem watcher side-channel
- [ ] compatibility flags, fallback branches, obsolete wrappers and dual endpoint tests

## 19. Acceptance Criteria

1. daemon behavior has one application implementation and all entrypoints delegate to it.
2. Daemon.API is a packable net10.0 contract package with no daemon/transport/event-library leakage.
3. public failures use `Result<T, DaemonError>`; plugin errors carry host-authenticated identity without Result covariance wrapper.
4. instance reads use deep immutable COW snapshots; Current/TryGet steady-state zero allocation and lock-free.
5. `/api/v2` is the only protocol endpoint; V1 runtime/client/generator code is deleted.
6. full built-in action/event/file behavior is represented in the frozen catalog and tested through V2.
7. runtime OpenRPC and checked-in Apifox originate from the same typed definitions.
8. MessagePipe is daemon-internal only; event ordering/error/cancellation/ownership semantics match §10.
9. slow remote consumers cannot block local publisher or other connections and are explicitly disconnected.
10. plugin load failure logs Error and skips atomically without preventing daemon startup.
11. health plugin proves typed RPC, snapshot query, typed event and explicit `JsonTypeInfo` from published output.
12. plugin-enabled daemon is untrimmed JIT single-file + sidecar; no Native AOT/trimmed product claim remains.
13. no extra fallback、compatibility wrapper、unused hook/factory/store/TouchSocket API is introduced.
14. Phase 7 dependency/contracts work remains explicitly tracked as the required next milestone.

## 20. Verification Commands

```powershell
dotnet build src/MCServerLauncher.Daemon.API/MCServerLauncher.Daemon.API.csproj /m:1
dotnet build src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj /m:1
dotnet build src/MCServerLauncher.DaemonClient/MCServerLauncher.DaemonClient.csproj /m:1
dotnet build src/MCServerLauncher.WPF/MCServerLauncher.WPF.csproj /m:1

dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj /m:1
dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release /m:1
dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release --no-build /m:1

dotnet run --project tools/MCServerLauncher.ProtocolDocs/MCServerLauncher.ProtocolDocs.csproj -- --check
dotnet run --project benchmarks/MCServerLauncher.Benchmarks/MCServerLauncher.Benchmarks.csproj -c Release -- --exporters json
dotnet run --project tools/MCServerLauncher.PerformanceGate/MCServerLauncher.PerformanceGate.csproj -- --baseline benchmarks/baselines/v1.json --results BenchmarkDotNet.Artifacts/results

dotnet publish src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj -c Release -r win-x64 --self-contained -o artifacts/plugin-e2e/daemon
dotnet publish samples/MCServerLauncher.Plugins.InstanceHealth/MCServerLauncher.Plugins.InstanceHealth.csproj -c Release -o artifacts/plugin-e2e/daemon/plugins/community.instance-health
dotnet publish tests/Fixtures/Plugins/MCServerLauncher.Plugin.ReturnedError/MCServerLauncher.Plugin.ReturnedError.csproj -c Release -o artifacts/plugin-e2e/daemon/plugins/fixture.returned-error
dotnet publish tests/Fixtures/Plugins/MCServerLauncher.Plugin.Throwing/MCServerLauncher.Plugin.Throwing.csproj -c Release -o artifacts/plugin-e2e/daemon/plugins/fixture.throwing
$env:MCSL_PUBLISHED_DAEMON = (Resolve-Path artifacts/plugin-e2e/daemon/MCServerLauncher.Daemon.exe)
dotnet test tests/MCServerLauncher.PluginIntegrationTests/MCServerLauncher.PluginIntegrationTests.csproj -c Release /m:1

pwsh -File tools/VerifyNoV1Runtime.ps1
dotnet pack src/MCServerLauncher.Common/MCServerLauncher.Common.csproj -c Release -o artifacts/packages
dotnet pack src/MCServerLauncher.Daemon.API/MCServerLauncher.Daemon.API.csproj -c Release -o artifacts/packages
dotnet list src/MCServerLauncher.Daemon.API/MCServerLauncher.Daemon.API.csproj package --include-transitive

git diff --check
git status --short --branch
```

`VerifyNoV1Runtime.ps1` 同时断言 generator project 已从 solution/filesystem 移除；不保留空目录。API dependency 命令的输出由 package-graph test 校验 allowlist，不能只靠人工阅读。

## 21. ADR

### Decision

先建立唯一 application core，再以一次性 breaking cutover 把 daemon/DaemonClient/WPF 切到 V2并删除 V1，最后在 frozen V2 catalog 上实现 startup-only plugin host。首期 plugin surface 只包含 typed RPC registration、typed event publication 与 immutable instance query。

### Drivers

- 项目负责人明确拒绝多余 fallback/兼容，要求一步到位；
- plugin API 必须建立在稳定业务边界，而非旧 transport internals；
- 高并发 read 需要安全持有、零分配、无锁的 snapshot；
- plugin failure 不应影响 daemon availability；
- direct plugin Contracts 与 local typed events 要保留进程内性能优势；
- 六原则要求减少横切面和永久并存债务。

### Rejected

- 修补旧 additive plan：矛盾和旧假设散落各节，执行者容易继续保留 fallback。
- V1/V2 长期双轨：形成双行为源和双测试矩阵。
- public `Result<T, PluginError>` 默认边界：Result 不变导致传播噪音。
- fork RustyOptions / `IResult<out ...>`：增加结果模型和可能分配，收益不足。
- 自研 local event bus：MessagePipe 在 untrimmed JIT 边界内已成熟；host wrapper 可掌握治理语义。
- 暴露 MessagePipe：把第三方版本/ALC identity 变成 plugin ABI。
- 首期 plugin dependency/services：没有首期 consumer，先完成可验证的 RPC/event/snapshot host。
- hot unload：startup-only 没有真实需求，direct references 也使可靠 unload 更复杂。
- Native AOT/trimmed plugin host：与 runtime ALC/MessagePipe 路线冲突，不维护第二 SKU。
- legacy relay migration：当前 daemon 没有 producer，延续只会固化幽灵契约。

### Consequences

- cutover change set 较大，必须以前置 characterization 与完整 parity tests 控制风险；
- DaemonClient public API 需要 major bump；
- plugin-enabled publish 不再支持 trimming/Native AOT；
- startup catalog freeze 后不能运行期添加 plugin；
- subscriber 与 plugin 都是 trusted code，capability 是治理/审计而不是 sandbox；
- future direct Contracts dependency 需要明确 DAG 和 shared assembly identity，但不需要每次调用 RPC。

## 22. References

- .NET plugin tutorial: https://learn.microsoft.com/dotnet/core/tutorials/creating-app-with-plugin-support
- AssemblyLoadContext: https://learn.microsoft.com/dotnet/core/dependency-loading/understanding-assemblyloadcontext
- Assembly unloadability: https://learn.microsoft.com/dotnet/standard/assembly/unloadability
- System.Text.Json schema exporter: https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/extract-schema
- .NET package validation: https://learn.microsoft.com/dotnet/fundamentals/apicompat/package-validation/overview
- JSON-RPC 2.0: https://www.jsonrpc.org/specification
- OpenRPC: https://spec.open-rpc.org/
- MessagePipe: https://github.com/Cysharp/MessagePipe
- McMaster.NETCore.Plugins shared types: https://github.com/natemcmaster/DotNetCorePlugins#shared-types
- NoneBot plugin requiring: https://nonebot.dev/docs/advanced/requiring
- prior risk assessment: `docs/review/2026-06-29-daemon-api-plugin-risk-assessment.md`
- frozen halt semantics: `docs/superpowers/plans/2026-07-10-daemon-inst-lifecycle-commands.md`

## Changelog

- 2026-06-27: Initial daemon API / trusted in-process plugin / V2 JSON-RPC plan.
- 2026-06-28: Added AOT, Apifox/OpenRPC, result model and ABI review decisions.
- 2026-06-30: Added hook pairing and implementation-risk review notes.
- 2026-07-10: Architecture owner review rejected the additive/fallback plan and replaced it in place with application-core-first, one-shot V2 cutover and startup-only plugin host.
- 2026-07-10: Froze `StatePublisher<T>`/`PublishedState<T>` COW semantics, `Result<T, DaemonError>` public boundaries, sealed host-attributed `PluginError`, and no RustyOptions covariance fork/wrapper.
- 2026-07-10: Froze MessagePipe 1.8.2 as daemon-internal typed event implementation only, with untrimmed JIT single-file host + sidecar plugins and no Native AOT/trimmed product target.
- 2026-07-10: Froze future plugin dependency model as manifest DAG + authoritative `PluginA.Contracts` shared assembly + direct CLR interface calls; implementation is the mandatory post-first-release milestone.
- 2026-07-11: Started Phase 0, aligned governance to the approved end state, captured the 36-action parity/deletion inventory and normalized V1 benchmark baseline, and explicitly excluded unsafe V1 transport bugs from parity.
- 2026-07-11: Clarified packaging from repo evidence: Daemon.API is the single explicit SDK entry package, Common is a version-locked transitive package, plugin bundles use an opt-in buildTransitive filter for shared contracts, and performance means require a matching environment or paired run.
- 2026-07-11: Completed Phase 0 with Release and `--no-build` protocol gates at 382/382, benchmark project build clean, and final whitespace/JSON checks clean. Independent review found and closed the notification all-connection fan-out P1; fixture cleanup P2s were fixed, while the real-clock restart delay remains an explicit migration-only characterization rather than a V2 timing contract.
- 2026-07-11: Verified the no-install Microsoft Learn CLI fallback, documented it in `harness.md`, and paused before Phase 1 so the active goal can resume after the agent runtime restarts.
- 2026-07-11: Completed Phase 1 with exact packed dependency locks (`MCServerLauncher.Common [1.0.0]`, `RustyOptions [0.10.1]`), real-nupkg graph checks, and first-release textual ABI baselines for Daemon.API plus all exported Common Contracts; the five protocol enums now serialize as frozen snake_case strings through explicit source-generated metadata.
- 2026-07-11: Hardened authoritative published-state version gaps, stale/equal publish rejection, writer-reentrancy rejection, immutable catalog validation, and concurrent-version uniqueness. Acceptance passed 42/42 API tests, a clean Release solution build, Release protocol tests at 382/382 (including `--no-build`), and 0 B/op for both published-state read paths; independent re-review reported no remaining P0/P1.
- 2026-07-11: Clarified the Phase 2 boundary around one daemon-internal `FileSessionCoordinator`, config side-channel deletion, immutable domain events, and V1 wire-only adapters. Phase 2 has not started; execution is paused for a runtime restart.
- 2026-07-12: Completed Phase 2 local application core and authoritative instance-state migration with 0 open P0/P1 findings from independent Sol review. Acceptance passed the focused fixed-tree suite at 73/73 twice, Daemon.API tests at 42/42, Release protocol tests and the Release `--no-build` gate at 442/442 each, Daemon.API/daemon/full-solution Release builds at 0 warnings / 0 errors, and `git diff --check`, with no update/upload staging residue. The generated-docs check is not applicable until Phase 3 creates the currently absent `tools/MCServerLauncher.ProtocolDocs`; execution is paused before Phase 3 for a runtime restart.
- 2026-07-12: Completed Phase 3 across the 14 commits `99c66d21^..a8671c83`: added the 38-RPC/4-event catalog with explicit `JsonTypeInfo` and application bindings; generated frozen catalog/OpenRPC/Apifox outputs; migrated daemon-local events to daemon-internal MessagePipe; added the ordered catalog feed with admission/drain ownership; composed the final runtime catalog and `rpc.discover` including a synthetic plugin definition; added the catalog/event fan-out benchmarks; corrected the conditional catalog-change schema; and removed handwritten catalog count constants. Acceptance passed Daemon.ApiTests 56/56, Release ProtocolTests and final `--no-build` at 548/548, the recorded acceptance invocations of Daemon.API/daemon/daemon-client/full-solution Release builds each at 0 warnings / 0 errors, ProtocolDocs `--check` at catalog hash `51e351a9...`, and `git diff --check`. Independent Sol max final review reported 0 open P0/P1/P2. The pre-existing ProtocolTests source `CS0105` duplicate-using warning is visible on rebuild, was not introduced by Phase 3, and remains separately tracked; Phase 4 is pending.
- 2026-07-13: Completed and independently reviewed Phase 4A daemon V2 across `b394957a^..98f07298` with 0 open P0/P1/P2 findings. Acceptance passed Daemon.ApiTests 56/56, real TouchSocket host 1/1, Release ProtocolTests and final `--no-build` 782/782, daemon/full-solution/benchmark Release builds at 0 warnings / 0 errors, ProtocolDocs `--check` at hash `74268afdb2bb3dd9be54cc1edf92017f581d305270f6be201f2c033eb2f4b44f`, and `git diff --check`. The existing ProtocolTests `CS0105` warning and paired V2/V1 Phase 6 performance gate remain separate; 4B/4C are pending, `/api/v1` remains branch-internal, and Phase 4 is not releasable.
