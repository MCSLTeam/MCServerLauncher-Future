# PR #52 Review Fixes Implementation Plan

> **For agentic workers:** Execute task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Local commits only; push once at end.

**Goal:** Clear PR #52 merge blockers and high-leverage Should items from `docs/review/2026-07-16-pr-52-v2-cutover-review.md` with atomic local commits.

**Architecture:** Keep frozen catalog as runtime truth. Apifox HTTP serves full frozen catalog (built-in + plugins). Checked-in Apifox remains built-in baseline for CI. File sessions become host-DI singletons. Download session limits are per-connection counts. Path safety rejects reparse points and re-checks final path after open.

**Tech Stack:** .NET 10 / C# 14, System.Text.Json source-gen, xUnit ProtocolTests, conventional commits.

**Delivery constraints (locked with user):**
- Scope: Must-fix + high-leverage Should
- Local commits only until full suite green; then one push
- Commit slicing by review theme

---

## Locked decisions

| Topic | Decision |
|---|---|
| Scope | Must + high-leverage Should |
| Apifox | Generator accepts frozen catalog; HTTP dynamically serves plugin-inclusive Apifox; checked-in file stays built-in for CI |
| Download limit | Per-connection open download session count (not per-path). Config key remains `FileDownloadSessions` |
| FileSessionCoordinator | Remove `Shared`; host DI Singleton; ctor inject limit; no business-path `new` |
| Path TOCTOU | Reject reparse during validate; after open re-check `GetFinalPathNameByHandle` stays under root |
| STJ | `JsonSerializerIsReflectionEnabledByDefault=false`; `PluginProtocol` rejects non-source-gen `JsonTypeInfo` |
| Commits | Multiple atomic conventional commits |
| Push | Only after all work + ProtocolTests green |

---

## File map

| Area | Primary files |
|---|---|
| CI red tests | `OwnedTaskSupervisor.cs`, `V2ClientConnectionCore.cs` (+ download close path), related tests |
| Download limit | `V2FileSessionConnection.cs`, `FileSessionCoordinator.cs`, tests |
| Coordinator DI | `DaemonServiceComposition.cs`, `FileSessionCoordinator.cs`, composition tests |
| Path safety | `FileSessionCoordinator.cs`, path validation tests |
| Apifox dynamic | `ApifoxProjectGenerator.cs`, `EmbeddedDocumentation` / HTTP plugin / new generator entry from catalog, console optional, docs topics |
| STJ / plugin | `MCServerLauncher.Daemon.csproj`, `PluginProtocol.cs`, plugin tests |
| Perf | `V2EventSubscriptionLedger.cs`, `V2RemoteEventBridge.cs`, `V2RequestWriter.cs` |
| Dedupe | shared `DaemonErrorWireKind` mapper; catalog item mapper if cheap |

---

## Commit sequence

1. `docs(plan): record pr 52 review fix plan`
2. `fix(events): observe cancel-callback failures in OwnedTaskSupervisor`
3. `fix(client): unblock close during blocking download send`
4. `fix(storage): enforce per-connection download session limits`
5. `refactor(storage): host-DI FileSessionCoordinator singleton`
6. `fix(storage): harden path validation against reparse TOCTOU`
7. `feat(docs): serve Apifox from frozen runtime catalog`
8. `fix(daemon): disable STJ reflection and constrain plugin JsonTypeInfo`
9. `perf(protocol): cut event match and request-writer allocations`
10. `refactor(protocol): dedupe error wire-kind mapping`
11. Final verification commit only if docs/review notes need update

---

### Task 1: Plan artifact

**Files:**
- Create: `docs/superpowers/plans/2026-07-16-pr-52-review-fixes.md` (this file)

- [x] **Step 1: Write plan**
- [ ] **Step 2: Commit** `docs(plan): record pr 52 review fix plan`

---

### Task 2: OwnedTaskSupervisor cancel-callback observability

**Files:**
- Modify: `src/MCServerLauncher.Daemon/Application/Events/OwnedTaskSupervisor.cs`
- Test: `tests/MCServerLauncher.ProtocolTests/Rpc/DomainEventPortAndTriggerTests.cs`

**Bug:** `OwnedTaskSupervisor_CancellationCallbackFailureIsObservedAfterTaskDrain` expects `DisposeAsync`/`DrainAsync` to throw `AggregateException` when a cancel callback throws; currently no exception.

**Fix direction:**
- Ensure `CancelAsync` / cancel path surfaces callback exceptions into stop completion or drain failures (as test asserts: AggregateException with nested AggregateException + error log).
- Prefer observing exceptions from `CancellationTokenSource.Cancel` / linked registration without swallowing in `CompleteStopAsync`.

- [ ] **Step 1: Reproduce failing test locally**
- [ ] **Step 2: Fix observation path**
- [ ] **Step 3: Pass targeted test + nearby supervisor tests**
- [ ] **Step 4: Commit** `fix(events): observe cancel-callback failures in OwnedTaskSupervisor`

---

### Task 3: Blocking download send must not block Close

**Files:**
- Modify: `src/MCServerLauncher.DaemonClient/Connection/V2/V2ClientConnectionCore.cs`
- Possibly: download coordinator close/fail-send path
- Test: `tests/.../V2ClientConnectionCoreTests.BlockingDownloadTextSendCannotBlockCloseAndCancellationDrainsReservation`

**Bug:** `Close()` times out while `SendTextAsync` blocks on `WaitHandle` until cancellation; cancel is not observed in time or Close waits on send.

**Fix direction:**
- Close must cancel connection token without waiting for in-flight send completion.
- Send observer must complete on cancel and drain reservation (`WaitForSendObserversAsync` returns).
- Keep Close reentrancy/idempotence invariants of existing tests.

- [ ] **Step 1: Reproduce failing test**
- [ ] **Step 2: Fix close/cancel ordering vs send observe**
- [ ] **Step 3: Pass download close tests**
- [ ] **Step 4: Commit** `fix(client): unblock close during blocking download send`

---

### Task 4: Per-connection download session limit

**Files:**
- Modify: `V2FileSessionConnection.cs` (primary enforcement)
- Modify: `FileSessionCoordinator.cs` (remove incorrect per-path limit counting)
- Config: keep `AppConfig.FileDownloadSessions` meaning = max open download sessions **per connection**
- Tests: coordinator + connection/file session tests

**Semantics:**
- Two concurrent downloads of different paths => 2 sessions.
- Limit N means one connection may hold at most N open download leases.
- On exceed: `ConflictDaemonError("file.download.limit", ...)`.
- If coordinator still has a limit field, either remove or repurpose carefully; preferred: pass limit into connection admission, coordinator no longer path-counts.

- [ ] **Step 1: Add/adjust tests for multi-path same connection**
- [ ] **Step 2: Implement per-connection count in Admit/OpenDownload**
- [ ] **Step 3: Remove per-path Count in coordinator**
- [ ] **Step 4: Commit** `fix(storage): enforce per-connection download session limits`

---

### Task 5: Host-DI FileSessionCoordinator

**Files:**
- Modify: `FileSessionCoordinator.cs` (delete `Shared`; ctor takes limit)
- Modify: `DaemonServiceComposition.cs` (DI register factory singleton)
- Modify: composition tests asserting Shared

**Pattern:**
```csharp
services/a.RegisterSingleton factory → construct FileSessionCoordinator(
  timeProvider: ...,
  downloadSessionLimit: AppConfig.Get().FileDownloadSessions)
```
No process-static. One instance per host container.

- [ ] **Step 1: Update type + registration**
- [ ] **Step 2: Fix tests**
- [ ] **Step 3: Commit** `refactor(storage): host-DI FileSessionCoordinator singleton`

---

### Task 6: Path reparse / final-path checks

**Files:**
- Modify: `FileSessionCoordinator.ResolveAndValidatePath` and open paths for download/upload/file ops
- Tests: existing reparse tests + open-after-swap if feasible

**Approach:**
1. During validation, reject path components that are reparse points (existing partial coverage).
2. After opening `FileStream`/`SafeFileHandle`, call final path API and ensure still under configured root; else fail closed and dispose handle.

- [ ] **Step 1: Implement final-path recheck helper**
- [ ] **Step 2: Apply on download open (minimum); extend to upload/file ops where same risk**
- [ ] **Step 3: Tests**
- [ ] **Step 4: Commit** `fix(storage): harden path validation against reparse TOCTOU`

---

### Task 7: Dynamic Apifox from frozen catalog

**Files:**
- Refactor: `tools/.../ApifoxProjectGenerator.cs` to accept document + descriptors (or catalog view)
- Daemon: generate Apifox bytes from `FrozenProtocolCatalog` at request or cache on freeze
- HTTP: `/apifox.json` serves runtime catalog-based Apifox (includes plugins)
- CI tool: still generates built-in-only checked-in artifact for hash check
- Docs topics: clarify runtime vs checked-in baseline

**Implementation sketch:**
```csharp
// Shared API
ApifoxProjectGenerator.Generate(OpenRpcDocument document, ImmutableArray<RpcDescriptor> rpcs)

// Runtime
var catalog = accessor.GetRequired();
var bytes = ApifoxProjectGenerator.Generate(catalog.Document, catalog.RpcDefinitions);

// Tools/CI
Generate(BuiltInProtocolDefinitions document/rpcs)
```

Optional console `apifox gen [path]` if cheap after HTTP path exists (nice; include if small).

- [ ] **Step 1: Refactor generator input**
- [ ] **Step 2: Wire HTTP dynamic serve**
- [ ] **Step 3: Keep ProtocolDocs --check on built-in**
- [ ] **Step 4: Update protocol topic wording**
- [ ] **Step 5: Commit** `feat(docs): serve Apifox from frozen runtime catalog`

---

### Task 8: STJ reflection off + plugin TypeInfo origin

**Files:**
- `MCServerLauncher.Daemon.csproj`: `JsonSerializerIsReflectionEnabledByDefault=false`
- `PluginProtocol.cs`: reject TypeInfo not from source-gen context
- Tests / fixtures that relied on reflection metadata

**Detection approach:** reject when originating resolver is `DefaultJsonTypeInfoResolver` or TypeInfo was created without a `JsonSerializerContext` parent; accept TypeInfo from generated contexts.

- [ ] **Step 1: Flip property**
- [ ] **Step 2: Harden PluginProtocol**
- [ ] **Step 3: Fix any red builds/tests**
- [ ] **Step 4: Commit** `fix(daemon): disable STJ reflection and constrain plugin JsonTypeInfo`

---

### Task 9: Performance Should items

**Files:**
- `V2EventSubscriptionLedger.Matches` — avoid per-call SubscriptionKey allocations
- `V2RemoteEventBridge.Publish` — reduce Snapshot alloc churn where safe
- `V2RequestWriter` — single buffer → ImmutableArray ownership transfer
- Upload CTS link short-circuit if same token (if still present)

- [ ] **Step 1: Writer double-copy fix**
- [ ] **Step 2: Event match allocation fix**
- [ ] **Step 3: Targeted benchmarks optional; ProtocolTests must stay green**
- [ ] **Step 4: Commit** `perf(protocol): cut event match and request-writer allocations`

---

### Task 10: Deduplicate ToWireKind

**Files:**
- Extract internal helper used by `V2RpcDispatcher` and `V2InboundMessagePipeline`
- Optional: reuse `BuiltInApplicationRpcExecution` where local ToExecution duplicates

- [ ] **Step 1: Extract mapper**
- [ ] **Step 2: Commit** `refactor(protocol): dedupe error wire-kind mapping`

---

### Task 11: Verification

```bash
dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj /m:1
dotnet build src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj /m:1
dotnet build src/MCServerLauncher.DaemonClient/MCServerLauncher.DaemonClient.csproj /m:1
# if docs tooling changed:
dotnet run --project tools/MCServerLauncher.ProtocolDocs/MCServerLauncher.ProtocolDocs.csproj -- --check
git status --short --branch
```

- [ ] **Step 1: Full ProtocolTests green**
- [ ] **Step 2: No uncommitted must-have changes**
- [ ] **Step 3: Ask user before `git push`**

---

## Out of scope (explicit)

- Plugin ALC collectible unload
- Remote-only RestartInstanceAsync contract unification
- Full handle-only filesystem rewrite
- Seven-RID release run
- WPF UI E2E smoke
- Push to remote before suite green

---

## Changelog

- 2026-07-16: Create plan from PR #52 review grill-me decisions.
