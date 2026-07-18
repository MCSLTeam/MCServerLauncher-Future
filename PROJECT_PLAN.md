# MCServerLauncher Future Project Plan

## Product Direction

MCServerLauncher Future, codename `mcsl-future`, is a server management suite for Minecraft servers and general console applications. It provides a Windows WPF client, a .NET daemon, a daemon client library, protocol tests, source generators, and performance benchmarks. The product goal is to make creating, monitoring, controlling, and maintaining multiple server instances predictable for local and remote operators.

## Audience

- Server owners who manage one or more Minecraft or console server instances.
- Operators who need a GUI for daemon-backed instance management.
- Contributors who extend daemon RPC, WPF workflows, installers, eventing, or protocol compatibility.
- Translators maintaining the six-language WPF resource set through Weblate.

## Scope Now

- WPF desktop client for daemon connection, instance creation, console management, file management, settings, notifications, and i18n.
- .NET daemon for instance lifecycle, installer workflows, file transfer, Java scanning, typed event broadcast, JSON-RPC V2, and CLI commands.
- Transport-neutral application contracts in `src/MCServerLauncher.Daemon.API`, implemented locally by the daemon and remotely by the daemon client.
- Daemon client library for typed application calls, typed event subscriptions, binary transfer sessions, immutable instance-state mirroring, and reconnection handling.
- Shared serialized protocol/data contracts in `src/MCServerLauncher.Common`.
- Startup-only trusted in-process plugins with typed RPC registration, typed event publication, and immutable instance queries.
- Protocol, API-boundary, plugin-integration, packaging, and performance tests.

## Out Of Scope

- The web panel and Tauri launcher are separate repositories.
- The Rust daemon is experimental and external.
- Browser UI changes are not made in this repository.
- Runtime plugin install, reload, hot unload, hooks, factory/installer extensions, plugin filesystem writes, and plugin-to-plugin services are outside the first plugin milestone.
- Protocol-breaking changes require an explicit migration plan, parity inventory, tests, and a release-atomic cutover. The approved V2 cutover intentionally removes V1 rather than maintaining fallback paths.

## Architecture

- `src/MCServerLauncher.WPF` is the Windows desktop client targeting `net10.0-windows10.0.18362.0`.
- `src/MCServerLauncher.Daemon` is the background service targeting `net10.0`; the plugin-enabled product is an untrimmed JIT single-file host with trim analysis enabled.
- `src/MCServerLauncher.Daemon.API` is the packable `net10.0` application/plugin contract package. It is transport-neutral and must not expose daemon, TouchSocket, MessagePipe, Serilog, or root-DI types.
- `src/MCServerLauncher.DaemonClient` is the remote implementation of the application contracts over `/api/v2` WebSocket JSON-RPC and binary frames.
- `src/MCServerLauncher.Common` targets `net10.0` and owns shared serialized DTOs and wire envelopes.
- A frozen typed RPC/event catalog is the single source for daemon dispatch, daemon-client metadata, runtime OpenRPC, and generated checked-in Apifox documentation.
- The V1 custom action generator is transitional and is deleted as part of the release-atomic V2 cutover.
- `tests/MCServerLauncher.ProtocolTests` verifies protocol and integration behavior.
- `tests/MCServerLauncher.PluginIntegrationTests` verifies plugins against a published single-file daemon host.
- `benchmarks/MCServerLauncher.Benchmarks` tracks performance-sensitive paths.

## Fixed Invariants

- Client-daemon communication uses JSON-RPC 2.0 and versioned binary transfer frames over the sole `/api/v2` WebSocket endpoint.
- Daemon behavior has one transport-neutral application implementation; transport, daemon console, event rules, WPF, and plugins are entrypoints rather than independent behavior sources.
- Shared serialized wire contracts live in `src/MCServerLauncher.Common`; application and plugin interfaces live in `src/MCServerLauncher.Daemon.API`.
- Serialization uses source-generated `System.Text.Json` metadata and explicit `JsonTypeInfo`; reflection fallback is not part of the protocol or plugin contract.
- Plugin-enabled daemon releases are untrimmed JIT single-file hosts with sidecar plugins. Keep trim analysis and source-generated serialization, but do not claim Native AOT or trimmed plugin-host support.
- Trusted plugins are discovered and started only during daemon startup. The first public capability surface is limited to typed RPC registration, typed event publication, and immutable instance queries.
- Instance catalog reads use deep immutable copy-on-write published state; public snapshots never expose mutable runtime collections or process/file/transport handles.
- Instance type support is factory-driven through `IInstanceFactory` and `[InstanceFactory]`.
- WPF user-facing strings must use `Lang.Tr[...]` and resource keys instead of hard-coded UI text.
- Daemon file paths must be resolved and validated through daemon-side path helpers when crossing trust boundaries.
- Instance lifecycle operations must preserve cancellation support where the surrounding API exposes it.
- Transport performance work must be measured against a checked-in baseline; equivalent V2 dispatch mean and allocation may not regress by more than the approved threshold without an explicit baseline decision.
- Commit messages use `type(scope): subject`.

## Vocabulary

- Use `daemon`, `WPF client`, `daemon client`, `instance`, `application core`, `RPC`, `event`, `meta`, `payload`, `factory`, `installer`, `plugin`, `capability`, and `notification` consistently.
- `action` refers only to the retiring V1 runtime or historical migration inventory; new public protocol work uses typed RPC methods and events.
- Use `Minecraft Java`, `Forge`, `Fabric`, `NeoForge`, `Quilt`, `Bedrock`, `Terraria`, and `OtherExecutable` for instance families.
- Avoid replacing `daemon` with generic words like service, backend, or server when describing this product's architecture.
- Avoid calling the WPF client the daemon.

## Verification Matrix

- Docs-only: inspect Markdown, run `git diff --check`, and run terminology searches when vocabulary changes.
- WPF: `dotnet build src/MCServerLauncher.WPF/MCServerLauncher.WPF.csproj /m:1`.
- Daemon: `dotnet build src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj /m:1`.
- Daemon API: `dotnet build src/MCServerLauncher.Daemon.API/MCServerLauncher.Daemon.API.csproj /m:1`.
- Daemon client: `dotnet build src/MCServerLauncher.DaemonClient/MCServerLauncher.DaemonClient.csproj /m:1`.
- Protocol behavior: `dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj /m:1`.
- Plugin integration: publish the daemon and plugin fixtures, set `MCSL_PUBLISHED_DAEMON`, then run `dotnet test tests/MCServerLauncher.PluginIntegrationTests/MCServerLauncher.PluginIntegrationTests.csproj -c Release /m:1`.
- Generated protocol docs: `dotnet run --project tools/MCServerLauncher.ProtocolDocs/MCServerLauncher.ProtocolDocs.csproj -- --check`.
- Performance: `dotnet run --project benchmarks/MCServerLauncher.Benchmarks/MCServerLauncher.Benchmarks.csproj -c Release`.
- Full solution: `dotnet build MCServerLauncher.slnx /m:1`.

## Milestones

- Complete the application-core-first `/api/v2` cutover, remove the V1 runtime/client/generator, and keep the typed catalog as the sole protocol source.
- Publish the packable Daemon API and validate the startup-only health plugin against a published daemon host.
- Keep immutable state reads lock-free and zero-allocation in steady state, and keep event fan-out serialization-once.
- Keep WPF instance creation and instance console workflows warning-clean and i18n-compliant.
- Improve installer robustness, local cache behavior, and user-facing validation.
- Preserve untrimmed single-file daemon publishing with trim analysis enabled; do not reintroduce a Native AOT or trimmed plugin-host product target.
- Keep agent and contributor docs aligned with actual repo workflows.
