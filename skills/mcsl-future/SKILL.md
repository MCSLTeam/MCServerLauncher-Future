---
name: mcsl-future
description: Guides work that changes or reviews MCServerLauncher Future domain behavior. Use for daemon protocol, instance lifecycle, file/path handling, installers, serialization, WPF instance submission, or event semantics, not ordinary docs-only edits.
---

# MCServerLauncher Future

## Quick Start

1. Read `PROJECT_PLAN.md`.
2. Read `RULES.md`.
3. Declare touched areas.
4. Preserve the invariants below.
5. Run the smallest relevant verification.

## Invariants

- Client-daemon communication uses JSON-RPC 2.0 plus versioned binary transfer frames over the sole `/api/v2` WebSocket endpoint.
- Shared serialized wire contracts live in `src/MCServerLauncher.Common`; transport-neutral application/plugin contracts live in `src/MCServerLauncher.Daemon.API`.
- Daemon behavior has one application implementation; transport, console, event rules, WPF, and plugins delegate to it.
- Protocol and plugin serialization uses explicit source-generated `System.Text.Json` metadata with no reflection fallback.
- Plugin-enabled publishing is untrimmed JIT single-file plus sidecars. Keep trim analysis, but do not claim Native AOT or trimmed plugin-host support.
- Plugins are trusted and startup-only. Plugin API 2.0 admits features via `mcsl-plugin.json` (`requires.features`); first-milestone runtime implements `rpc.register`, `event.publish`, and `instance.query`, with Preview-1 expanding the vocabulary for operations/provisioning/auth/HTTP.
- Instance catalog reads use deep immutable copy-on-write published state.
- Instance creation remains factory-driven through `IInstanceFactory` and `[InstanceFactory]`.
- WPF user-facing text uses `Lang.Tr[...]`.
- Daemon path handling validates trust boundaries before file access.

## Task Recipes

**Protocol or serialization.** Start with serialized DTOs in Common and typed definitions/application contracts in Daemon API, then update daemon, daemon client, WPF, generated docs, protocol tests, and benchmarks. Do not add V1 fallback or reflection metadata discovery.

**Application core.** Put behavior behind the four domain application services. Console, event rules, JSON-RPC bindings, and plugins must not duplicate manager/storage rules.

**Published state.** Keep snapshot values deeply immutable. `Current`/`TryGet` reads are lock-free and allocation-free; I/O, callbacks, logging, and `await` never occur under the writer lock.

**Plugins.** Validate `mcsl-plugin.json`, API version, features, references, namespace ownership, and catalog conflicts before commit. A failed plugin must leave no host-owned registration or lifetime residue and must not stop daemon startup.

**Instance lifecycle.** Public application methods use `Task<Result<T, DaemonError>>`; legacy manager `Result<T, Error>` paths stay implementation-only until the V2 deletion gate. Preserve cancellation and daemon-side authority for state changes.

**Installer changes.** Keep mirror behavior explicit, preserve Forge-family format differences, and validate cached/downloaded libraries when checksums exist.

**WPF create-instance flows.** Validate form values before submission, but keep daemon-side validation authoritative.

**Events.** Preserve missing/null/object meta/data semantics, typed canonical filtering, reconnect resubscription, connection-owned ordering, slow-consumer disconnect, and serialization-once fan-out.

## Red Flags

- Frontend-only validation becomes the only security or path-safety check.
- Reflection is added to protocol/plugin DTO serialization or runtime catalog discovery.
- Daemon internals, TouchSocket, MessagePipe, Serilog, root DI, mutable collections, or disposable handles leak into Daemon API.
- V1/V2 fallback, dual dispatch, legacy envelope detection, or compatibility wrappers are introduced.
- Plugin hooks, hot unload, factory/store/control/filesystem capabilities, or plugin dependencies are added before an approved plan and real consumer exist.
- A protocol field changes without protocol tests.
- Domain vocabulary changes without updating `PROJECT_PLAN.md` and `RULES.md`.

## Verification

- Docs: inspect Markdown and run terminology searches.
- Daemon API: `dotnet build src/MCServerLauncher.Daemon.API/MCServerLauncher.Daemon.API.csproj /m:1`
- WPF: `dotnet build src/MCServerLauncher.WPF/MCServerLauncher.WPF.csproj /m:1`
- Daemon: `dotnet build src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj /m:1`
- Protocol: `dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj /m:1`
- Generated docs: `dotnet run --project tools/MCServerLauncher.ProtocolDocs/MCServerLauncher.ProtocolDocs.csproj -- --check`
- V1 deletion: `pwsh -File tools/VerifyNoV1Runtime.ps1`
- Final: `git diff --check`

## Commits

Use `type(scope): subject`.
