# MCServerLauncher-Future Agent Guide

Guide for agents working on MCServerLauncher-Future.

## Fast Start

1. Read `PROJECT_PLAN.md` for product direction, architecture, scope, vocabulary, and fixed invariants.
2. Read `RULES.md`; declare touched areas before code, runnable configuration, or project-rule changes.
3. Read `EXECUTE_PLAN.md` when changing phase work, backlog, or task priority.
4. Use `CLAUDE.md` as the compact index when context is tight.
5. Use `skills/mcsl-future/SKILL.md` for daemon protocol, instance lifecycle, file/path handling, installer, serialization, WPF create-instance submission, or event-semantics work.
6. Read `harness.md` for agent runtime workflow requirements (e.g. verify against `microsoft-docs` skills/MCP before writing any .NET / Microsoft code).

Common touched areas: `docs`, `agent-docs`, `frontend`, `backend`, `protocol`, `serialization`, `installer`, `storage`, `tests`, `benchmarks`, `workflow`, and `integrations`.

## Project Overview

MCServerLauncher-Future is a C#/.NET application suite for managing Minecraft servers and general console applications. It contains a Windows WPF client, a daemon service, a daemon client library, shared protocol contracts, source generators, protocol tests, and benchmarks.

- Repository: `MCServerLauncher-Future`
- Language/runtime: C# 14, .NET 10.0
- UI framework: WPF with `iNKORE.UI.WPF.Modern`
- Architecture: WPF client uses the daemon client remote application implementation over the sole `/api/v2` TouchSocket WebSocket JSON-RPC endpoint plus versioned binary frames
- License: GPLv3
- Team: MCSLTeam
- Copyright: 2022-2026 MCSLTeam

## Source Of Truth

- `PROJECT_PLAN.md`: product purpose, audience, scope, architecture, vocabulary, and invariants.
- `RULES.md`: activation-scoped implementation rules.
- `EXECUTE_PLAN.md`: phases, exit criteria, cross-phase dependencies, and near-term backlog.
- `docs/superpowers/plans/`: plans for architecture, rules, or substantial implementation work.
- `harness.md`: agent-agnostic runtime workflow requirements (verify against `microsoft-docs` before writing .NET/Microsoft code, etc.).
- `src/MCServerLauncher.Daemon/.Resources/Docs/`: daemon-facing protocol and manual documentation.
- `README.md` and `README_ZH.md`: public-facing introduction; verify claims against project files before carrying them into governance docs.

## Essential Commands

Use `/m:1` for WPF and other build checks when the task does not require full parallelism.

```bash
# Build the full solution
dotnet build MCServerLauncher.slnx /m:1

# Build major projects
dotnet build src/MCServerLauncher.WPF/MCServerLauncher.WPF.csproj /m:1
dotnet build src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj /m:1
dotnet build src/MCServerLauncher.Daemon.API/MCServerLauncher.Daemon.API.csproj /m:1
dotnet build src/MCServerLauncher.DaemonClient/MCServerLauncher.DaemonClient.csproj /m:1

# Run protocol tests
dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj /m:1

# Run benchmarks
dotnet run --project benchmarks/MCServerLauncher.Benchmarks/MCServerLauncher.Benchmarks.csproj -c Release

# Run apps
dotnet run --project src/MCServerLauncher.WPF/MCServerLauncher.WPF.csproj
dotnet run --project src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj

# Publish
dotnet publish src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj -c Release -r win-x64 --self-contained
dotnet publish src/MCServerLauncher.WPF/MCServerLauncher.WPF.csproj -c Release -r win-x64 --self-contained
```

## Project Structure

```text
MCServerLauncher-Future/
├── src/MCServerLauncher.Common/            # Shared models, utilities, protocol contracts
├── src/MCServerLauncher.Daemon.API/         # Transport-neutral application and plugin contracts
├── src/MCServerLauncher.Daemon/            # Background daemon managing instances
├── generators/MCServerLauncher.Daemon.Generators/ # Transitional V1 action generator; delete at V2 cutover
├── src/MCServerLauncher.DaemonClient/      # .NET daemon client library
├── src/MCServerLauncher.WPF/               # Windows WPF client
├── tests/MCServerLauncher.ProtocolTests/     # Protocol and integration tests
├── tests/MCServerLauncher.PluginIntegrationTests/ # Published-host plugin tests
├── tools/MCServerLauncher.ProtocolDocs/     # Catalog-derived OpenRPC/Apifox tooling
├── benchmarks/MCServerLauncher.Benchmarks/        # BenchmarkDotNet performance baselines
└── Sign/                               # Code signing utilities
```

## Code Organization

### `MCServerLauncher.WPF`

- Target: `net10.0-windows10.0.18362.0`.
- `Translations/`: `.resx` resources for six-language i18n. Use `Lang.Tr[...]` or established resource lookup helpers for user-facing strings.
- `Modules/`: initializer, language, settings, and app-level WPF logic.
- `View/`: pages, dialogs, and XAML components.
- `ViewModels/`: presentation state and command coordination.
- `InstanceConsole/`: console management for instances.
- `Resources/`: icons, fonts such as Montserrat and SegoeIcons, syntax highlighting resources.
- Key dependencies: `iNKORE.UI.WPF.Modern`, AvalonEdit, Serilog, Downloader.

### `MCServerLauncher.Daemon`

- Target: `net10.0`; the plugin-enabled product is an untrimmed JIT single-file host with sidecar plugins. Keep trim analysis enabled, but do not claim Native AOT or trimmed plugin-host support.
- `Application/`: the sole local implementation of transport-neutral instance, file, system, and event-rule behavior.
- `Remote/Rpc/`: JSON-RPC V2 parsing, dispatch, connection-owned outbound queues, typed remote events, and binary sessions.
- `Plugins/`: startup-only trusted plugin discovery, validation, lifecycle, and ownership cleanup.
- `Storage/`: file management, Java scanning, contained files.
- `Management/`: instance lifecycle, factories, Minecraft support, installers, process communication.
- `Management/Factory/`: instance factories such as `MCUniversalFactory` and `MCForgeFactory` using `[InstanceFactory]`.
- `Management/Installer/`: Minecraft Forge-family installer logic.
- `Remote/Action/`: RPC action handlers and generated registry execution.
- `Remote/Event/`: event triggering and fan-out.
- `Console/`: daemon CLI commands through Brigadier.NET.
- `Utils/`: status helpers and memory/system utilities.
- `Contained/`: embedded resources such as `server.jar`.
- Key dependencies: TouchSocket, Serilog, Brigadier.NET, RustyOptions, JWT libraries.

### `MCServerLauncher.DaemonClient`

- Target: `net10.0`.
- Implements the same application contracts as the local daemon over JSON-RPC V2.
- Owns connection readiness, typed RPC calls, typed subscriptions, immutable instance-catalog mirroring, reconnect/resync behavior, and binary transfer sessions.
- Do not expose `ActionType`, `EventType`, legacy packets, raw method strings, or fallback envelope parsing.

### `MCServerLauncher.Daemon.API`

- Target: `net10.0`; packable NuGet boundary.
- Owns application interfaces, typed errors, published-state abstractions, typed RPC/event definitions, and the minimal startup plugin SDK.
- May depend only on Common, RustyOptions, BCL, and logging abstractions.
- Must not expose daemon implementation types, TouchSocket, MessagePipe, Serilog, root DI, mutable collections, or disposable resource handles.

### `MCServerLauncher.Common`

- Target: `net10.0`.
- `ProtoType/`: shared wire contracts, data structures, and enums.
- Use `System.Text.Json` exclusively for shared serialization assumptions.
- Keep serialized contracts source-generation friendly and free of logging/runtime implementation dependencies.

### V1 Action Generator

- `generators/MCServerLauncher.Daemon.Generators` is transitional V1-only code.
- Do not extend it. Remove the project, tests, benchmarks, solution entry, and runtime switch at the V2 deletion gate.

### Tests And Benchmarks

- `tests/MCServerLauncher.ProtocolTests/`: integration and protocol behavior tests.
- `benchmarks/MCServerLauncher.Benchmarks/`: BenchmarkDotNet baselines for allocation, transport, and serialization-sensitive work.

## Task Routing

- Docs or governance only: use `docs`, `agent-docs`, or `workflow`; inspect Markdown, terminology, `git diff --check`, and `git status --short`.
- WPF UI or workflow: use `frontend`; check `RULES.md` WPF rules and build with `/m:1`.
- Daemon behavior: use `backend`; public application boundaries use `Task<Result<T, DaemonError>>`, while legacy manager `Error` remains internal-only during migration. Preserve cancellation, daemon-side authority, and path validation.
- Wire contracts or serializers: use `protocol` and `serialization`; start with serialized DTOs in Common and typed application/catalog definitions in Daemon API, then update daemon, daemon client, WPF, docs tooling, tests, and benchmarks.
- Application-core changes: use `backend`; all non-implementation entrypoints must delegate to `src/MCServerLauncher.Daemon/Application`.
- Plugin-host changes: use `backend`, `protocol`, `serialization`, `tests`, and `integrations`; preserve the startup-only three-capability boundary.
- Installer changes: use `installer` plus `backend` or `storage`; keep mirror behavior explicit and preserve Forge-family installer differences.
- File/path changes: use `storage`; validate trust boundaries daemon-side.
- Tests or performance work: use `tests` or `benchmarks`; keep coverage proportional to behavioral risk.
- External services or ecosystem docs: use `integrations`; keep WebSocket, TouchSocket, Weblate, and related launcher references precise.

## AI Programming Workflow

- Before code or project-rule changes, write or update a plan under `docs/superpowers/plans` unless the change is small and purely mechanical.
- Keep plan task status current while working.
- Prefer existing project patterns and helpers over new abstractions.
- Keep edits scoped to the requested behavior and touched areas.
- Do not revert user or teammate changes without explicit instruction.
- When the work changes behavior, update docs and tests in the same task.
- Before finishing, add a changelog entry to the relevant plan when one exists.
- Before every commit, run `dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj /m:1` and confirm the full protocol test suite passes.
- Commit messages use `type(scope): subject`.

## Naming And Style

- Use PascalCase for classes, methods, properties, and events.
- Use camelCase for local variables and parameters.
- Use `_camelCase` for private fields.
- Interfaces start with `I`, for example `IInstance`, `IInstanceManager`, and `IInstanceFactory`.
- Nullable reference types are enabled; address CS8618 with `required`, nullable annotations, or initialization.
- Use implicit usings where the project enables them.
- Attribute usage omits the `Attribute` suffix, for example `[InstanceFactory]`.

## Vocabulary

Use these terms consistently:

- `daemon`: background service in `MCServerLauncher.Daemon`.
- `WPF client`: desktop UI in `MCServerLauncher.WPF`.
- `daemon client`: library in `MCServerLauncher.DaemonClient`.
- `instance`: managed server or console process with persisted config and lifecycle state.
- `RPC` and `event`: V2 protocol concepts over WebSocket. Use `action` only for the retiring V1 runtime or migration inventory.
- `meta` and `payload`: event/action data concepts.
- `factory`, `installer`, `relay`, and `notification`: project terms with established meaning.
- `Minecraft Java`, `Forge`, `Fabric`, `NeoForge`, `Quilt`, `Bedrock`, `Terraria`, and `OtherExecutable`: instance family terms.

Avoid replacing `daemon` with generic `backend`, `server`, or `service` when describing architecture. Do not call the WPF client the daemon.

## Stack Guidance

- C# uses nullable reference types; keep changed projects warning-clean.
- Shared serialization uses `System.Text.Json`; do not introduce Newtonsoft.Json.
- Plugin-enabled daemon publish paths are untrimmed JIT single-file plus sidecars. Keep trim analysis and source-generated JSON, but do not reintroduce a Native AOT/trimmed plugin-host target.
- Protocol/plugin DTO metadata must be explicit `JsonTypeInfo`; do not use reflection fallback or assembly scanning.
- WPF user-facing strings must use `Lang.Tr[...]` and resource keys.
- WPF builds should use `/m:1` to avoid transient generated-XAML issues.
- Instance type support remains factory-driven through `IInstanceFactory` and `[InstanceFactory]`.
- Protocol field or semantic changes need protocol tests.
- Performance-sensitive transport or serialization changes need a benchmark update or a clear reason existing benchmarks are sufficient.

## Domain Checks

- Client-daemon communication uses the sole `/api/v2` JSON-RPC endpoint plus versioned binary frames over WebSocket.
- Shared serialized wire contracts live in `src/MCServerLauncher.Common`; application/plugin contracts live in `src/MCServerLauncher.Daemon.API`.
- Daemon behavior has one application implementation; transport, console, event rules, WPF, and plugins are entrypoints.
- The frozen typed catalog is the only source for dispatch, daemon-client metadata, runtime OpenRPC, and generated checked-in Apifox.
- Daemon-side validation is authoritative for security, lifecycle, and path safety.
- Meta-bearing events must preserve documented missing/null metadata semantics.
- Instance lifecycle paths should preserve cancellation support where the surrounding API exposes it.
- Installer code should validate cached or downloaded libraries with checksums when data provides them.
- WPF validation may prevent bad submissions, but it must not be the only safety check.

## Performance Notes

- Recent work focuses on byte-oriented transport paths and reducing avoidable string conversion.
- Serialization uses explicit source-generated `System.Text.Json` metadata for every protocol and plugin DTO.
- Event fan-out should avoid duplicate serialization when semantics allow.
- Benchmarks should remain tied to meaningful protocol, transport, and serialization behavior.
- Performance logs may use `[PERF]` markers when the surrounding code already does so.
- Object pooling exists through `Microsoft.Extensions.ObjectPool`; use it only where it removes measured allocation pressure.

## Common Issues

### Config File Shape Changes

If daemon startup fails with JSON parsing errors after model changes, outdated local config may need removal:

- `./config.json`
- `./daemon/instances/<uuid>/daemon_instance.json`

Do not delete user config unless the user asks or the task explicitly authorizes cleanup.

### Source Generator Warnings

RS2008 analyzer release-tracking warnings are non-critical unless the task touches generator packaging or diagnostics. Fix new generator warnings introduced by changed code.

### Nullable Warnings

Common nullable-warning locations include daemon installer JSON models and daemon action execution paths. Fix nullable warnings in changed code rather than broadly suppressing them.

### Switch Exhaustiveness

CS8524 warnings indicate enum cases may be missing in switch expressions. Add explicit cases or a deliberate fallback.

## Verification

Choose the smallest relevant set:

- Docs-only: inspect Markdown, run terminology search when vocabulary changes, then `git diff --check`.
- WPF: `dotnet build src/MCServerLauncher.WPF/MCServerLauncher.WPF.csproj /m:1`
- Daemon: `dotnet build src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj /m:1`
- Daemon API: `dotnet build src/MCServerLauncher.Daemon.API/MCServerLauncher.Daemon.API.csproj /m:1`
- Daemon client: `dotnet build src/MCServerLauncher.DaemonClient/MCServerLauncher.DaemonClient.csproj /m:1`
- Protocol: `dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj /m:1`
- Generated docs: `dotnet run --project tools/MCServerLauncher.ProtocolDocs/MCServerLauncher.ProtocolDocs.csproj -- --check`
- V1 deletion: `pwsh -File tools/VerifyNoV1Runtime.ps1`
- Plugin integration: publish daemon and fixtures, set `MCSL_PUBLISHED_DAEMON`, then run `dotnet test tests/MCServerLauncher.PluginIntegrationTests/MCServerLauncher.PluginIntegrationTests.csproj -c Release /m:1`
- Benchmarks: `dotnet run --project benchmarks/MCServerLauncher.Benchmarks/MCServerLauncher.Benchmarks.csproj -c Release`
- Full solution: `dotnet build MCServerLauncher.slnx /m:1`
- Final hygiene: `git diff --check` and `git status --short --branch`

## Documentation And Integrations

- Daemon machine-readable docs live under `src/MCServerLauncher.Daemon/.Resources/Docs/`, primarily `apifox.json` and embedded `protocol/topics/*.md` resources.
- WPF docs live under `src/MCServerLauncher.WPF/.Resources/Docs/`.
- Public READMEs exist in English and Chinese.
- Internationalization is managed through Weblate.
- Related projects include the Rust daemon experiment, the cross-platform Tauri launcher, and the browser web panel. Treat those as external unless the task explicitly crosses repo boundaries.

## Git And Workflow Notes

- Main branch is `master`.
- Keep commits concise and conventional: `type(scope): subject`.
- Check build warnings, especially nullable warnings, for touched projects.
- Before every commit, ensure the full `MCServerLauncher.ProtocolTests` suite passes.
- Report known existing warnings separately from warnings introduced by the task.
- Do not stage or commit unrelated user-owned changes unless explicitly instructed.

## Do Not

- Do not restore, delete, or reformat unrelated user-owned changes.
- Do not move shared protocol types out of `src/MCServerLauncher.Common`.
- Do not make frontend-only checks authoritative for daemon security or path safety.
- Do not add reflection fallback or assembly-scanned DTO discovery to protocol/plugin paths.
- Do not expose daemon internals, TouchSocket, MessagePipe, Serilog, root DI, mutable runtime collections, or disposable handles through Daemon API.
- Do not add V1 fallback, dual dispatch, compatibility switches, plugin hot unload, hooks, factory/store/control/filesystem capabilities, or plugin dependencies to the first milestone.
- Do not rename protocol vocabulary casually.
- Do not add broad refactors to narrow fixes.
- Do not commit secrets, local credentials, generated build output, or private data.

## Known Local Notes

- `TASKS.md` may be absent in the current working tree; treat that as user-owned state unless instructed otherwise.
- Local skills under `skills/` may include user-provided untracked files. Do not clean them up without explicit instruction.
