# Startup Plugin Developer Guide

## Scope

The first plugin SDK milestone is intentionally small. A plugin may declare `rpc.register`, `event.publish`, and `instance.query`, then use the corresponding typed API:

- register typed RPC descriptors with source-generated `JsonTypeInfo`;
- publish typed events with missing, explicit-null, or value metadata;
- read immutable `IInstanceSnapshotSource` state.

Plugins are trusted in-process code. Capabilities are admission and audit boundaries, not a sandbox. Hooks, hot unload, factory/store/control/filesystem capabilities, and plugin-to-plugin dependency services are outside this milestone.

## Project reference

Reference the packable API package (and Common only when the public DTO contract requires it):

```xml
<PackageReference Include="MCServerLauncher.Daemon.API" Version="1.0.0" />
```

The API package does not expose TouchSocket, MessagePipe, Serilog, daemon DI, mutable runtime collections, or disposable resource handles.

## Manifest

Create `plugin.json` beside the entry assembly:

```json
{
  "id": "community.example.health",
  "version": "1.0.0",
  "entry_assembly": "Example.Plugin.dll",
  "entry_type": "Example.Plugin.HealthPlugin",
  "api_version": "[1.0.0,2.0.0)",
  "capabilities": ["rpc.register", "event.publish", "instance.query"]
}
```

The id is lowercase, dot-separated, and owns the `plugin.<id>.` protocol namespace. Discovery rejects duplicate ids, invalid ranges, forbidden daemon/transport references, private copies of shared contracts, and malformed PE metadata deterministically.

## Lifecycle

Implement `IDaemonPlugin`:

1. `Configure` records typed registrations only. It must not perform I/O or start background work.
2. The host validates the draft globally and moves the plugin through `Configured` and `Validated`.
3. `StartAsync` may create background work, but work must await `IPluginContext.Activation` before publishing events.
4. The host commits the catalog and activates all successful plugins before opening `/api/v2`.
5. `StopAsync` releases plugin-owned resources. The host cancels `LifetimeToken` first and stops plugins in reverse order.

Return `Result.Err` for expected failures. Unexpected exceptions are caught, logged with the lifecycle stage, and isolated from other plugins and daemon startup.

## Build and publish

```powershell
dotnet build Example.Plugin/Example.Plugin.csproj -c Release
dotnet publish Example.Plugin/Example.Plugin.csproj -c Release -o artifacts/plugins/community.example.health
```

Copy `plugin.json`, the plugin entry DLL, and private dependencies to `plugins/community.example.health/` beside the published daemon. Do not copy the daemon executable, TouchSocket assemblies, MessagePipe, Serilog, `MCServerLauncher.Daemon.API.dll`, or `MCServerLauncher.Common.dll` into the bundle.

The repository fixtures under `tests/Fixtures/Plugins/` show external-style compile, health, returned-error, and throwing cases. The published acceptance command is:

```powershell
$env:MCSL_PUBLISHED_DAEMON = (Resolve-Path artifacts/plugin-e2e/daemon/MCServerLauncher.Daemon.exe)
dotnet test tests/MCServerLauncher.PluginIntegrationTests/MCServerLauncher.PluginIntegrationTests.csproj -c Release /m:1
```
