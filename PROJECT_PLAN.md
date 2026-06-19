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
- .NET daemon for instance lifecycle, installer workflows, file transfer, Java scanning, event broadcast, RPC actions, and CLI commands.
- Daemon client library for WebSocket RPC, event subscriptions, file upload/download, and reconnection handling.
- Shared protocol/data contracts in `src/MCServerLauncher.Common`.
- Source generators for AOT-compatible daemon action registration and serialization support.
- Protocol tests and benchmarks for transport, serialization, and performance behavior.

## Out Of Scope

- The web panel and Tauri launcher are separate repositories.
- The Rust daemon is experimental and external.
- Browser UI changes are not made in this repository.
- Protocol-breaking changes are not acceptable without explicit migration and tests.

## Architecture

- `src/MCServerLauncher.WPF` is the Windows desktop client targeting `net10.0-windows10.0.18362.0`.
- `src/MCServerLauncher.Daemon` is the background service targeting `net10.0`, with trimming/AOT constraints.
- `src/MCServerLauncher.DaemonClient` is the .NET client library for daemon WebSocket communication.
- `src/MCServerLauncher.Common` owns shared protocol types and cross-project contracts.
- `generators/MCServerLauncher.Daemon.Generators` owns Roslyn generation for daemon action registration and analyzer diagnostics.
- `tests/MCServerLauncher.ProtocolTests` verifies protocol and integration behavior.
- `benchmarks/MCServerLauncher.Benchmarks` tracks performance-sensitive paths.

## Fixed Invariants

- Client-daemon communication uses the action/event protocol over WebSocket.
- Shared wire contracts live in `src/MCServerLauncher.Common` and must remain serializer-friendly.
- Daemon serialization must remain compatible with trimming and AOT; prefer source-generated `System.Text.Json`.
- Instance type support is factory-driven through `IInstanceFactory` and `[InstanceFactory]`.
- WPF user-facing strings must use `Lang.Tr[...]` and resource keys instead of hard-coded UI text.
- Daemon file paths must be resolved and validated through daemon-side path helpers when crossing trust boundaries.
- Instance lifecycle operations must preserve cancellation support where the surrounding API exposes it.
- Transport performance work must be measured or covered by protocol tests and benchmarks when behavior changes.
- Commit messages use `type(scope): subject`.

## Vocabulary

- Use `daemon`, `WPF client`, `daemon client`, `instance`, `factory`, `installer`, `action`, `event`, `meta`, `payload`, `relay`, and `notification` consistently.
- Use `Minecraft Java`, `Forge`, `Fabric`, `NeoForge`, `Quilt`, `Bedrock`, `Terraria`, and `OtherExecutable` for instance families.
- Avoid replacing `daemon` with generic words like service, backend, or server when describing this product's architecture.
- Avoid calling the WPF client the daemon.

## Verification Matrix

- Docs-only: inspect Markdown, run `git diff --check`, and run terminology searches when vocabulary changes.
- WPF: `dotnet build src/MCServerLauncher.WPF/MCServerLauncher.WPF.csproj /m:1`.
- Daemon: `dotnet build src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj /m:1`.
- Daemon client: `dotnet build src/MCServerLauncher.DaemonClient/MCServerLauncher.DaemonClient.csproj /m:1`.
- Source generator: `dotnet build generators/MCServerLauncher.Daemon.Generators/MCServerLauncher.Daemon.Generators.csproj /m:1`.
- Protocol behavior: `dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj /m:1`.
- Performance: `dotnet run --project benchmarks/MCServerLauncher.Benchmarks/MCServerLauncher.Benchmarks.csproj -c Release`.
- Full solution: `dotnet build MCServerLauncher.sln /m:1`.

## Milestones

- Stabilize daemon protocol contracts, byte-oriented transport paths, and source-generated serialization.
- Keep WPF instance creation and instance console workflows warning-clean and i18n-compliant.
- Improve installer robustness, local cache behavior, and user-facing validation.
- Preserve daemon publishability under trimming, single-file, and Native AOT constraints.
- Keep agent and contributor docs aligned with actual repo workflows.
