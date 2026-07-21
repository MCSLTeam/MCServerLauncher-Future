# Startup Plugin Developer Guide

## Scope

Plugin API **2.0** uses feature-gated admission. A first-milestone plugin may declare
`rpc.register`, `event.publish`, and `instance.query`, then use the corresponding typed API:

- register typed RPC descriptors with source-generated `JsonTypeInfo`;
- publish typed events with missing, explicit-null, or value metadata;
- read immutable `IInstanceSnapshotSource` state.

Plugins are trusted in-process code. Features are admission and audit boundaries, not a sandbox.
Hooks, hot unload, factory/store/control/filesystem capabilities, and plugin-to-plugin dependency
services are outside this milestone. Preview-1 expands the feature vocabulary for operations,
provisioning, auth verification, and HTTP listen; those surfaces land with the Plugin SDK 2.0 plan.

## Project reference

Prefer the Plugin SDK package for generated modules:

```xml
<PackageReference Include="MCServerLauncher.Daemon.Plugin.Sdk" Version="2.0.0-preview.1" />
```

Reference the packable API package directly only when the public DTO contract requires it without generation:

```xml
<PackageReference Include="MCServerLauncher.Daemon.API" Version="2.0.0-preview.1" />
```

Each tagged GitHub release attaches matching `MCServerLauncher.Common.<version>.nupkg`,
`MCServerLauncher.Daemon.API.<version>.nupkg`, and `MCServerLauncher.Daemon.Plugin.Sdk.<version>.nupkg`
assets. Download them into a configured NuGet source before restore when a package feed is not
available. Exact Preview-1 versions and SHA-256 hashes are recorded in
`docs/preview1-package-pin.md`.

The API package does not expose TouchSocket, MessagePipe, Serilog, daemon DI, mutable runtime
collections, or disposable resource handles.

## Manifest

Create `mcsl-plugin.json` beside the entry assembly:

```json
{
  "package": {
    "id": "community.example.health",
    "version": "1.0.0"
  },
  "entry": {
    "assembly": "Example.Plugin.dll",
    "type": "Example.Plugin.HealthPlugin"
  },
  "requires": {
    "api": "[2.0.0,3.0.0)",
    "features": ["event.publish", "instance.query", "rpc.register"]
  }
}
```

The package id is lowercase, dot-separated, and owns the `plugin.<id>.` protocol namespace.
Discovery rejects duplicate ids, invalid ranges, unknown or unimplemented features, forbidden
daemon/transport references, private copies of shared contracts, and malformed PE metadata
deterministically. There is no `plugin.json` / `capabilities` compatibility path.

## Lifecycle

Implement `IDaemonPlugin`:

1. `Configure` records typed registrations only. It must not perform I/O or start background work.
2. The host validates the draft globally and moves the plugin through `Configured` and `Validated`.
3. `StartAsync` may create background work, but work must await `IPluginContext.Activation` before publishing events.
4. The host commits the catalog and activates all successful plugins before opening `/api/v2`.
5. `StopAsync` releases plugin-owned resources. The host cancels `LifetimeToken` first and stops plugins in reverse order.

Return `Result.Err` for expected failures. Unexpected exceptions are caught, logged with the
lifecycle stage, and isolated from other plugins and daemon startup.

## Build and publish

```powershell
dotnet build Example.Plugin/Example.Plugin.csproj -c Release
dotnet publish Example.Plugin/Example.Plugin.csproj -c Release -p:MCSLPluginBundle=true -o artifacts/plugins/community.example.health
```

The `MCSLPluginBundle=true` property is required: the API package's publish target removes shared
host assemblies from the bundle so the daemon can preserve one shared contract identity. Copy
`mcsl-plugin.json`, the plugin entry DLL, and private dependencies to
`plugins/community.example.health/` beside the published daemon. Do not copy the daemon executable,
TouchSocket assemblies, MessagePipe, Serilog, `MCServerLauncher.Daemon.API.dll`, or
`MCServerLauncher.Common.dll` into the bundle.

The repository fixtures under `tests/Fixtures/Plugins/` show external-style compile, health,
returned-error, and throwing cases. The published acceptance command is:

```powershell
$env:MCSL_PUBLISHED_DAEMON = (Resolve-Path artifacts/plugin-e2e/daemon/MCServerLauncher.Daemon.exe)
dotnet test tests/MCServerLauncher.PluginIntegrationTests/MCServerLauncher.PluginIntegrationTests.csproj -c Release -m:1
```
