# Phase 7 Plugin Contracts And Dependency DAG Plan

Status: completed locally 2026-08-08. Manifest plugin dependencies, Contracts assembly declarations, deterministic dependency-DAG admission, direct typed provider/consumer calls, and plugin-facing typed event subscription are implemented with focused protocol/API/generator coverage.

## Goal

Enable startup plugins to depend on explicit shared Contracts assemblies and to use direct typed provider/consumer calls plus plugin-facing typed event subscription without adding plugin hot reload, runtime install, broad service registries, or RPC-based plugin-to-plugin fallback.

## Scope

- Add a versioned manifest dependency section without renaming or remeaning existing `package`, `entry`, `requires.api`, or `requires.features` fields.
- Validate plugin dependency DAGs deterministically during startup preflight before plugin IL activation.
- Define authoritative shared Contracts assembly rules so contract type identity is stable across plugin load contexts.
- Add provider/consumer contracts for direct typed calls only where a provider explicitly exports and a consumer explicitly requires the contract.
- Implement plugin-facing typed event subscription for `event.subscribe`, preserving current event missing/null/object semantics and source-generated `JsonTypeInfo` requirements.
- Keep plugins trusted, startup-only, non-hot-reloadable, and sidecar-based.

## Non-Goals

- No runtime plugin install, reload, hot unload, or dynamic dependency changes.
- No hooks, factory or installer extension points, daemon-root filesystem grants, or plugin write access beyond existing bounded surfaces.
- No root `IServiceProvider`, daemon implementation types, TouchSocket, MessagePipe, mutable runtime collections, or disposable daemon handles in Daemon API.
- No RPC-based plugin-to-plugin fallback or unused general service registry.

## Design Principles

- Manifest remains authoritative: dependency declarations drive ordering, admission, type sharing, and failure messages.
- Admission is atomic: a missing, cyclic, incompatible, or conflicting dependency skips the affected plugin set without blocking daemon startup.
- Shared contract identity is explicit: only declared Contracts assemblies are shared across dependent plugin load contexts.
- Provider calls are typed and local: consumers call declared provider interfaces, never raw method strings.
- Event subscription reuses the frozen typed event catalog semantics and must not reintroduce reflection serialization fallback.

## Work Packages

1. Manifest grammar and parser — **implemented**
   - Add a versioned `dependencies` section for plugin ids and contract packages.
   - Reject duplicate ids, self-dependencies, invalid ranges, and undeclared contract references.
   - Keep old manifests valid when no dependencies are declared.

2. Dependency DAG admission — **implemented for discovered/admitted plugin ids**
   - Build a deterministic plugin dependency graph after discovery and before lifecycle configure/start.
   - Detect cycles and missing providers with stable diagnostics.
   - Start providers before consumers and stop consumers before providers.

3. Contracts assembly sharing — **implemented**
   - Define package/assembly naming, version range, and digest rules for shared Contracts assemblies.
   - Load approved Contracts assemblies once per compatible contract identity and share them across participating plugin ALCs.
   - Reject private copies that conflict with declared shared contracts.

4. Direct typed provider/consumer calls — **implemented**
   - Add minimal Daemon API contracts for provider export and consumer import.
   - Generate feature-bag accessors only for declared imports/exports.
   - Ensure provider failure or missing activation prevents consumers from starting atomically.

5. Plugin-facing typed event subscription — **implemented**
   - Implement `event.subscribe` as the final FeatureCatalog entry required for Phase 7.
   - Preserve existing event data/meta field-presence semantics and source-generated metadata requirements.
   - Add shutdown cleanup so subscriptions never outlive plugin lifetime.

6. Tests, docs, and package gates — **implemented and verified locally**
   - Add parser/generator tests, daemon admission tests, protocol tests for event subscription, and published-host fixture coverage.
   - Update plugin developer guide, daemon manual, preview pin records if public package payloads change, and the active execute plan status.

## Acceptance Gates

- Daemon.ApiTests cover manifest dependency grammar, package contract identity, and API baselines.
- ProtocolTests cover DAG ordering, cycle/missing dependency skips, provider/consumer failure isolation, shared contract resolution, direct typed provider imports, and typed event subscription semantics.
- Plugin generator tests cover generated import/export/subscription surfaces and diagnostic failures.
- Published-host plugin integration proves the SDK package consumer still restores, publishes, and loads from a sidecar daemon publish.
- `event.subscribe` is implemented, no known FeatureCatalog entry remains unimplemented, and tests fail if future features land unimplemented silently.
- Existing gates remain green: daemon build, daemon client build, ProtocolTests, ProtocolDocs `--check`, no-V1 gate, package contract tests, and `git diff --check`.

## Current Verification

- `dotnet build MCServerLauncher.slnx -c Release /m:1` passed with 0 warnings / 0 errors.
- `dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release --no-build /m:1` passed 1413/1413.
- `dotnet test tests/MCServerLauncher.Daemon.ApiTests/MCServerLauncher.Daemon.ApiTests.csproj -c Release --no-build /m:1` passed 116/116.
- `dotnet test tests/MCServerLauncher.Daemon.ApiTests/MCServerLauncher.Daemon.ApiTests.csproj -c Release --filter FullyQualifiedName~PackageContract /m:1` passed 6/6.
- `dotnet test tests/MCServerLauncher.Daemon.Plugin.Generators.Tests/MCServerLauncher.Daemon.Plugin.Generators.Tests.csproj -c Release --no-build /m:1` passed 67/67.
- Published plugin integration against local `osx-arm64` daemon publish passed 4/4.
- `dotnet run --project tools/MCServerLauncher.ProtocolDocs/MCServerLauncher.ProtocolDocs.csproj -- --check` passed.
- `pwsh -File tools/VerifyNoV1Runtime.ps1` passed.
- `git diff --check` passed.

## Implementation Notes

- Package pin hashes were re-recorded after package contract tests proved deterministic reproduction.
- Keep all future plugin surfaces source-generated and `System.Text.Json`-metadata explicit.
