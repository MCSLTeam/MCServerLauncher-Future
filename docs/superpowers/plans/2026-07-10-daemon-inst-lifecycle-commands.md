# Daemon `inst` Lifecycle Commands Implementation Plan

> **Current authority:** The user's 2026-07-10 follow-up explicitly supersedes the earlier `TryKillInstance` proposal. The current design keeps the special fire-and-forget `void KillInstance(Guid)` API and adds no result-bearing kill API.
>
> **Touched areas:** `backend`, `tests`, `docs`.

## Goal

Add daemon-local console commands:

```text
inst list
inst start <instance_name|uuid>
inst stop <instance_name|uuid>
inst halt <instance_name|uuid>
```

`start` and `stop` use their existing result-bearing manager APIs. `halt` sends the existing force-stop signal through `KillInstance` and does not ask the manager for an acknowledgement.

## User-Overridden Design

1. Do **not** add `bool TryKillInstance(Guid)` to `IInstanceManager` or `InstanceManager`.
2. `inst stop` calls only `TryStopInstance(Guid)` and maps its `bool` to command result `0/1`.
3. `inst halt` calls only `void KillInstance(Guid)` once a target is resolved.
4. A successful `halt` command means the force-stop signal call returned without exception; it does not mean the command received a process-state acknowledgement.
5. `KillInstance` is intentionally special compared with other manager methods: missing instance or missing process is a no-op, while an existing process is killed directly.
6. `KillInstance` resolves from `Instances`, not `RunningInstances`, so `stop` followed by `halt` can still reach a gracefully stopping Minecraft process.
7. `KillInstance` reads `instance.Process` once, uses null-conditional `KillProcess()`, performs no status/exit pre-check, and adds no catch.

## Evidence Baseline

- `inst list` exists in `src/MCServerLauncher.Daemon/Console/Commands/InstanceCommand.cs`.
- Console dispatch is synchronous in `src/MCServerLauncher.Daemon/Console/ConsoleApplication.cs`.
- `TryStopInstance` removes from `RunningInstances` before calling polymorphic `Stop()` in `src/MCServerLauncher.Daemon/Management/InstanceManager.cs:150-155`.
- Minecraft `Stop()` writes `stop` to stdin, so the process may remain alive after removal from `RunningInstances`.
- The existing `KillInstance(Guid)` API is already used by RPC/event paths and returns `void`.
- `InstanceProcess.KillProcess()` performs the force kill and wait; its exceptions remain transparent.

## Command Contract

### Target resolution

Use one `manager.Instances.ToArray()` snapshot:

1. If the token parses as an existing dictionary-key UUID, that UUID wins.
2. A UUID-shaped token whose UUID is absent falls back to name matching.
3. Names use `StringComparison.OrdinalIgnoreCase`.
4. Multiple name matches are ambiguous, list snapshot keys sorted by UUID, invoke no lifecycle method, and return `1`.
5. One match supplies the snapshot dictionary key.
6. Zero matches call `SendError` and return `1`.
7. Names containing spaces require Brigadier quoting.
8. Use terminal `Arguments.String()`; trailing tokens remain syntax errors.

Keep resolution as a small pure `internal` helper for tests.

### `start`

- Resolve `GracefulShutdown` from `ConsoleCommandSource`.
- Call `TryStartInstance(id, shutdown.CancellationToken).GetAwaiter().GetResult()`.
- Non-null result: feedback and `0`.
- Null result: `SendError` and `1`.
- Cancellation or other exceptions propagate to the existing Console exception path.

### `stop`

- Call only `TryStopInstance(id)`.
- `true`: feedback explicitly says stop was requested, return `0`.
- `false`: `SendError`, return `1`.

### `halt`

- Call only `KillInstance(id)`.
- Do not inspect `RunningInstances`, `Status`, `Process`, or exit state in Console code.
- If the call returns normally, feedback says the force-stop signal was sent and return `0`.
- Any exception propagates; there is no business-failure acknowledgement from the manager.

## Manager Contract

Retain the interface unchanged:

```csharp
void KillInstance(Guid instanceId);
```

Implementation:

```text
if Instances does not contain id: return
capture instance.Process once
capturedProcess?.KillProcess()
```

No `Try` API, no return value, no `RunningInstances` lookup, no status/HasExit check, no exception filter, and no new service/dependency.

## Non-goals

- No restart, alias, wildcard, batch command, or selector UI.
- No pending-stop gate, run generation, lease, or lifecycle state-machine redesign.
- No RPC/event response-shape, shared protocol, daemon-client, WPF, configuration, or dependency change.
- No change to `TryStopInstance`, `StopAllInstances`, or `SendToInstance`.

## Tests

### Console

Create/update `tests/MCServerLauncher.ProtocolTests/Console/InstanceCommandTests.cs` to cover:

- UUID priority, UUID-shaped name fallback, case-insensitive names, duplicate ambiguity, quoted names, missing/trailing arguments, and usage output.
- `start` waiting, shutdown-token identity, null result, and cancellation propagation.
- `stop` calls only `TryStopInstance` and maps `true/false` to `0/1`.
- `halt` calls only `KillInstance`, returns `0` when the void call returns, propagates exceptions, and still calls it when `RunningInstances` is empty.

### Manager

Create/update `tests/MCServerLauncher.ProtocolTests/Management/InstanceManagerKillTests.cs` to cover:

- A controlled live process remains killable after `TryStopInstance` removed its running entry.
- `Stopped` and `Crashed` labels do not block force kill.
- Missing instance and null process are no-ops.
- `Process` getter is read exactly once.
- Process failures propagate.
- Reflection confirms `IInstanceManager` exposes no `TryKillInstance` method.

Use bounded process waits, `try/finally`, and no arbitrary sleep.

## Acceptance Criteria

1. Real Brigadier dispatch supports `inst list|start|stop|halt` with exactly one lifecycle target.
2. Target resolution follows the frozen snapshot/UUID/name/ambiguity rules.
3. `start` waits and passes the daemon shutdown token.
4. `stop` preserves `TryStopInstance` semantics and request wording.
5. `halt` invokes `KillInstance` exactly once and treats normal void return as command success.
6. `KillInstance` uses `Instances`, captures `Process` once, performs no status/exit check, and no-ops when instance/process is absent.
7. Sequential `TryStopInstance` then `KillInstance` terminates a controlled still-live process.
8. No result-bearing kill API exists.
9. RPC/event/shared protocol source remains unchanged.
10. Focused tests, daemon build, full Release protocol tests, and diff hygiene pass without new warnings.

## Verification

```powershell
dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj /m:1 --filter "FullyQualifiedName~InstanceManagerKillTests|FullyQualifiedName~InstanceCommandTests"

dotnet build src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj /m:1

dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release /m:1

dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release --no-build /m:1

git diff --check
git status --short --branch
```

## ADR

### Decision

Keep `KillInstance(Guid)` as a `void` force-stop signal API. Make it resolve from `Instances` so it remains usable after graceful stop removes the running entry. Console halt sends the signal and does not request an acknowledgement.

### Drivers

- The user explicitly wants halt to behave like forced power-off rather than a `Try` query.
- Sequential stop-then-halt must work.
- The task should remain small and preserve existing RPC/event API shape.

### Rejected

- Adding `TryKillInstance(Guid)`: explicitly rejected by the user.
- Returning process-state acknowledgement from halt: conflicts with signal semantics.
- Full lifecycle generation/linearization redesign: outside this task.

### Consequences

- Halt command success means dispatch completed, not that a process existed.
- Missing instance/process inside `KillInstance` remains a no-op.
- Real kill failures still throw.
- Existing lifecycle concurrency limitations remain.

## Changelog

- 2026-07-10: Initial consensus plan proposed a result-bearing kill API.
- 2026-07-10: User explicitly superseded that proposal: retain `void KillInstance`, use it directly for halt, and treat it as a force-stop signal with no acknowledgement.
- 2026-07-10: Implementation and tests updated to the user-overridden lightweight contract.
- 2026-07-10: Central review removed the sub-agent's stale result-bearing kill implementation and verified the final void-signal contract.
- 2026-07-10: Focused command/manager tests passed 22/22; daemon build passed with 0 warnings; full Release protocol tests and the no-build commit gate passed 366/366. Existing protocol-test warnings remain CS0105 in `RpcGoldenCharacterizationTests.cs` and CS0067 in `InstanceSettingsCoordinatorTests.cs`.
