# Daemon Manual

## Run locally

```powershell
dotnet run --project src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj
```

The daemon reads `config.json` from its working directory. The minimum local configuration contains a listening port and the authentication secret:

```json
{
  "port": 25565,
  "secret": "replace-with-a-secret",
  "main_token": "replace-with-a-secret",
  "file_download_sessions": 4,
  "verbose": false
}
```

The client connects to `ws://host:port/api/v2` and supplies the token according to the daemon client options. `/api/v1` is not a supported endpoint.

## Publish

Supported daemon runtime identifiers are `win-x64`, `win-x86`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64`. The release workflow publishes self-contained and framework-dependent variants for each identifier. All plugin-enabled publishes explicitly set `PublishTrimmed=false` and keep `PublishSingleFile=true`.

```powershell
dotnet publish src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj `
  -c Release -r win-x64 --self-contained -p:PublishTrimmed=false `
  -p:PublishSingleFile=true -o artifacts/publish/daemon
```

Place each plugin bundle under `artifacts/publish/daemon/plugins/<plugin-id>/`. A bundle contains `plugin.json`, the entry assembly, and private implementation dependencies. Shared `MCServerLauncher.Daemon.API.dll` and `MCServerLauncher.Common.dll` copies are rejected.

## Startup and shutdown

Plugins are discovered, validated, configured, started, admitted to the frozen catalog, and activated before the first client connection is accepted. A failed plugin is logged and skipped without preventing daemon startup. Shutdown cancels plugin lifetimes, stops successful plugins in reverse start order, closes event/RPC ownership, and then stops the daemon services.

For an interactive daemon, type `exit` in the console to request graceful shutdown. A console cancellation signal follows the same lifecycle.

## Diagnostics

Daemon logs are written under the configured daemon data directory. Plugin failures include the plugin id, version, stable lifecycle stage, error code, and message. Exception objects remain in daemon logs and are never serialized through the public protocol.

## Performance evidence

The benchmark project emits BenchmarkDotNet JSON reports. The checked-in V2 baseline covers request dispatch, immutable state reads, and the 32-subscriber event serialization comparison:

```powershell
dotnet run --project benchmarks/MCServerLauncher.Benchmarks/MCServerLauncher.Benchmarks.csproj -c Release -- --exporters json --artifacts BenchmarkDotNet.Artifacts
dotnet run --project tools/MCServerLauncher.PerformanceGate/MCServerLauncher.PerformanceGate.csproj -c Release -- `
  --baseline benchmarks/baselines/v2.json --results BenchmarkDotNet.Artifacts/results
```

The gate always checks allocations. The request-dispatch entry explicitly compares the V2 report with the Phase 0 V1 `request.dispatch.ping` reference; the state and event entries use their recorded V2 captures. Mean comparisons require the BenchmarkDotNet, SDK, runtime, OS, architecture, processor, and configuration fingerprint to match the baseline. A deliberately paired same-machine A/B run may pass `--paired`; an unrelated environment must recapture its baseline instead.
