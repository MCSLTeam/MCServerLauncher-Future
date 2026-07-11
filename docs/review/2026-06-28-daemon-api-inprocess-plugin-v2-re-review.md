# Re-Review:Daemon API / In-Process Plugin / V2 JSON-RPC/OpenRPC 转型方案(修订版)

> **审核对象:** `docs/superpowers/plans/2026-06-27-daemon-api-inprocess-plugin-v2-plan.md`(2026-06-28 修订版)
> **审核日期:** 2026-06-28
> **审核轮次:** 第 2 轮(针对基于第 1 轮 review 修订后的稿件;第 1 轮见 `2026-06-28-daemon-api-inprocess-plugin-v2-review.md`)
> **审核依据:** 对照 `src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj` + 实际代码 + `PROJECT_PLAN.md` / `RULES.md`
> **Touched areas:** `docs`

---

## 一、上一轮三个 Blocker —— 全部妥善解决

| 上轮 Blocker | 本轮修订落点 | 评价 |
|---|---|---|
| 🔴 AOT ↔ `AssemblyLoadContext` | §0.2 冻结"plugin host 只走 JIT、Phase 1-6 不承诺 Native AOT";决策 3;§13.2 标注 `PluginLoader` 仅属 non-AOT path;Phase 0 退出条件 + Phase 4 标注 "JIT host path only" | ✅ **彻底解决**,且保留了 trim / source-gen discipline |
| 🔴 OpenRPC ↔ Apifox-only | §0.3 主从分工;§12.3 重写;决策 7 扩展;Phase 0 明确"修订 `RULES.md`" | ✅ **解决**,双源真相通过"职责差异"消解 |
| 🔴 `ApiResult` 未定义 | §0.4 冻结 `Result<Unit,Error>` / `Result<T,Error>`;§8.1 control 接口已改;§8.4 适配规则;决策 9 | ✅ **解决** |

四个"被低估工程量"中:

- **built-in handler 迁移**(§9.6 + Phase 3 inventory + Phase 5 分层迁移)✅
- **capability × permission**(§7.6 + §9.4 `RequiredCapabilities` + §11.5.1 dispatch 顺序,分层尤其清晰)✅
- **plugin ABI 版本**(§7.7 + Phase 1)✅

三项都补到位。决策记录从 7 条扩到 9 条、编号连续,Changelog 记录了 2026-06-28 条目,符合 `RULES.md`。

> **架构层面已无 Blocker。**

---

## 二、修改后仍需处理的点

### ⚠️ 1. §0.4、§8.4 与 §8.1 自相矛盾(最实在,建议进 Phase 0 前修)

冻结规则(§0.4、§8.4)都说:

> query-like daemon API:使用 `Result<T, Error>`

但 §8.1 的 `IInstanceQueryApi` 示例**全部用 nullable / 裸集合**,没有 `Result`:

```csharp
Task<IReadOnlyDictionary<Guid, InstanceReport>> GetReportsAsync(...);  // 无 Result
Task<InstanceReport?> GetReportAsync(...);                              // nullable
Task<IReadOnlyList<string>> GetLogAsync(...);                           // 无 Result
```

三处不一致。实现者会照 §8.1 抄,于是冻结规则形同虚设。需要二选一统一:

- 若"query 的 not-found 用 nullable 即可、只读不算错误"(合理设计),则**改 §0.4 / §8.4 措辞**为"query 默认 `Result<T,Error>`;纯 not-found 语义可用 nullable";
- 若坚持 `Result`,则 §8.1 示例要改成 `Task<Result<InstanceReport?, Error>>` 等。

建议前者——nullable 对 query 更自然,但必须在 §0.4 把规则写对。

### ⚠️ 2. notify fan-out adapter 在 Phase 清单里悬空

§13.3 正确地把 notify 拆成独立工程,并强调"不是给 `IEventService` 套一层接口就结束,而是要补齐真正的广播出口"。但翻遍 Phase:

- Phase 2 列了 management / storage / **serialization** adapter,**没有 notify adapter**;
- Phase 3 的 `INotifyRegistry` 是**注册面**(让插件登记 publish 能力),不是 §13.3 说的 `INotifyBus` fan-out **实现**。

也就是说,"把分散在 `IEventService` / `WsEventPlugin` / `WsContextContainer` / `EventTriggerService` 的 fan-out 收束为 `INotifyBus`"这块工作量,在 Phase 实施清单里**没有对应 task**。建议在 Phase 2 显式加一项 "notify / event fan-out adapter(`INotifyBus` 实现)",否则这块工程量会掉进 registry 与 V2 dispatcher 之间的夹缝。

### 3. §0.2 事实声明已核验(准确)+ 一点补充

`src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj` 证实:

- `PublishSingleFile=true`(`:13`)
- `EnableTrimAnalyzer=true`(`:14`)
- `PublishTrimled` 条件块(`:25-27`)
- `JsonSerializerIsReflectionEnabledByDefault=true`(`:16`)
- **无 `PublishAot` 也无 `IsAotCompatible`**

§0.2 的"当前 daemon 工程文件本身并未声明 `PublishAot=true`"**准确**。顺带两个 plan 没提的实现细节:

- **single-file 与动态加载的张力**:`PublishSingleFile=true`(`:13`)与 ALC 动态加载有额外张力——single-file 发布下外部插件 DLL 的依赖解析 / probing 路径有特殊行为,插件需作为 sidecar 而非打进包。建议 Phase 4 plugin host 显式关注,别当成免费能力。
- **反射默认开启的纪律**:`JsonSerializerIsReflectionEnabledByDefault=true`(`:16`)说明当前反射默认开启(正是 `ActionHandlerRegistryRuntime` 走 Legacy 路径的原因)。与 §12.5 要求 OpenRPC 走 source-gen 不冲突,但 plugin DTO 在反射默认开启时仍可能隐式走反射——§8.3 / §12.5 要求显式 `JsonTypeInfo` 的纪律要在 Phase 2 / 6 落实为强制,而非仅建议。

### 4. 小一致性:§7.4 映射缺 `serialization` 一行

§7.4 的 capability → API 映射列了 Query / Control / Factory / Store / Rpc / Notify / TouchSocket,**唯独没有 `ISerializationApi → serialization.json`**(而 §7.3 capability 组里 `serialization.json` 是存在的)。补一行即与其他 API 对齐。

---

## 三、结论

**可以进 Phase 0。** 架构决策已闭环,三个 Blocker 全部消解。

进 Phase 0 前建议顺手清掉的两处文字级问题(成本极低,但会直接影响实现者照抄):

1. 统一 §0.4 / §8.4 与 §8.1 的 query 结果风格;
2. Phase 2 补 notify fan-out adapter task,并在 §7.4 补 serialization 映射行。

这两点修完,plan 就是一份自洽、可直接驱动实施的设计。
