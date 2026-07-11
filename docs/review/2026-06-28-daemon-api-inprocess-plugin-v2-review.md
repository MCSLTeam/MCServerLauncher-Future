# Review:Daemon API、In-Process Plugin System 与 V2 JSON-RPC/OpenRPC 转型方案

> **审核对象:** `docs/superpowers/plans/2026-06-27-daemon-api-inprocess-plugin-v2-plan.md`
> **审核日期:** 2026-06-28
> **审核依据:** 对照实际代码库(CodeGraph + 源码核验)+ `PROJECT_PLAN.md` / `RULES.md` / `EXECUTE_PLAN.md` 项目约束
> **Touched areas:** `docs`

---

## 总体结论

方向、动机、分层取舍都站得住。对"为什么不直接公开内部接口""为什么 registry 优先于 attribute""为什么 in-process 不等于安全边界"的论证质量很高。

但存在一个系统性问题:**plan 是在真空里设计 API/plugin/V2,没有把项目既定的 fixed invariants(AOT/trim、Apifox-only 文档、`Result<T,Error>` 约定)纳入约束**。由此产生 **3 个会直接卡死实施的硬冲突(Blocker)**,以及若干被低估的工程量。

> 一句话:**先解决三个 Blocker,再进 Phase 0。**

---

## 一、现状描述准确性(已逐项代码核验)

plan §2 列出的 seam 与风险点,逐条对照代码验证,**描述基本属实**:

| plan 声明 | 代码佐证 | 结论 |
|---|---|---|
| `IInstanceManager` 暴露运行时集合、权限过大 | `src/MCServerLauncher.Daemon/Management/IInstanceManager.cs:11-12` 直接暴露 `ConcurrentDictionary<Guid,IInstance> Instances` / `RunningInstances` | ✅ 准确 |
| `IInstance.Process` 绑定 `InstanceProcess` | `src/MCServerLauncher.Daemon/Management/InstanceManager.cs:144,161,167` 直接 `instance.Process!.WriteLine/KillProcess` | ✅ 准确 |
| `[ActionHandler]`+`ActionType` 不适合插件 | `src/MCServerLauncher.Daemon/Remote/Action/ActionHandler.cs:193-204` `internal` attribute;handler 签名 `Handle(TParam, WsContext, IResolver, ct)`(`ActionHandler.cs:101`) | ✅ 准确,**且耦合比 plan 描述的更深**(见 §三.1) |
| `FileManager` 高权限静态入口 | `src/MCServerLauncher.Daemon/Storage/FileManager.cs:18` static,全表面 `ResolveAndValidatePath` 直通 | ✅ 准确 |
| `DaemonRpcJsonBoundary` 已建边界 | `src/MCServerLauncher.Daemon/Serialization/DaemonRpcJsonBoundary.cs:19` 已有 source-gen + 可关反射 fallback | ✅ 准确 |
| `DaemonServiceComposition.ConfigureContainer` 存在 | `src/MCServerLauncher.Daemon/Bootstrap/DaemonServiceComposition.cs:21` | ✅ 准确 |

现状描述可信。问题出在 plan **没把这些现状连回项目约束**。

---

## 二、Blocker 级问题(推进前必须解决)

### 🔴 Blocker 1:`AssemblyLoadContext` 动态加载插件 ↔ Native AOT/trim 的根本冲突

这是 plan 最大的硬伤,却**通篇只字未提**。

- `PROJECT_PLAN.md:33`、`RULES.md:42`、`PROJECT_PLAN.md:44`:daemon 是 **`net10.0` + trimming/Native AOT 约束**,source-generated JSON 是 fixed invariant。
- 代码里 `ActionHandlerRegistryRuntime.CreateSelected`(`src/MCServerLauncher.Daemon/Remote/Action/ActionHandlerRegistry.cs:77-82`)已按 `JsonSerializer.IsReflectionEnabledByDefault` 二选一,AOT 场景强制走 generated 路径——说明项目**认真对待** AOT。
- 而 plan §13.2 / Phase 3 要求 "`AssemblyLoadContext` 加载" 插件 DLL。

**Native AOT 根本不支持运行时 `AssemblyLoadContext` 动态加载外部程序集。** 即便只做 trimming,第三方插件 DLL 里引用的类型也会被 trim 删除,导致运行时 `MissingMetadataException`。

plan 必须在 Phase 0 明确二选一并写进决策记录:

- **(A)** 引入插件 ⇒ daemon **放弃 Native AOT 发布**,只保留 trimming(且需为插件依赖做 trim root 预留);或
- **(B)** 保留 AOT ⇒ 插件模型只能做 compile-time / source-generator 注册,放弃运行时动态加载。

目前 plan 同时承诺两者,是一个"实现到一半才发现走不通"的陷阱。建议新增一条决策记录,并列为 Phase 0 退出条件。

### 🔴 Blocker 2:OpenRPC `rpc.discover` ↔ "Apifox-only" 文档策略冲突

- `RULES.md:81`:**"Daemon machine-readable protocol docs are Apifox-only."**
- `RULES.md:82`:禁止引入 Swagger/OpenAPI/HTTP bridge 类机器可读协议文档(除非显式变更文档策略)。

plan §12.3 / Phase 6 要让 daemon 通过 `rpc.discover` 在线返回 OpenRPC 文档,这是一条新的 daemon 机器可读协议文档来源。OpenRPC ≠ OpenAPI(非 HTTP),但与 RULES 精神直接张力,并制造**双源真相**:checked-in 的 `apifox.json` 与 runtime live OpenRPC 谁是权威?漂移时以谁为准?

plan §16 Phase 0 只写了"更新 `PROJECT_PLAN.md` / `EXECUTE_PLAN.md`",**漏掉 `RULES.md:81-82` 的文档策略修订**。建议:

- Phase 0 显式增加一条:"修订 `RULES.md` 文档策略,定义 Apifox(人工权威描述)与 `rpc.discover`(live runtime 描述)的分工与主从关系"。
- 否则该 plan 一旦执行会违反项目规则。

### 🔴 Blocker 3:`ApiResult` 是未定义类型,与既有 `Result<T, Error>` 约定脱节

- `RULES.md:48`:**"Use `Result<T, Error>` where daemon operations can fail as part of normal control flow."**
- 现状:action handler 全用 `Result<TResult, ActionError>` + `ActionRetcode`(`src/MCServerLauncher.Daemon/Remote/Action/ActionHandler.cs:62-81`);`InstanceManager` 全用 `Result<T, Error>`(`RustyOptions`)。

但 plan §8.1 `IInstanceControlApi.StartAsync` 返回 `Task<ApiResult>`——`ApiResult` 在 plan 里**从未定义**,也没说明它与 `Result<T, Error>` / `ActionError` / `ActionRetcode` 的关系。这是设计黑洞,直接影响 Phase 1 的 contract 定义。

建议:直接复用 `Result<T, Error>`(或定义 `ApiResult = Result<Unit, ApiError>` 别名),并在 §8 给出明确定义,而不是留一个裸类型名。

---

## 三、被低估的工程量(应补进 plan,否则 Phase 估时会严重失真)

### 1. built-in handler registry 化的成本被严重低估

plan §9.5 用一句话带过"为 built-in action 提供 `ActionType -> RpcMethod` 映射"。但代码显示现有 handler 签名是:

```csharp
// src/MCServerLauncher.Daemon/Remote/Action/ActionHandler.cs:101
Result<TResult, ActionError> Handle(TParam param, WsContext ctx, IResolver resolver, CancellationToken ct);
```

`WsContext` 和 `IResolver` 是 **TouchSocket 类型,直接焊在 handler 签名里**。新 registry 的 `RpcContext`(plan §9.3)要桥接**全部几十个 built-in handler**,每个都要从 `RpcContext` 反解出 `WsContext` / `IResolver`。这不是"映射",是一次全员 adapter 改造。建议在 Phase 4/5 之间单列一个"built-in handler 迁移到 RpcContext"的任务组并标明规模。

### 2. `INotifyBus` adapter 复杂度被低估

plan §13.3 "IEventService → INotifyBus / event adapter" 一笔带过。但实际事件广播逻辑**分散**在:

- `IEventService`(signal,`src/MCServerLauncher.Daemon/Remote/Event/IEventService.cs`)
- `WsEventPlugin`(fan-out)
- `WsContextContainer`(连接枚举,`src/MCServerLauncher.Daemon/Remote/WsContextContainer.cs`)
- `EventTriggerService`(规则触发,`src/MCServerLauncher.Daemon/Remote/Event/EventTriggerService.cs:255` 直接遍历 `_wsContexts` 发帧)

`IEventService` 本身非常薄(只有 `OnEvent` + `Signal`)。plugin 拿到的 `INotifyBus` 要真正能 publish 给客户端,需要桥接这一整条链路。建议在 §13.3 拆出明确的 adapter 边界。

### 3. capability 与 permission 是两个正交体系,plan 没厘清交叉点

现状已有 client-facing 权限模型:`Authentication.Permission.Of(...)` + `WsContext.permissions` + handler 注册时的 `permission`(`src/MCServerLauncher.Daemon/Remote/Action/ActionHandler.cs:199`,如 `[ActionHandler(..., "*")]`)。

plan §7.3 又引入 plugin-facing `capability` 组。两者维度不同:

- **capability** = 插件*能拿到什么 service*(注册时声明,服务网关 gate)
- **permission** = 客户端*能否调用某 method*(连接级,`WsContext`)

但 §9.4 `RpcDescriptor` 同时塞了 `Permission` 字段,却没说清:一个插件注册的 method 被 client 调用时,**capability 校验(插件有没有这个能力)和 permission 校验(client 有没有权限)如何串联、谁先谁后、失败分别返回什么**。这是 V2 dispatcher(Phase 5)的核心逻辑,必须在 §9.4 或 §11.5 明确分层语义,否则 Phase 5 会卡在设计上。

### 4. plugin ABI / 版本兼容策略空白

in-process 插件与 daemon 共享进程 ABI,是 dll hell 的高发区。manifest 有 `"mcsl_api": "1.0"`,但**版本不匹配时的行为未定义**(拒绝?警告?尽力运行?)。plan §16 Phase 8 只有一句模糊的 "compatibility/versioning policy"。建议 Phase 1 就定义语义版本规则 + 加载时版本校验行为,因为它是 `IPluginContext` / manifest 设计的输入,不是收尾事项。

---

## 四、次要问题(建议性)

1. **命名空间 `MCServerLauncher.Daemon.API.Json`(§6.2)易与 `System.Text.Json` 混淆**,尤其 `IJsonApi.Options` 返回的就是 `System.Text.Json.JsonSerializerOptions`。建议改 `Serialization`,与内部目录一致。

2. **Phase 3(Plugin host)排在 Phase 4(Registry)之前**。但插件的核心价值是注册 RPC/notify/hook,没有 registry,Phase 3 的 sample plugin fixture 只能测 lifecycle。建议 Registry 与 Plugin host 并行或前置,否则 Phase 3 fixture 覆盖面很窄。

3. **`rpc.discover` 本身的 AOT 安全性**未提。好在 plan §12.4 优先用 descriptor 的 `JsonTypeInfo`,方向 trim-friendly ✅,但 OpenRPC document 的序列化路径需要显式纳入 source-gen context,建议在 Phase 6 加一条。

4. **touched areas(§头部)缺 `integrations` 或新项目对 solution/pack 的影响说明**。引入 `MCServerLauncher.Daemon.API` 会改变 NuGet / solution 结构,值得单列。

---

## 五、做得好的地方(应保留)

- §5 关于"禁用反射 ≠ 安全黑箱"的论证,**与代码里既有的 `DaemonStjReflectionFallbackPolicy` + `IsReflectionEnabledByDefault` 开关(`src/MCServerLauncher.Daemon/Serialization/DaemonRpcJsonBoundary.cs:44-46`)完全同向**,是项目既有思路的延续,不是空想。
- §3.3 "不拆太多 NuGet"、§7.1 "程序集分级只做辅助风险信号"——务实,避免了过度工程。
- V2 additive(§11.1 `/ws` + `/ws/v2`)符合 `PROJECT_PLAN.md:28` "protocol-breaking changes not acceptable without migration"。✅
- 已有 Changelog(§末)、已声明 touched areas、已引用 `superpowers:subagent-driven-development`——满足 `RULES.md` 的 plan 规范。✅

---

## 六、建议的下一步

三个 Blocker 都属于"plan 层面没做决定"而非"实现细节",应该在动工前补进 plan。最小修订:

1. **Phase 0 增设退出条件**:明确 AOT 与插件加载的取舍(决策 A/B)、修订 `RULES.md:81-82` 文档策略、定义 `ApiResult` / 复用 `Result<T,Error>`。
2. **§9.4 / §11.5 增加 capability × permission 串联语义**。
3. **§13 拆出 built-in handler 迁移 与 INotifyBus adapter 两个显式任务组**。
4. **Phase 1 补 plugin ABI 版本策略**(不应留到 Phase 8)。
