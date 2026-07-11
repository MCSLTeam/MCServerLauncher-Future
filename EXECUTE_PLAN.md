# MCServerLauncher Future Execute Plan

## Current Status

The active architecture initiative is `docs/superpowers/plans/2026-06-27-daemon-api-inprocess-plugin-v2-plan.md`. It replaces the earlier additive V1/V2 design with an application-core-first, release-atomic V2 cutover followed by a startup-only trusted plugin host.

The 2026-07-11 baseline is the V1 daemon at commit `925666a4`: Release protocol tests pass 382/382 after adding migration-only raw-binary, restart, file-transfer, file-operation, notification-producer, and subscription/reconnect characterization. The working tree also contains user-owned governance, review, harness, plan, and local-tool files; implementation must preserve those changes.

Phase 0, Phase 1, and Phase 2 are complete and independently reviewed. Independent Sol review has 0 open P0/P1 findings. Phase 1 established the packable Daemon API boundary, complete four-domain contracts and immutable Common DTOs, typed identifiers, published state, immutable instance snapshots, exact package dependency locks, textual API baselines, and Common Serilog removal. Its acceptance passed 42/42 API tests, a clean Release solution build, Release protocol tests at 382/382 (including `--no-build`), and 0 B/op for both published-state read paths.

Phase 2 established the local application core, authoritative instance-state commit points, the shared file-session coordinator, immutable domain-event adapters, and removal of runtime config side channels. Acceptance passed the focused fixed-tree suite at 73/73 twice, Daemon.API tests at 42/42, Release protocol tests at 442/442, the Release `--no-build` protocol gate at 442/442, Daemon.API/daemon/full-solution Release builds at 0 warnings / 0 errors, and `git diff --check`, with no update/upload staging residue. The generated-docs check is not applicable at Phase 2 because `tools/MCServerLauncher.ProtocolDocs` is a Phase 3 create target and is absent from both HEAD and the current tree.

Execution is intentionally paused before Phase 3 for a runtime restart. Phase 3 has not started; resume at the frozen catalog, typed events, and generated docs work described below.

## Phase 0: Governance, Characterization, And Deletion Inventory

Goal: make every existing V1 behavior, migration owner, test, benchmark, and deletion target explicit before implementation changes behavior.

Exit criteria:

- Governance docs describe Daemon API, one application core, `/api/v2`, generated protocol docs, and the untrimmed JIT plugin-host product boundary.
- The checked-in parity inventory maps every action, event, binary-transfer path, permission, cancellation rule, and missing/null semantic to a V2 owner and test.
- Release protocol tests pass and the V1 benchmark baseline is recorded.
- The deletion manifest covers Common, daemon, daemon client, WPF, source generator, tests, benchmarks, and embedded docs.
- No architecture question remains for an implementer to guess.

## Phase 1: Daemon API, Errors, And Published State

Goal: establish the packable transport-neutral contract boundary and immutable state primitives.

Exit criteria:

- `MCServerLauncher.Daemon.API` targets `net10.0`, packs successfully, and exposes no daemon, transport, MessagePipe, Serilog, mutable-collection, disposable-handle, or root-DI types.
- Public application methods use `Task<Result<T, DaemonError>>`, place `CancellationToken` last, and preserve normal cancellation.
- `StatePublisher<T>` and `PublishedState<T>` pass monotonic-version, retained-history, concurrency, and steady-state zero-allocation tests.
- Immutable instance snapshots contain only public facts.
- Common no longer has a Serilog package or static logging dependency.

## Phase 2: Local Application Core

Goal: make daemon behavior exist once behind the application services.

Exit criteria:

- Instance, file, system, and event-rule services own the current behavior and persistence commit points.
- Console, event-rule execution, and later transport bindings delegate to the application services.
- `KillInstance(Guid)` keeps its frozen void-signal semantics.
- Instance catalog snapshots update only after authoritative commits; performance/log/player activity does not trigger catalog copy-on-write.
- The runtime instance filesystem watcher no longer mutates the catalog.

## Phase 3: Frozen Catalog, Typed Events, And Generated Docs

Goal: create one typed definition source for dispatch, local events, runtime discovery, and checked-in docs.

Exit criteria:

- Built-in RPC/event definitions include explicit `JsonTypeInfo`, permissions, and application bindings.
- Startup registration freezes into immutable lookup tables and rejects post-freeze mutation.
- MessagePipe 1.8.2 is daemon-internal and wrapped by project-owned event interfaces with the frozen ordering, cancellation, error, and ownership semantics.
- Runtime OpenRPC and checked-in Apifox are deterministic outputs of the same definitions; `--check` passes.
- Catalog, local-event, and serialization-once benchmarks exist.

## Phase 4: V2 Transport And Release-Atomic Cutover

Goal: make `/api/v2` the sole runtime protocol while preserving the complete V1 behavior inventory.

Exit criteria:

- JSON-RPC profile, typed remote events, bounded per-connection writer, slow-consumer handling, immutable catalog mirror, and versioned binary sessions pass the transport matrix.
- Daemon client remotely implements the application contracts and typed subscriptions; WPF uses only the typed API and localized error mapping.
- `/api/v1`, V1 Common envelopes/enums, daemon action/event runtime, daemon-client V1 API/parser/caches, WPF V1 call sites, and the V1 source generator are deleted.
- `tools/VerifyNoV1Runtime.ps1`, the full solution build, protocol tests, and inventory searches pass.

## Phase 5: Startup Plugin Host

Goal: prove the public API with real external-style plugins without expanding the first capability surface.

Exit criteria:

- Manifest, version-range, capability, duplicate-id, dependency/reference, namespace, and catalog-conflict checks are deterministic.
- Per-plugin non-collectible load contexts preserve shared contract type identity.
- Configure/start/commit/activate and reverse-stop are transactional; returned errors and exceptions log and skip without blocking daemon startup.
- The health plugin proves typed RPC, immutable instance query, typed event publication, and explicit source-generated metadata.
- Returned-error and throwing fixtures prove failure isolation against a published single-file daemon host.

## Phase 6: Packaging, Performance, And Public Documentation

Goal: align release artifacts, NuGet, docs, and measured behavior with the implementation.

Exit criteria:

- Daemon API package validation and dependency-graph checks pass with an approved API baseline.
- Published daemon + sidecar plugin integration tests pass on supported RIDs without a trimmed/Native-AOT claim.
- Equivalent V2 dispatch mean and allocation remain within the approved 25% baseline gate; state reads are 0 B/op and remote event payloads serialize once per publish.
- README files, daemon manual, plugin developer guide, license inventory, plan status, and changelog match the released behavior.
- `git diff --check` and final status review pass.

## Mandatory Follow-Up: Plugin Contracts And Dependency DAG

After the first plugin milestone is accepted, create a separate plan for manifest dependency DAGs, authoritative shared Contracts assemblies, direct typed provider/consumer calls, and plugin-facing typed event subscription. Do not prebuild unused service registries or RPC-based plugin-to-plugin fallback in the first milestone.

## Verification Expectations

- Daemon API: `dotnet build src/MCServerLauncher.Daemon.API/MCServerLauncher.Daemon.API.csproj /m:1`.
- Daemon: `dotnet build src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj /m:1`.
- Daemon client: `dotnet build src/MCServerLauncher.DaemonClient/MCServerLauncher.DaemonClient.csproj /m:1`.
- WPF: `dotnet build src/MCServerLauncher.WPF/MCServerLauncher.WPF.csproj /m:1`.
- Protocol: `dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release /m:1`.
- Commit gate: `dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release --no-build /m:1`.
- Generated docs: `dotnet run --project tools/MCServerLauncher.ProtocolDocs/MCServerLauncher.ProtocolDocs.csproj -- --check`.
- Plugin integration: publish the daemon and fixtures, set `MCSL_PUBLISHED_DAEMON`, then run `dotnet test tests/MCServerLauncher.PluginIntegrationTests/MCServerLauncher.PluginIntegrationTests.csproj -c Release /m:1`.
- V1 deletion: `pwsh -File tools/VerifyNoV1Runtime.ps1`.
- Benchmarks: `dotnet run --project benchmarks/MCServerLauncher.Benchmarks/MCServerLauncher.Benchmarks.csproj -c Release -- --exporters json` plus the performance gate.
- Full solution: `dotnet build MCServerLauncher.sln /m:1`.
- Final hygiene: `git diff --check` and `git status --short --branch`.

## Near-Term Backlog

- Complete the active Daemon API / V2 / startup-plugin plan through independent review and acceptance.
- Then create the mandatory plugin Contracts/dependency-DAG plan.
- Localize detailed create-instance validation messages instead of reusing generic missing-data text.
- Recreate or replace `TASKS.md` only if the team still wants a second lightweight checklist.
