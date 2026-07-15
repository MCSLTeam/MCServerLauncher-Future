# MCServerLauncher Future Execute Plan

## Current Status

The active architecture initiative is `docs/superpowers/plans/2026-06-27-daemon-api-inprocess-plugin-v2-plan.md`. It replaces the earlier additive V1/V2 design with an application-core-first, release-atomic V2 cutover followed by a startup-only trusted plugin host.

The 2026-07-11 baseline is the V1 daemon at commit `925666a4`: Release protocol tests pass 382/382 after adding migration-only raw-binary, restart, file-transfer, file-operation, notification-producer, and subscription/reconnect characterization. The working tree also contains user-owned governance, review, harness, plan, and local-tool files; implementation must preserve those changes.

Phase 0, Phase 1, Phase 2, Phase 3, the Phase 4A daemon V2 sub-exit, and the Phase 4B daemon-client/WPF sub-exit are complete and independently reviewed. The independent Sol max Phase 4A and Phase 4B final reviews each have 0 open P0/P1/P2 findings. Phase 1 established the packable Daemon API boundary, complete four-domain contracts and immutable Common DTOs, typed identifiers, published state, immutable instance snapshots, exact package dependency locks, textual API baselines, and Common Serilog removal. Its acceptance passed 42/42 API tests, a clean Release solution build, Release protocol tests at 382/382 (including `--no-build`), and 0 B/op for both published-state read paths.

Phase 2 established the local application core, authoritative instance-state commit points, the shared file-session coordinator, immutable domain-event adapters, and removal of runtime config side channels. Acceptance passed the focused fixed-tree suite at 73/73 twice, Daemon.API tests at 42/42, Release protocol tests at 442/442, the Release `--no-build` protocol gate at 442/442, Daemon.API/daemon/full-solution Release builds at 0 warnings / 0 errors, and `git diff --check`, with no update/upload staging residue. The generated-docs check is not applicable at Phase 2 because `tools/MCServerLauncher.ProtocolDocs` is a Phase 3 create target and is absent from both HEAD and the current tree.

Phase 3 established the 38-RPC/4-event frozen catalog with explicit `JsonTypeInfo` and application bindings; generated deterministic runtime OpenRPC and checked-in Apifox; daemon-internal MessagePipe dispatch; ordered catalog publication with admission/drain ownership; runtime composition and `rpc.discover` extension coverage; and catalog/event fan-out benchmarks. These are V2 dispatch-ready bindings/metadata; live V1 dispatch remains through the Phase 4 deletion gate. Acceptance passed Daemon.ApiTests 56/56, Release ProtocolTests 548/548 and the Release `--no-build` gate at 548/548, the recorded acceptance invocations of Daemon.API/daemon/daemon-client/full-solution Release builds each at 0 warnings / 0 errors, ProtocolDocs `--check` at catalog hash `51e351a9...`, and `git diff --check`. This does not claim the repository is warning-free: the pre-existing ProtocolTests source `CS0105` duplicate-using warning is visible on rebuild, was not introduced by Phase 3, and remains separately tracked. The benchmark project builds with seven Phase 3 methods; its implementation-time ShortRun is not a final performance gate.

Phase 4A completed the daemon JSON-RPC profile/dispatcher/error mapping, typed remote event subscriptions with the bounded 256-message connection queue and serialization-once fan-out, versioned binary sessions with connection ownership and deterministic cleanup, and production TouchSocket composition. Across the 11 commits `b394957a^..98f07298`, final acceptance passed Daemon.ApiTests 56/56, real TouchSocket host 1/1, Release ProtocolTests and final `--no-build` 782/782, daemon/full-solution/benchmark Release builds at 0 warnings / 0 errors, ProtocolDocs `--check` at hash `74268afdb2bb3dd9be54cc1edf92017f581d305270f6be201f2c033eb2f4b44f`, and `git diff --check`; independent Sol max final review reported 0 open P0/P1/P2. The pre-existing ProtocolTests `CS0105` warning remains separately tracked, and paired V2/V1 performance validation remains a Phase 6 residual.

Phase 4B completed the daemon-client remote application implementation, typed subscriptions, binary sessions, reconnect/catalog reconciliation, and immutable remote instance mirror, plus the WPF connection-layer consumer cutover to typed application/event APIs and localized errors. Final independent Sol max review reported `P0=0 / P1=0 / P2=0`. Acceptance passed WPF tests 19/19, Release ProtocolTests `--no-build` 1105/1105, DaemonClient/WPF/full-solution Release builds at 0 warnings / 0 errors, and `git diff --check`; the WPF V1/raw-symbol search was clean except for `EventRuleEditorDialog`'s UI/domain string property `ActionType`. This does not claim an unperformed manual UI smoke test or runtime end-to-end UI exercise.

Current status: Phase 4C is active. M1, M2a, and M2b are complete and independently reviewed with no open P0/P1/P2 findings. M2b closed the non-V1 legacy error migration and hardened replacement-installer transactions against partial output, rollback failure, reparse-point traversal, and Windows case-alias ambiguity. Phase 4C and Phase 4 overall remain incomplete and unreleasable. Residual V1 daemon/Common/client/generator source is intentionally retained for M3-M4, and the release-atomic scope and sequencing remain unchanged.

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

Progress: Phase 4A daemon V2 and Phase 4B daemon-client/WPF connection-layer cutover are complete and independently reviewed with 0 open P0/P1/P2 findings. Phase 4C M1 sole `/api/v2` authority, M2a canonical DTO/persistence-boundary migration, and M2b legacy error migration are complete and independently reviewed. M2b acceptance passed the expanded focused slice at 133/133, Release ProtocolTests twice at 1196/1196, WPF.Tests at 20/20, the full Release solution build at 0 warnings / 0 errors, ProtocolDocs `--check`, the allowlisted residual search, and `git diff --check`; independent Sol max closure review reported `P0=0 / P1=0 / P2=0`. M3-M4 retain the V1 daemon/Common/client/generator physical deletion work. Phase 4C and Phase 4 overall remain incomplete and unreleasable.

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
