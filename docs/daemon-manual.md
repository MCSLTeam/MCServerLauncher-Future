# Daemon Manual

## Run locally

```powershell
dotnet run --project src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj
```

The daemon reads `config.json` from its executable base directory. Keep it beside the published daemon executable; the process working directory does not change this location when a service manager launches the daemon. The minimum local configuration contains a listening port and the authentication secret:

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
$referenceRoot = Join-Path ([System.IO.Path]::GetTempPath()) "mcsl-phase0-925666a4"
git worktree add --detach $referenceRoot 925666a4
$referenceArtifacts = Join-Path $PWD "BenchmarkDotNet.Reference"
Push-Location $referenceRoot
try {
  dotnet run --project benchmarks/MCServerLauncher.Benchmarks/MCServerLauncher.Benchmarks.csproj -c Release -- `
    --filter "*ActionDispatchBenchmarks.DispatchPing*" --exporters json --artifacts $referenceArtifacts
}
finally {
  Pop-Location
}
dotnet run --project benchmarks/MCServerLauncher.Benchmarks/MCServerLauncher.Benchmarks.csproj -c Release -- --exporters json --artifacts BenchmarkDotNet.Artifacts
dotnet run --project tools/MCServerLauncher.PerformanceGate/MCServerLauncher.PerformanceGate.csproj -c Release -- `
  --baseline benchmarks/baselines/v2.json --reference-results BenchmarkDotNet.Reference/results `
  --results BenchmarkDotNet.Artifacts/results --paired
git worktree remove $referenceRoot
```

The gate always checks allocations. The request-dispatch entry compares the V2 report with `ActionDispatchBenchmarks.DispatchPing` built from the immutable Phase 0 commit `925666a4`; the state and event entries use their recorded V2 captures. `--paired` requires that independent V1 report, and the gate verifies that its BenchmarkDotNet, SDK, runtime, OS, architecture, processor, and configuration fingerprint matches the V2 candidate. The scheduled benchmark workflow and every release run perform the same two-checkout comparison on one runner before release artifacts are built.
