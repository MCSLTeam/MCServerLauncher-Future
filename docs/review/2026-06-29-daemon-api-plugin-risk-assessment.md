# Daemon API / In-Process Plugin / V2 JSON-RPC/OpenRPC 转型:风险评估报告

> **评估对象:** `docs/superpowers/plans/2026-06-27-daemon-api-inprocess-plugin-v2-plan.md`(2026-06-28 修订稿,第二轮 re-review 通过)
> **评估日期:** 2026-06-29
> **评估范围:** 实施 plan 时,为保持 **daemon 架构清晰、库简洁、代码一致、克制的美学、积极且审慎地采用 .NET 10 / C# 14、以及性能可控** 所面临的风险
> **评估依据:** 实际代码(CodeGraph + 源码核验)+ `PROJECT_PLAN.md` / `RULES.md` + daemon 工程配置(`net10.0` / `LangVersion 14` / `PublishSingleFile` / `EnableTrimAnalyzer`)
> **Touched areas:** `docs`

---

## 1. 摘要(Executive Summary)

plan 的设计决策本身合理,已无架构级 Blocker。真正的风险来自**并存的复杂度累积**:V1/V2 双协议、core / adapter / registry / gate / host 多层、serialization 边界从 3 套涨到 6 套、capability × permission × hook × plugin 的组合空间。这些不是"做错",而是"做全"的代价。

在此基础上,本报告额外纳入四个维度:**代码一致性**(新抽象极易与既有模式漂移)、**克制的美学**(抽象膨胀是最大隐性成本)、**.NET 10 / C# 14 采纳**(既是机会也是滥用风险)、**性能优化**(plugin/V2 不能拖垮现有热路径)。

**三条最关键的建议(若只读三句):**

1. **单一核心服务层**:V1/V2/插件都只认核心服务面(`IInstanceControlApi` 等),`IInstanceManager` 降级为实现细节。这是防止"两个 daemon"的根本。
2. **每个并存都设退场条件**:V1 何时 deprecate、unload 何时才需要、hook 何时扩——不设退场的并存都会变永久债务。第一期优先**砍**(unload、多数 hook)而非全做。
3. **新特性服从"可读性或性能二选一"门槛**:`FrozenDictionary` / `Lock` / source-gen 等高价值特性应积极用;纯语法糖在没收益时不用。一致性优先于新颖。

---

## 2. 评估方法

### 2.1 风险维度(7 个)

| 维度 | 关注 |
|---|---|
| 架构清晰度 | 分层、依赖方向、可演进性 |
| 库简洁度 | API 表面积、抽象数量 |
| 运行时正确性 | 并发、生命周期、unload |
| 代码一致性 | 跨层模式、错误模型、async 约定 |
| 克制的美学 | YAGNI、抽象膨胀、配置开关 |
| .NET 10 / C# 14 采纳 | 特性滥用 vs 高价值机会 |
| 性能优化 | allocation、热路径、并发、序列化 |

### 2.2 严重度定义

- **Critical:** 不处理会在实施中段卡死或造成不可逆架构债务。
- **High:** 不处理会显著侵蚀清晰度 / 正确性 / 性能,需在对应 Phase 内解决。
- **Medium:** 影响可维护性或体验,建议处理但可延后。
- **Low:** 风格性 / 体验性, opportunistically 处理。

### 2.3 风险条目格式

每条:维度 · 严重度 · 落点 Phase · 风险描述 · 对策。

---

## 3. 风险登记汇总表

| ID | 维度 | 风险 | 严重度 | 主要落点 |
|---|---|---|---|---|
| R1 | 架构清晰度 | V1/V2 双轨 → "两个 daemon" | **Critical** | 2 / 5 / 8 |
| R2 | 架构清晰度 | API 包类型归属未定(Common 复用 vs 投影) | High | 1 |
| R3 | 架构清晰度 | 分层深(8 层)+ 无可观测性/性能红线 | High | 1 / 2 / 5 |
| R4 | 库简洁度 | `IDaemonApi` 上帝对象 + `Daemon`/`Services` 双入口冗余 | Medium | 1 |
| R5 | 库简洁度 | serialization 边界 3→6 + 插件污染 singleton | High | 2 / 6 |
| R6 | 库简洁度 | capability 裸字符串 + 17 项矩阵 | Medium | 1 / 3 |
| R7 | 运行时正确性 | plugin unload 可靠性(ALC 经典坑) | High | 4(建议砍) |
| R8 | 运行时正确性 | registry 并发模型未定义 | High | 3 |
| R9 | 运行时正确性 | hook 横切爆炸(13 个) | Medium | 4 |
| R10 | 代码一致性 | async / `CancellationToken` / 返回类型跨层不一致 | **High** | 1 / 2 |
| R11 | 代码一致性 | 错误模型四套表示,层间转换漂移 | **High** | 1 / 2 / 5 |
| R12 | 代码一致性 | 新包命名/可见性/record 风格漂离既有代码 | Medium | 1 |
| R13 | 克制的美学 | 抽象膨胀 / 过度设计(YAGNI) | **High** | 全程 |
| R14 | 克制的美学 | 配置开关与条件路径过多,测试矩阵爆炸 | Medium | 0 / 4 |
| R15 | .NET 10 采纳 | 新特性滥用 vs 克制的张力 | Medium | 全程 |
| R16 | 性能优化 | V2 dispatch 缺 allocation/延迟预算 | High | 5 |
| R17 | 性能优化 | registry / gate 并发锁与快照 | High | 3 / 4 |
| R18 | 性能优化 | 序列化路径分配回归 | Medium | 2 / 5 / 6 |
| R19 | 性能优化 | notification/event fan-out 性能 | Medium | 2 / 5 |
| — | .NET 10 采纳 | **机会清单**(高价值特性,正向) | — | 各 Phase |

---

## 4. 详细风险条目

### 4.1 架构清晰度维度

#### R1 — V1/V2 双轨长期并存 → "两个 daemon"
**维度:** 架构清晰度 · **严重度:** Critical · **落点:** Phase 2 / 5 / 8

**风险:** 现状会演变成 V1 handler **直连** `IInstanceManager`(`resolver.GetRequiredService<IInstanceManager>().TryStartInstance`,返回 `bool`/`IInstance?`),而 V2 dispatcher 经 `IInstanceControlApi` adapter 调同一个 `InstanceManager`。两条路径绕同一核心,但返回语义、错误码映射、权限检查时机、取消传播都可能分叉。半年后每个能力要维护两套行为,ProtocolTests ×2。

**对策:** 建立单一核心服务层——即使保留 `[ActionHandler]` 作为 V1 注册机制,V1 handler **内部也委托核心服务面**(`IInstanceControlApi` 等),`IInstanceManager` 降级为实现细节。attribute 只是 transport/registration frontend,行为源头只有一个。同时在 Phase 7/8 明确 **V1 deprecation criteria**(V2 对等覆盖后 V1 标 deprecated),否则双轨无限期并存。

#### R2 — API 包的类型归属未定
**维度:** 架构清晰度 · **严重度:** High · **落点:** Phase 1

**风险:** `IManageApi.GetReportAsync` 返回 `InstanceReport`,而 `InstanceReport` 现在在 `MCServerLauncher.Common.ProtoType.Instance`。第一个 API 包 PR 就要决策:复用 Common 类型(A,但 Common 进入公共契约层)还是 API 包造投影(B,三份类型 + mapping)。不定则各 phase 各自选。

**对策:** 选 **A(复用 Common wire 类型)**,立规矩:被 API 包暴露的 Common 类型 = 公共契约,变更走 semver;daemon 实现细节类型(`InstanceProcess` 等)不进 Common,由 API 包定义抽象(plan §2.2 已对)。混合策略最省事,避免三份类型地狱。写入 Phase 1。

#### R3 — 分层深(最坏 8 层)+ 无可观测性/性能红线
**维度:** 架构清晰度 · **严重度:** High · **落点:** Phase 1 / 2 / 5

**风险:** 一次 `instance.start` 在 V2 最坏穿过:dispatcher → capability → permission → registry lookup → `RpcContext` adapter → `IInstanceControlApi` adapter → `IInstanceManager` → `InstanceProcess`。出错难定位;每层可能 allocation。plan §15.6 有 benchmark 但**无红线**。

**对策:** (a) 可观测性贯穿:每插件独立 log scope(plugin id)、capability/permission 拒绝与 registry 冲突有结构化日志、V2 dispatch 用 correlation id 贯穿;(b) 性能红线:V2 dispatch p99 延迟与 allocation 不得劣于 V1 baseline 超阈值,每个 phase 交付时校验。

---

### 4.2 库简洁度维度

#### R4 — `IDaemonApi` 上帝对象 + `Daemon`/`Services` 双入口冗余
**维度:** 库简洁度 · **严重度:** Medium · **落点:** Phase 1

**风险:** §6.3 `IDaemonApi` 挂 6 个子面,`IPluginContext` 同时暴露 `IDaemonApi Daemon` 和 `IPluginServices Services`——两个入口都能拿同一批服务。`IDaemonApi` 会无限膨胀;插件作者不知用哪个;实现里两者易分叉。

**对策:** 明确分工:`IDaemonApi` 是稳定能力域 facade(新增域需评审),`IPluginServices` 是按 capability 精细取用入口;或只留一个。两者职责重叠的部分必须收敛。

#### R5 — serialization 边界 3→6 + 插件污染 singleton
**维度:** 库简洁度 · **严重度:** High · **落点:** Phase 2 / 6

**风险:** 现有 3 套 boundary(`DaemonRpcJsonBoundary` / `DaemonPersistenceJsonBoundary` / `DaemonClientRpcJsonBoundary`)已要求类型分散注册。plan 再加 `ISerializationApi.Register` + V2 DTO + OpenRPC context。最大隐患:`ISerializationApi.Register(JsonTypeInfo)` 若注册到 daemon singleton `JsonSerializerOptions`,插件 unload 后**注册项残留**,污染全局、wire 漂移。

**对策:** context 分层 + 所有权清晰:Common wire(共享不可变)/ Daemon RPC / Daemon persistence / **Plugin DTO(plugin-scoped resolver,绝不进 daemon singleton)**。`ISerializationApi` 给插件 plugin-scoped options,而非往全局注册。Phase 2 serialization gate 定死。

#### R6 — capability 裸字符串 + 17 项矩阵
**维度:** 库简洁度 · **严重度:** Medium · **落点:** Phase 1 / 3

**风险:** §7.3 的 17 个 capability 是裸字符串,拼错(`"managment.query"`)静默失败;manifest 声明与代码隐式依赖可能不一致;capability × permission × plugin 测试组合爆炸。

**对策:** capability **strongly-typed**(`readonly struct Capability`,对齐 §9.3 已对 `RpcMethod` 做的);加载期做声明一致性校验(touch ⊆ declared)。`RpcMethod` 已用此模式,capability 没理由退回裸字符串。

---

### 4.3 运行时正确性维度

#### R7 — plugin unload 可靠性(建议第一期砍)
**维度:** 运行时正确性 · **严重度:** High · **落点:** Phase 4

**风险:** in-process collectible `AssemblyLoadContext` 的 unload 是 .NET 经典坑:任何 long-lived 引用(插件持有 `IInstanceManager`、订阅 event、注册 hook)都让 unload 不生效 → DLL 卸不掉 → 内存泄漏 + 旧 hook 幽灵调用。§15.2 当测试项,但它是架构级可靠性问题,测试兜不住。

**对策:** 质疑需求——第一期是否真需热卸载?对 trusted 本地 daemon,"改插件 = 重启生效"通常可接受。**建议第一期只支持启动期加载,砍 unload**,省掉 ALC collectible + 引用追踪 + hook/event 干净拆除。unload 列为未来能力,非 P0。

#### R8 — registry 并发模型未定义
**维度:** 运行时正确性 · **严重度:** High · **落点:** Phase 3

**风险:** registry 是并发读写(插件注册 + 请求查找同时)。plan 未定义并发模型,易出现锁竞争、快照撕裂、注册/卸载与请求竞态。

**对策:** 沿用现有 `ActionHandlerRegistrySnapshot` 的 **COW snapshot 读无锁 + 写细粒度锁**模式(见 `src/MCServerLauncher.Daemon/Remote/Action/ActionHandlerRegistry.cs`)。Phase 3 显式定义 registry 的并发契约,写入测试。

#### R9 — hook 横切爆炸(13 个单向 → 成对最小集)
**维度:** 运行时正确性 · **严重度:** Medium · **落点:** Phase 4

**风险:** 原 §13.4 一次列 13 个**单向** hook(before/after/error 混杂),易出现:pre 跑了 post 没跑(资源泄漏)、命名散乱不可记忆、13 × N 插件 × priority 隐式耦合温床。

**对策:** hook 改为**成对设计**(`loading/loaded`、`pre/post`),第一期最小成对集:`daemon.pre_start/post_start`、`daemon.pre_stop/post_stop`、`plugin.loading/loaded`、`rpc.pre/post`(error 并入 post 参数)、`instance.pre_start/post_start`、`instance.pre_stop/post_stop`。强制**成对触发保证**(finally 语义):pre 触发后 post 必触发,即使主流程失败;pre 可声明 vetoable,否则异常吞 + 记日志。砍 `rpc.error`(并入 post)、`instance.log`(走 notify bus)、`factory.*`(无第二消费者推迟)。每新增一对需论证 ≥2 消费者。详见 plan §13.4。

---

### 4.4 代码一致性维度(新增)

> 这是 plan 引入大量新抽象后最容易失控的维度。现有 daemon 已有**不一致先例**(见下),新 API 既是建立一致性的机会,也可能延续甚至放大不一致。

#### R10 — async / `CancellationToken` / 返回类型跨层不一致
**维度:** 代码一致性 · **严重度:** High · **落点:** Phase 1 / 2

**风险:** 现有 `IInstanceManager` 已经混用:`TryStartInstance` → `Task<IInstance?>` 带 ct;`TryStopInstance` → `bool` **无 ct**;`KillInstance` → `void` 无 ct;`SendToInstance` → `bool` 无 ct。新 `IInstanceControlApi` 用 `Task<Result<Unit,Error>>` + ct(§8.1)。adapter 桥接这些时,cancellation 与 async 语义容易断裂。

**对策:** 核心服务层统一约定——**所有 async 操作返回 `ValueTask`/`Task` 且签名末尾强制 `CancellationToken`**;无 ct 的旧方法在 adapter 层补 `CancellationToken.None` 或传入,并在注释标明。`RULES.md` 已有"Use `CancellationToken` on async lifecycle",**扩展为对所有新 API 面强制**,并在 Phase 1 写进 API 包约定。

#### R11 — 错误模型四套表示,层间转换漂移
**维度:** 代码一致性 · **严重度:** High · **落点:** Phase 1 / 2 / 5

**风险:** 同时存在 `Result<T,Error>`(RustyOptions)、`ActionError`/`ActionRetcode`(V1)、JSON-RPC error code(V2)、已弃 `ApiResult`。四套错误表示,层间转换易丢失信息、错误码映射漂移。

**对策:** 单一权威错误模型(`Result<T,Error>`),V1/V2 transport 各做**单向适配**且适配点唯一、可测。registry / hook / gate 的失败**也用 `Result`**,不引入第五种。plan §0.4/§8.4 已定 transport 方向,需扩展到**所有新抽象的内部失败**。

#### R12 — 新包命名 / 可见性 / record 风格漂离既有代码
**维度:** 代码一致性 · **严重度:** Medium · **落点:** Phase 1

**风险:** 现有 daemon 风格:大量 `internal` 接口(`IActionHandler` internal)、static 类(`FileManager`)、snake_case JSON 边界、`sealed record` 用于 DTO、`RustyOptions` Result。新 API 包若自由发挥,会变成"另一个项目的代码",review 时各凭喜好。

**对策:** Phase 1 立 **API 包代码风格契约**(可见性规则、命名、`record` vs `class` 边界、nullability `#nullable enable` 全开、`sealed` 默认),写入 plan 或 `AGENTS.md`,PR review 据此把关。一致性是"克制美学"的可执行形式。

---

### 4.5 克制的美学维度(新增)

#### R13 — 抽象膨胀 / 过度设计(YAGNI)
**维度:** 克制的美学 · **严重度:** High · **落点:** 全程

**风险:** plan 引入 11 个新 daemon 内部类型(§13.2)、6 个 `IDaemonApi` 子面、4 个 registry、13 个 hook、17 个 capability。每个单看合理,叠加是巨大表面积。实现成本高、认知负荷重、API 难学、维护负担重。"做全所有合理设计"本身就是反模式。

**对策:** YAGNI 收敛——每个新抽象在第一期必须回答:**"它在第一期有 ≥2 个实现或调用方吗?"** 没有 → 推迟。先收敛(让一个能力跑通),再泛化(抽接口、加 registry)。`IHookRegistry`、`IFeatureRegistry`、`ITransportApi`、`IInstanceStore` 等若第一期无第二消费者,先不抽。

#### R14 — 配置开关与条件路径过多
**维度:** 克制的美学 · **严重度:** Medium · **落点:** Phase 0 / 4

**风险:** 现有已有 `ActionHandlerRegistryMode`(Legacy/Generated)、`UseGeneratedActionRegistry`、`DaemonStjReflectionFallbackPolicy`,plan 再加 plugin-enabled/AOT SKU 开关、batch 支持开关、serialization fallback 策略。每加一个开关,测试矩阵 ×2,未测路径藏 bug。

**对策:** 开关数量设上限;每个开关有**默认值 + deprecation 计划**;能用编译期/启动期决定的不留运行期开关。Phase 0 冻结开关清单,新增需评审。

---

### 4.6 .NET 10 / C# 14 采纳维度(新增)

> 项目已 target `net10.0` + `LangVersion 14`(`MCServerLauncher.Daemon.csproj:19`),特性可用。本维度既是风险(滥用),也是高价值机会。

#### R15 — 新特性滥用 vs 克制的张力
**维度:** .NET 10 采纳 · **严重度:** Medium · **落点:** 全程

**风险:** C# 14 的 `field`、extension members、params collections、user-defined compound assignment 等若"为用而用",会降低可读性、增加学习成本、与克制美学冲突。

**对策:** 设**采纳门槛**——每个新特性使用须满足以下之一:
1. **可证明的可读性提升**(减少样板、意图更清晰);或
2. **可证明的性能收益**(allocation/分支减少)。

不引入仅"看起来现代"的语法糖;团队不熟悉的特性先在隔离模块试点再推广。

#### 机会清单(高价值,建议积极采纳)

这些特性经评估与项目 AOT/trim 纪律和热路径诉求一致,**建议在对应 Phase 主动采用**:

| 特性 | 用途 | 落点 | 备注 |
|---|---|---|---|
| `FrozenDictionary` / `FrozenSet` | capability 表、permission 映射、`ActionType→RpcMethod` inventory 等**启动后不变**查找表 | 3 | build-time freeze,O(1) 查找;AOT/trim 友好 |
| `System.Threading.Lock` | registry / gate / lifecycle 的互斥,替代 `lock(obj)` | 3 / 4 | contended 场景更快;无反射 |
| `field` 关键字(C#14) | API 包 record/类减少 backing field 样板 | 1 | 提升可读性 |
| `params ReadOnlySpan<T>`(C#14) | 热路径 API(hook 注册、fan-out 入参)减少 params 数组分配 | 3 / 5 | 性能收益明确 |
| `extension` members(C#14) | 为 `RpcMethod`/`RpcContext`/`Result` 加流式扩展,比静态扩展类干净 | 1 / 5 | 可读性提升 |
| `CollectionsMarshal.AsSpan` | registry/fan-out 枚举避免 `List`→array 复制 | 3 / 5 | 现有 fan-out 受益 |
| `SearchValues<byte>` | envelope 解析、method 名路由的字节匹配 | 5 | transport 热路径 |
| `Task.WhenEach` | notification/event fan-out 流式处理 | 2 / 5 | 现 `Task.WhenAll` 可优化 |
| source-gen JSON(继续) | V2 DTO / OpenRPC model 全走 `JsonTypeInfo<T>` | 2 / 5 / 6 | 已在用,保持 |

**约束:** 所有采纳必须与 serialization boundary(R5)和 AOT/trim 纪律一致——`Frozen*` 与 `Lock` 无反射,安全;extension members / field 是编译期,安全。

---

### 4.7 性能优化维度(新增)

> 现有代码已建立热路径纪律(`JsonElementHotPathAdapters` / `JsonPayloadBuffer` / `AsyncTimedLazyCell` / snapshot registry)。风险是**新路径回归到随意 allocation**,把现有纪律抵消。

#### R16 — V2 dispatch 缺 allocation / 延迟预算
**维度:** 性能优化 · **严重度:** High · **落点:** Phase 5

**风险:** V2 dispatch 最坏 8 层,每层可能 allocation(envelope 解析、descriptor 查找、`RpcContext` 构造、adapter 包装、Result 装箱)。现有 V1 有 baseline benchmark。V2 默默比 V1 慢,plugin 系统拖垮热路径。

**对策:** 每个 Phase 交付跑 benchmark;V2 dispatch 的 p99 延迟与 bytes-allocated 不得劣于 V1 baseline 超阈值(设红线并写入 `EXECUTE_PLAN.md`)。registry 查找走 snapshot 读无锁(R8)。

#### R17 — registry / gate 并发锁与快照
**维度:** 性能优化 · **严重度:** High · **落点:** Phase 3 / 4

**风险:** 注册/卸载(写)+ 请求查找(读)并发;plugin lifecycle 与请求并发。锁竞争、死锁、快照撕裂。

**对策:** 读路径无锁(COW snapshot,沿用 `ActionHandlerRegistrySnapshot`);写路径细粒度 `System.Threading.Lock`(R15 机会清单)。并发契约显式写入 Phase 3 并配竞态测试。

#### R18 — 序列化路径分配回归
**维度:** 性能优化 · **严重度:** Medium · **落点:** Phase 2 / 5 / 6

**风险:** `JsonElement` 传递、`Result`→JSON-RPC 适配、OpenRPC 生成。现有代码有零拷贝理念(`JsonPayloadBuffer` 保留 explicit-null 语义、`JsonElementHotPathAdapters`)。新路径易回归到 `JsonSerializer.Serialize<object>` 这类 boxing/反射调用。

**对策:** V2 DTO 与 OpenRPC model 全走 source-gen `JsonTypeInfo<T>`,复用现有 hot-path adapter 模式;`Result`→JSON-RPC 适配优先用 `Utf8JsonWriter` 直接写,避免中间 `JsonElement`。

#### R19 — notification / event fan-out 性能
**维度:** 性能优化 · **严重度:** Medium · **落点:** Phase 2 / 5

**风险:** 广播到 N 连接。现有 `EventTriggerService` 遍历 `_wsContexts` + `Task.WhenAll` + 每连接 `SendAsync`,每帧可能重复序列化。连接数大时 fan-out 慢。

**对策:** payload 一次性序列化为 `ReadOnlyMemory<byte>`,广播共享 buffer 不重复序列化;用 `Task.WhenEach`(R15)或 per-connection send queue,避免慢连接阻塞快连接。

---

## 5. 横切元原则(保持简洁与清晰的根基)

1. **单一核心服务层** — V1/V2/插件都只认核心服务面;`IInstanceManager` 是实现细节,不是公共契约。(R1)
2. **每个并存都设退场条件** — V1 何时 deprecate、unload 何时才需要、hook 何时扩、开关何时退。不设退场的并存 = 永久债务。(R1 / R7 / R9 / R14)
3. **一致性优先于新颖** — 新代码沿用既有 async/ct/错误模型/命名/可见性约定;`RULES.md` 扩展覆盖新 API 面。(R10 / R11 / R12)
4. **YAGNI 收敛** — 第一期每个抽象需有 ≥2 个实现/调用方;先收敛再泛化。(R13)
5. **强类型 + 最小化横切** — capability、hook、`IDaemonApi` 子域用 strongly-typed 且最小集,编译期挡字符串拼写与组合爆炸。(R4 / R6 / R9)
6. **新特性服从"可读性或性能二选一"** — 积极用 `FrozenDictionary`/`Lock`/source-gen 等高价值特性;纯语法糖无收益不用。(R15 / 机会清单)
7. **协议无关的核心测试** — 核心服务层测一次,V1/V2 只测 transport 适配,避免 ×2 矩阵。

---

## 6. 建议的 Phase 级落地映射

| Phase | 必须落实的风险对策 |
|---|---|
| **Phase 0** | 冻结开关清单(R14);定 V1 deprecation criteria(R1) |
| **Phase 1** | 类型归属选 A(R2);API 包风格契约(R12);async/ct/Result 统一约定(R10/R11);`IDaemonApi`/`Services` 分工(R4);capability strongly-typed(R6);`field`/extension members 试点(R15) |
| **Phase 2** | 单一核心服务层骨架(R1);serialization 所有权分层 + 插件不污染 singleton(R5);serialization metadata gate(R5);fan-out adapter + `INotifyBus`(R19);可观测性骨架(R3) |
| **Phase 3** | registry 并发契约 + snapshot(R8/R17);capability 一致性校验(R6);`FrozenDictionary` 查找表(机会清单);`ActionType→RpcMethod` inventory |
| **Phase 4** | **砍 unload,仅启动期加载**(R7);hook 成对最小集 `pre/post` / `loading/loaded`(R9);sidecar/probing(R7 补);`System.Threading.Lock`(R17) |
| **Phase 5** | V2 dispatch 性能红线(R16/R3);`Result`→JSON-RPC `Utf8JsonWriter` 适配(R18);dispatch allocation 预算(R16);`SearchValues`/`Task.WhenEach`(R19/机会清单) |
| **Phase 6** | OpenRPC source-gen context(R5/R18);plugin DTO schema gate |
| **Phase 7/8** | V1 deprecation 执行(R1);benchmark 红线纳入 `EXECUTE_PLAN.md`(R16) |

---

## 附录 A:与前两轮 review 的关系

- **第 1 轮 review**(`2026-06-28-...-review.md`):提出 3 个 Blocker(AOT/插件、OpenRPC/Apifox、`ApiResult`)— 已在 plan §0.2/§0.3/§0.4 解决。
- **第 2 轮 re-review**(`2026-06-28-...-re-review.md`):确认 Blocker 解决 + 2 个文字级问题 — 已在 plan 修订。
- **本报告**:面向**实施期**的前瞻性风险,聚焦"做完之后如何不糊"。不重复 review 的 Blocker,而是把范围扩展到一致性 / 美学 / .NET 10 / 性能四个新维度。

## 附录 B:建议砍掉或推迟的复杂度(第一期)

为贯彻克制美学,以下建议在第一期**砍掉或推迟**:

- **plugin unload** → 仅启动期加载(R7)
- **13 个单向 hook** → 成对最小集(`pre/post`、`loading/loaded`),砍 `rpc.error` / `instance.log` / `factory.*`(R9)
- **`IHookRegistry` / `IFeatureRegistry` / `ITransportApi` / `IInstanceStore`** 若第一期无第二消费者 → 先不抽(R13)
- **runtime 配置开关** → 能编译期/启动期定的不留运行期(R14)
- **batch JSON-RPC** → 第一期明确拒绝而非支持(已在 §18 开放问题)

## Changelog

- 2026-06-29: 首版风险评估报告。整合三轮 review/分析,新增代码一致性、克制美学、.NET 10/C# 14 采纳、性能优化四个维度,共 19 条风险 + 1 份高价值特性机会清单 + 横切元原则 + Phase 级落地映射。
- 2026-06-30: R9 更新为成对 hook 设计(`pre/post`、`loading/loaded` + finally 语义),对齐 plan §13.4 修订;附录 B 与 §6 Phase 4 行同步。
