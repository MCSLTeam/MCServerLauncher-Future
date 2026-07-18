# V2 heavy RPC performance (system_info / java_list / rpc.discover)

## Touched areas

`backend`, `protocol`, `serialization`, `tests`

## Goal

Beat V1 under high concurrency for heavy payload RPCs per `BENCHMARK_REPORT.md`
(throughput −60% / latency +160% on system_info & java_list; discover memory hotspot).

## Changes

1. `AsyncTimedLazyCell`: lock-free cache hits + single-flight refresh + stale-while-revalidate.
2. `CpuInfoHelper`: Win/Linux sliding sample window (no per-request 300ms delay after cold start); Linux reads `/proc/stat` without shell.
3. `MemoryInfoHelper`: Windows `GlobalMemoryStatusEx`; Linux `/proc/meminfo` file read.
4. Java runtime cell TTL `2s` → `5m` (full-disk scan).
5. `rpc.discover`: success path reuses frozen `DocumentUtf8` via `JsonRpcObjectPayload.FromValidatedUtf8Object`.
6. `LocalSystemApplication`: reuse `JavaRuntimeList` for same array instance.

## Verification

- `dotnet build src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj /m:1`
- `dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj /m:1` → 927 passed

## Follow-up

- Re-run Windows load matrix in the benchmark repo (not available in this macOS session).
- Optional: prebuild success envelopes for discover (id still dynamic), event subscribe ledger contention.

## Changelog

- 2026-07-18: Implemented P0 caching/serialization paths above.
