# Daemon V1 to V2 Parity And Deletion Inventory

> **Authority:** Companion execution inventory for `2026-06-27-daemon-api-inprocess-plugin-v2-plan.md`.
>
> **Baseline:** commit `925666a4`, branch `daemon-api`, captured 2026-07-11 with .NET SDK `10.0.201` on Windows `win-x64`.
>
> **Rule:** Preserve business intent and explicitly frozen lifecycle semantics. Do not preserve V1 transport bugs, unsafe ownership, fallback paths, error leakage, or side-channel state mutation.

## 1. Baseline Evidence

- `dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release /m:1`: 382 passed, 0 failed, 0 skipped.
- Runtime endpoint: `/api/v1`, query-token JWT verification.
- Runtime registry: 36 `ActionType` values, selectable reflection or generated registry.
- Events: `InstanceLog`, `DaemonReport`; notification is a separate direct-broadcast envelope; relay has no daemon producer.
- Upload: action-open plus either base64 action chunks or an undocumented raw binary shortcut.
- Download: action-open plus `BigEndianUnicode` string ranges; no binary download.
- The normalized benchmark baseline is stored in `benchmarks/baselines/v1.json`; raw BenchmarkDotNet reports remain build artifacts and are not checked in.

Current benchmark evidence used to bootstrap the normalized baseline:

| Operation | Mean ns | Allocated B/op |
|---|---:|---:|
| action dispatch: ping | 448.64 | 424 |
| action dispatch: system info | 7,915.50 | 2,584 |
| action parse | 1,769.75 | 2,320 |
| action response serialize | 219.51 | 352 |
| daemon-client action round trip | 2,436.25 | 2,256 |
| daemon-client event round trip | 1,227.11 | 1,384 |
| event packet serialize | 775.53 | 976 |
| legacy raw upload frame build, 64 KiB | 67,781.36 | 65,656 |
| legacy raw upload ack parse | 269.95 | 72 |
| generated registry startup | 641.45 | 2,704 |
| reflection registry startup | 128,736.78 | 61,025 |

Mean comparisons are valid only for a matching environment fingerprint or an explicit same-machine A/B run. Allocation gates are always evaluated.

## 2. Frozen Ownership And Metadata Decisions

- Serialized DTOs and JSON-RPC/binary envelopes live in Common.
- Application interfaces, typed errors, published state, typed names/descriptors, and plugin SDK live in Daemon API.
- Built-in params/results/event payloads use explicit metadata from `BuiltInProtocolJsonContext`; JSON-RPC envelopes use `RpcProtocolJsonContext`. These replace the V1 `RpcEnvelopeContext`, `ActionParametersContext`, `ActionResultsContext`, `EventDataContext`, and runtime type-info cache.
- Plugin DTO metadata is supplied by each plugin and registered in its startup draft. The host never scans plugin assemblies for DTOs.
- `Ping`, permissions, subscriptions, connection readiness, outbound queues, and binary-frame correlation are connection/transport responsibilities, not application-core behavior.
- `RestartInstanceAsync` remains a daemon-client convenience composition of typed stop, the existing one-second delay, and typed start. It is not a new daemon RPC or application method.
- WPF migration groups used below:
  - `connection`: `Modules/Daemons.cs`, `Services/DaemonConnectionService.cs`.
  - `instances`: `ViewModels/InstanceManagerViewModel.cs`.
  - `console`: `InstanceConsole/Modules/InstanceDataManager.cs`, `CommandPage`.
  - `files`: `FileManagerPage`, `FileEditorWindow`, `ComponentScanner`, `ComponentManagerViewModel`.
  - `settings`: `InstanceSettingsViewModel`, `InstanceSettingsModel`.
  - `create`: Java/Fabric/Forge/NeoForge providers and `SelectMinecraftJavaJvm`.

## 3. Action Parity Inventory

All rows require a frozen descriptor with params/result `JsonTypeInfo`, permission, daemon binding, daemon-client typed mapping, golden case, and catalog coverage. `Unit` success serializes as `{}`.

| V1 action | V2 method/transport | V1 params -> result | Permission | Application/transport owner | Current client -> target / WPF | Evidence and migration requirement |
|---|---|---|---|---|---|---|
| `SubscribeEvent` | `mcsl.event.subscribe` | `SubscribeEventParameter` -> empty | `*` | connection subscription adapter | `SubscribeEvent` -> typed subscription / `console` | `LegacyEventSubscriptionMigrationCharacterizationTests` freezes the successful request shape/ack and internal reconnect replay intent without retaining pre-ack mutation; V2 adds wildcard, explicit-null, unknown-event, idempotency, and disconnect-race tests. |
| `UnsubscribeEvent` | `mcsl.event.unsubscribe` | `UnsubscribeEventParameter` -> empty | `*` | connection subscription adapter | `UnSubscribeEvent` -> subscription disposal / `console` | `LegacyEventSubscriptionMigrationCharacterizationTests` freezes the successful request shape/ack only. V2 must commit reconnect intent after the server acknowledgement and add disposal/race tests. |
| `Ping` | `mcsl.daemon.ping` | empty -> `PingResult` | `*` | transport | `PingAsync` -> typed transport ping / `instances` | Preserve timing payload; add JSON-RPC id/profile/notification tests. |
| `GetSystemInfo` | `mcsl.system.info.get` | empty -> `GetSystemInfoResult` | `*` | `ISystemApplication.GetInfoAsync` | `GetSystemInfoAsync` / `instances`, settings | Preserve timed-cell behavior; propagate caller cancellation at the application boundary. |
| `GetPermissions` | `mcsl.auth.permissions.get` | empty -> `GetPermissionsResult` | `*` | auth adapter | `GetPermissions` -> typed connection permissions / none | Freeze deterministic ordering and wildcard/empty behavior. |
| `GetJavaList` | `mcsl.java.list` | empty -> `GetJavaListResult` | `mcsl.daemon.java_list` | `ISystemApplication.GetJavaListAsync` | `GetJavaListAsync` / settings, create | Add scanner error/cancellation characterization; return typed safe errors. |
| `GetDirectoryInfo` | `mcsl.directory.info.get` | `GetDirectoryInfoParameter` -> `GetDirectoryInfoResult` | `mcsl.daemon.file.info.directory` | `IFileApplication.GetDirectoryInfoAsync` | `GetDirectoryInfoAsync` / `files` | Preserve metadata intent; add path-boundary, not-found, access, ordering tests. |
| `GetFileInfo` | `mcsl.file.info.get` | `GetFileInfoParameter` -> `GetFileInfoResult` | `mcsl.daemon.file.info.file` | `IFileApplication.GetFileInfoAsync` | `GetFileInfoAsync` / `files`, settings | Preserve metadata intent; add path-boundary and error mapping tests. |
| `FileUploadRequest` | `mcsl.file.upload.open` | `FileUploadRequestParameter` -> `FileUploadRequestResult` | `mcsl.daemon.file.upload` | `IFileApplication.OpenUploadAsync` | `UploadFileAsync` -> typed upload session / `files`, settings, create | Declare size + SHA-256, connection ownership, 30-minute expiry, 1 MiB max chunk. |
| `FileUploadChunk` | base64 action deleted; V2 uses binary `upload_chunk` + control ack | `FileUploadChunkParameter` -> `FileUploadChunkResult` | `mcsl.daemon.file.upload` | `IFileApplication.WriteUploadChunkAsync` | no public wrapper; current upload helper uses the separate raw-binary shortcut | Preserve only the business operation. Strict offset, one in-flight chunk, typed ack, malformed-frame and disconnect cleanup tests replace both V1 chunk paths. |
| `FileUploadCancel` | `mcsl.file.upload.cancel` | `FileUploadCancelParameter` -> empty | `mcsl.daemon.file.upload` | `IFileApplication.CancelUploadAsync` | upload cancellation / same | Idempotent owner-scoped cancellation; no heuristic ack. |
| implicit V1 upload completion | `mcsl.file.upload.close` | none -> none | `mcsl.daemon.file.upload` | `IFileApplication.CloseUploadAsync` | typed upload session close / same | New explicit V2 control: validate exact size/SHA-256 before atomic commit. |
| `FileDownloadRequest` | `mcsl.file.download.open` | `FileDownloadRequestParameter` -> `FileDownloadRequestResult` | `mcsl.daemon.file.download` | `IFileApplication.OpenDownloadAsync` | `DownloadFileAsync` -> typed download session / `files`, settings | Return size/SHA-256/max chunk/expiry and bind session to connection. |
| `FileDownloadRange` | `mcsl.file.download.read` + binary `download_chunk` | `FileDownloadRangeParameter` -> `FileDownloadRangeResult` | `mcsl.daemon.file.download` | `IFileApplication.ReadDownloadAsync` | range loop -> binary session / same | Response + binary frame is one ordered two-frame message; one in-flight read. |
| `FileDownloadClose` | `mcsl.file.download.close` | `FileDownloadCloseParameter` -> empty | `mcsl.daemon.file.download` | `IFileApplication.CloseDownloadAsync` | download cleanup / same | Invalid owner/session is typed failure, not swallowed success. |
| `DeleteFile` | `mcsl.file.delete` | `DeleteFileParameter` -> empty | `mcsl.daemon.file.delete.file` | `IFileApplication.DeleteFileAsync` | `DeleteFileAsync` / `files` | Add path trust, not-found/access/in-use tests. |
| `DeleteDirectory` | `mcsl.directory.delete` | `DeleteDirectoryParameter` -> empty | `mcsl.daemon.file.delete.directory` | `IFileApplication.DeleteDirectoryAsync` | `DeleteDirectoryAsync` / `files` | Preserve recursive flag; add non-empty/collision/error tests. |
| `RenameFile` | `mcsl.file.rename` | `RenameFileParameter` -> empty | `mcsl.daemon.file.rename.file` | `IFileApplication.RenameFileAsync` | `RenameFileAsync` / `files` | Validate destination name and collision at daemon boundary. |
| `RenameDirectory` | `mcsl.directory.rename` | `RenameDirectoryParameter` -> empty | `mcsl.daemon.file.rename.directory` | `IFileApplication.RenameDirectoryAsync` | `RenameDirectoryAsync` / `files` | Same as file rename, directory semantics. |
| `CreateDirectory` | `mcsl.directory.create` | `CreateDirectoryParameter` -> empty | `mcsl.daemon.file.create.directory` | `IFileApplication.CreateDirectoryAsync` | `CreateDirectoryAsync` / `files` | Add existing-path/access/path-boundary tests. |
| `MoveFile` | `mcsl.file.move` | `MoveFileParameter` -> empty | `mcsl.daemon.file.move.file` | `IFileApplication.MoveFileAsync` | `MoveFileAsync` / `files` | Add cross-volume/collision/failure mapping tests. |
| `MoveDirectory` | `mcsl.directory.move` | `MoveDirectoryParameter` -> empty | `mcsl.daemon.file.move.directory` | `IFileApplication.MoveDirectoryAsync` | `MoveDirectoryAsync` / `files` | Add descendant/self move and collision tests. |
| `CopyFile` | `mcsl.file.copy` | `CopyFileParameter` -> empty | `mcsl.daemon.file.copy.file` | `IFileApplication.CopyFileAsync` | `CopyFileAsync` / `files` | Add overwrite/collision/access tests. |
| `CopyDirectory` | `mcsl.directory.copy` | `CopyDirectoryParameter` -> empty | `mcsl.daemon.file.copy.directory` | `IFileApplication.CopyDirectoryAsync` | `CopyDirectoryAsync` / `files` | Add recursive copy/collision/failure tests. |
| `AddInstance` | `mcsl.instance.create` | `AddInstanceParameter` -> `AddInstanceResult` | `*` | `IInstanceApplication.CreateInstanceAsync` | `AddInstanceAsync` / `create` | Preserve factory-driven creation; persist before snapshot publish. |
| `RemoveInstance` | `mcsl.instance.remove` | `RemoveInstanceParameter` -> empty | `*` | `IInstanceApplication.RemoveInstanceAsync` | `RemoveInstanceAsync` / `instances` | Preserve running-state refusal; persist/remove then publish snapshot. |
| `StartInstance` | `mcsl.instance.start` | `StartInstanceParameter` -> empty | `*` | `IInstanceApplication.StartInstanceAsync` | `StartInstanceAsync` / `instances`, `console` | Preserve manager semantics and log-event attachment through typed event publisher. |
| `StopInstance` | `mcsl.instance.stop` | `StopInstanceParameter` -> empty | `*` | `IInstanceApplication.StopInstanceAsync` | `StopInstanceAsync` / `instances`, `console` | Preserve graceful request semantics; distinguish not-found/bad-state with typed errors. |
| `KillInstance` | `mcsl.instance.halt` | `KillInstanceParameter` -> empty | `*` | `IInstanceApplication.HaltInstanceAsync` | `KillInstanceAsync` -> halt / `instances`, `console` | `Ok(Unit)` means the frozen void signal returned; missing instance/process is still a no-op; real kill exceptions propagate to host mapping. |
| `SendToInstance` | `mcsl.instance.command.send` | `SendToInstanceParameter` -> empty | `*` | `IInstanceApplication.SendCommandAsync` | `SentToInstanceAsync` / `instances`, `console` | Preserve not-found vs non-running distinction; no transport strings in WPF. |
| `GetInstanceReport` | `mcsl.instance.report.get` | `GetInstanceReportParameter` -> `GetInstanceReportResult` | `*` | `IInstanceApplication.GetReportAsync` | `GetInstanceReportAsync` / `instances`, `console` | Keep high-frequency report outside the COW catalog. |
| `GetAllReports` | `mcsl.instance.report.list` | empty -> `GetAllReportsResult` | `*` | `IInstanceApplication.ListReportsAsync` | `GetAllReportsAsync` / `instances` | Keep high-frequency reports outside the COW catalog. |
| `GetInstanceLogHistory` | `mcsl.instance.log.get` | `GetInstanceLogHistoryParameter` -> `GetInstanceLogHistoryResult` | `*` | `IInstanceApplication.GetLogAsync` | `GetInstanceLogHistoryAsync` / `console` | Add not-found/retention/order tests. |
| `GetInstanceSettings` | `mcsl.instance.settings.get` | `GetInstanceSettingsParameter` -> `GetInstanceSettingsResult` | `*` | `IInstanceApplication.GetSettingsAsync` | `GetInstanceSettingsAsync` / `settings` | Remove sync-over-async; preserve domain DTO after rehoming from V1 Action namespace. |
| `UpdateInstanceSettings` | `mcsl.instance.settings.update` | `UpdateInstanceSettingsParameter` -> `UpdateInstanceSettingsResult` | `*` | `IInstanceApplication.UpdateSettingsAsync` | `UpdateInstanceSettingsAsync` / `settings` | Pass cancellation; persist before runtime/snapshot commit; preserve coordinator rules. |
| `GetEventRules` | `mcsl.instance.event-rules.get` | `GetEventRulesParameter` -> `GetEventRulesResult` | `*` | `IEventRuleApplication.GetAsync` | `GetEventRulesAsync` / `console` | Preserve discriminator/persistence format; add handler/application tests. |
| `SaveEventRules` | `mcsl.instance.event-rules.update` | `SaveEventRulesParameter` -> empty | `*` | `IEventRuleApplication.UpdateAsync` | `SaveEventRulesAsync` / `console` | Persist first, then atomically replace live rules; write failure must leave current state unchanged. |

### Per-definition metadata, call-site, test, and cancellation evidence

Notation: `P(X)` means `ActionParametersContext.Default.X`; `R(X)` means `ActionResultsContext.Default.X`. Every target descriptor moves both sides to `BuiltInProtocolJsonContext` and the catalog coverage test. `REG` means current `ActionRegistryCharacterizationTests` plus `ActionHandlerInventoryTests`; it proves registration/classification/permission only, not behavior.

| V1 action | Current source `JsonTypeInfo` -> target | Exact current daemon-client/WPF symbol | Existing behavior evidence; required addition | Cancellation: V1 -> V2 |
|---|---|---|---|---|
| `SubscribeEvent` | `P(SubscribeEventParameter)` / `R(EmptyActionResult)` -> built-in descriptor | `DaemonExtensions.SubscribeEvent`; `InstanceDataManager.InitializeAsync:110` | `REG`, `RpcGoldenCharacterizationTests`, `DaemonClientTransportModernizationTests`, `LegacyEventSubscriptionMigrationCharacterizationTests`; add typed filter/idempotency/disconnect tests | ignored sync token -> connection request token |
| `UnsubscribeEvent` | `P(UnsubscribeEventParameter)` / `R(EmptyActionResult)` -> built-in descriptor | `DaemonExtensions.UnSubscribeEvent`; `InstanceDataManager.DisposeAsync:354` | `REG`, reconnect/convergence seams, `LegacyEventSubscriptionMigrationCharacterizationTests`; add server-acknowledged disposal/race tests | ignored sync token -> connection request token |
| `Ping` | `P(EmptyActionParameter)` / `R(PingResult)` -> built-in descriptor | `DaemonExtensions.PingAsync`; `InstanceDataManager.GetDaemonLatencyAsync:263` | `REG`, ping golden and real dispatch; add JSON-RPC id/profile/notification tests | ignored sync token/client wait only -> request cancellation |
| `GetSystemInfo` | `P(EmptyActionParameter)` / `R(GetSystemInfoResult)` -> built-in descriptor | `GetSystemInfoAsync`; `DaemonManagerViewModel:223`, `InstanceManagerViewModel:216` | `REG`, inbound success seam; add application error/cancel test | handler receives token but timed cell ignores -> application token last |
| `GetPermissions` | `P(EmptyActionParameter)` / `R(GetPermissionsResult)` -> built-in descriptor | `GetPermissions`; no WPF caller | `REG` only; add ordering/empty/wildcard tests | ignored sync token -> request cancellation |
| `GetJavaList` | `P(EmptyActionParameter)` / `R(GetJavaListResult)` -> built-in descriptor | `GetJavaListAsync`; `InstanceSettingsViewModel:329`, `SelectMinecraftJavaJvm:151,211` | `REG` only; add scanner success/error/cancel tests | handler token ignored by timed cell/scanner -> application token last |
| `GetDirectoryInfo` | `P(GetDirectoryInfoParameter)` / `R(GetDirectoryInfoResult)` -> built-in descriptor | `GetDirectoryInfoAsync`; `ComponentScanner:67,79`, `FileManagerPage:454,512` | `REG`, representative success in `LegacyFileOperationsMigrationCharacterizationTests`; add V2 path/error/order tests | ignored sync token -> application token last |
| `GetFileInfo` | `P(GetFileInfoParameter)` / `R(GetFileInfoResult)` -> built-in descriptor | `GetFileInfoAsync`; no direct WPF caller | `REG`, representative success in `LegacyFileOperationsMigrationCharacterizationTests`; add V2 path/error tests | ignored sync token -> application token last |
| `FileUploadRequest` | `P(FileUploadRequestParameter)` / `R(FileUploadRequestResult)` -> upload-open descriptor | internal to `UploadFileAsync`; users: `ComponentManagerViewModel:296`, `InstanceSettingsViewModel:208`, `FileManagerPage:647`, `FileEditorWindow:400`, Java provider `:237` | `REG` only; add owner/permission/size/hash/expiry tests | ignored sync token -> application/connection token |
| `FileUploadChunk` | `P(FileUploadChunkParameter)` / `R(FileUploadChunkResult)` -> binary header + typed ack metadata | no direct public wrapper; `UploadFileAsync` bypasses it through raw binary | `REG`; V2 malformed/offset/ownership tests; raw shortcut evidence is listed separately below | handler token accepted but FileManager ignores -> chunk token last |
| `FileUploadCancel` | `P(FileUploadCancelParameter)` / `R(EmptyActionResult)` -> upload-cancel descriptor | internal `UploadFileAsync` cancellation | `REG`, public context cancellation in `LegacyFileTransferMigrationCharacterizationTests`; add V2 owner/idempotency/disconnect tests | ignored sync token -> application token last |
| `FileDownloadRequest` | `P(FileDownloadRequestParameter)` / `R(FileDownloadRequestResult)` -> download-open descriptor | internal `DownloadFileAsync`; users: `ComponentScanner:116`, `FileManagerPage:613`, `FileEditorWindow:296` | `REG`, real open/helper/progress evidence in `LegacyFileTransferMigrationCharacterizationTests`; add V2 owner/permission/SHA-256/expiry tests | handler token accepted but FileManager ignores -> application token last |
| `FileDownloadRange` | `P(FileDownloadRangeParameter)` / `R(FileDownloadRangeResult)` -> read descriptor + binary metadata | internal `DownloadFileAsync` range loop | `REG`, even-byte range and helper chunk evidence in `LegacyFileTransferMigrationCharacterizationTests`; add V2 bytes-read/two-frame/ownership tests and do not retain string transport | handler token accepted but FileManager ignores -> read token last |
| `FileDownloadClose` | `P(FileDownloadCloseParameter)` / `R(EmptyActionResult)` -> close descriptor | internal `DownloadFileAsync` cleanup | `REG`, close/context-cancel cleanup in `LegacyFileTransferMigrationCharacterizationTests`; add V2 invalid-owner/error/disconnect tests | ignored sync token -> application token last |
| `DeleteFile` | `P(DeleteFileParameter)` / `R(EmptyActionResult)` -> built-in descriptor | `DeleteFileAsync`; `ComponentManagerViewModel:168`, `FileManagerPage:739` | `REG`, success/missing/path-boundary evidence in `LegacyFileOperationsMigrationCharacterizationTests`; add V2 access/in-use mapping tests | ignored sync token -> application token last |
| `DeleteDirectory` | `P(DeleteDirectoryParameter)` / `R(EmptyActionResult)` -> built-in descriptor | `DeleteDirectoryAsync`; `FileManagerPage:735` | `REG`, representative recursive/non-recursive success in `LegacyFileOperationsMigrationCharacterizationTests`; add V2 non-empty/collision/error tests | ignored sync token -> application token last |
| `RenameFile` | `P(RenameFileParameter)` / `R(EmptyActionResult)` -> built-in descriptor | `RenameFileAsync`; `ComponentScanner:57`, `FileManagerPage:706` | `REG`, representative success in `LegacyFileOperationsMigrationCharacterizationTests`; add V2 collision/name/path tests | ignored sync token -> application token last |
| `RenameDirectory` | `P(RenameDirectoryParameter)` / `R(EmptyActionResult)` -> built-in descriptor | `RenameDirectoryAsync`; `FileManagerPage:702` | `REG`, representative success in `LegacyFileOperationsMigrationCharacterizationTests`; add V2 collision/name/path tests | ignored sync token -> application token last |
| `CreateDirectory` | `P(CreateDirectoryParameter)` / `R(EmptyActionResult)` -> built-in descriptor | `CreateDirectoryAsync`; `FileManagerPage:767` | `REG`, success and out-of-root rejection in `LegacyFileOperationsMigrationCharacterizationTests`; add V2 existing/access tests | ignored sync token -> application token last |
| `MoveFile` | `P(MoveFileParameter)` / `R(EmptyActionResult)` -> built-in descriptor | `MoveFileAsync`; no WPF caller | `REG`, success/missing-source evidence in `LegacyFileOperationsMigrationCharacterizationTests`; add V2 cross-volume/collision tests | ignored sync token -> application token last |
| `MoveDirectory` | `P(MoveDirectoryParameter)` / `R(EmptyActionResult)` -> built-in descriptor | `MoveDirectoryAsync`; no WPF caller | `REG`, success/missing-source evidence in `LegacyFileOperationsMigrationCharacterizationTests`; add V2 self/descendant/collision tests | ignored sync token -> application token last |
| `CopyFile` | `P(CopyFileParameter)` / `R(EmptyActionResult)` -> built-in descriptor | `CopyFileAsync`; no WPF caller | `REG`, success/missing-source/out-of-root evidence in `LegacyFileOperationsMigrationCharacterizationTests`; add V2 overwrite/collision/access tests | ignored sync token -> application token last |
| `CopyDirectory` | `P(CopyDirectoryParameter)` / `R(EmptyActionResult)` -> built-in descriptor | `CopyDirectoryAsync`; no WPF caller | `REG`, recursive success/missing-source evidence in `LegacyFileOperationsMigrationCharacterizationTests`; add V2 collision/failure tests | ignored sync token -> application token last |
| `AddInstance` | `P(AddInstanceParameter)` / `R(AddInstanceResult)` -> built-in descriptor | `AddInstanceAsync`; Java/Fabric/Forge/NeoForge providers `:286/:202/:202/:217` | `REG`, factory/validation tests; add application persistence/snapshot test | passes token to `TryAddInstance` -> application token last |
| `RemoveInstance` | `P(RemoveInstanceParameter)` / `R(EmptyActionResult)` -> built-in descriptor | `RemoveInstanceAsync`; `InstanceManagerViewModel:416` | `REG`, lifecycle state seams; add application error/snapshot test | ignored sync token -> application token last |
| `StartInstance` | `P(StartInstanceParameter)` / `R(EmptyActionResult)` -> built-in descriptor | `StartInstanceAsync`; `InstanceManagerViewModel:271`, `InstanceDataManager:163` | `REG`, `InstanceCommandTests`, action failure seams; add typed event attachment/application test | passes token to `TryStartInstance` -> application token last |
| `StopInstance` | `P(StopInstanceParameter)` / `R(EmptyActionResult)` -> built-in descriptor | `StopInstanceAsync`; `InstanceManagerViewModel:308`, `InstanceDataManager:183` | `REG`, `InstanceCommandTests`; add typed not-found/state mapping | ignored sync token -> application token last |
| `KillInstance` | `P(KillInstanceParameter)` / `R(EmptyActionResult)` -> built-in descriptor | `KillInstanceAsync`; `InstanceManagerViewModel:380`, `InstanceDataManager:203` | `REG`, `InstanceManagerKillTests`, `InstanceCommandTests` fully freeze signal semantics | ignored sync token -> application token accepted; void signal itself is non-cancelable |
| `SendToInstance` | `P(SendToInstanceParameter)` / `R(EmptyActionResult)` -> built-in descriptor | `SentToInstanceAsync`; `InstanceDataManager:243` | `REG` only; add not-found/non-running mapping | ignored sync token -> application token last |
| `GetInstanceReport` | `P(GetInstanceReportParameter)` / `R(GetInstanceReportResult)` -> built-in descriptor | `GetInstanceReportAsync`; `InstanceDataManager:143` | `REG`, limited convergence success; add not-found/cancel test | passes token to manager -> application token last |
| `GetAllReports` | `P(EmptyActionParameter)` / `R(GetAllReportsResult)` -> built-in descriptor | `GetAllReportsAsync`; `InstanceManagerViewModel:132` | `REG`, limited success; add immutable result/cancel test | passes token to manager -> application token last |
| `GetInstanceLogHistory` | `P(GetInstanceLogHistoryParameter)` / `R(GetInstanceLogHistoryResult)` -> built-in descriptor | `GetInstanceLogHistoryAsync`; `InstanceDataManager:282`, `CommandPage:109` | `REG` only; add order/not-found test | ignored sync token -> application token last |
| `GetInstanceSettings` | `P(GetInstanceSettingsParameter)` / `R(GetInstanceSettingsResult)` -> built-in descriptor | `GetInstanceSettingsAsync`; `InstanceSettingsViewModel:93` | `REG`, settings golden, `InstanceSettingsCoordinatorTests`; add no-sync-over-async application test | sync-over-async, token ignored -> true async token last |
| `UpdateInstanceSettings` | `P(UpdateInstanceSettingsParameter)` / `R(UpdateInstanceSettingsResult)` -> built-in descriptor | `UpdateInstanceSettingsAsync`; `InstanceSettingsViewModel:218` | `REG`, `InstanceSettingsCoordinatorTests`; add persistence/snapshot/cancel tests | handler receives token but manager ignores -> application token last |
| `GetEventRules` | `P(GetEventRulesParameter)` / `R(GetEventRulesResult)` -> built-in descriptor | `GetEventRulesAsync`; `InstanceDataManager:301`, `EventTriggerViewModel:38` | `REG`, discriminator/persistence tests; add handler/application not-found test | ignored sync token -> application token last |
| `SaveEventRules` | `P(SaveEventRulesParameter)` / `R(EmptyActionResult)` -> built-in descriptor | `SaveEventRulesAsync`; `InstanceDataManager:320`, `EventTriggerViewModel:60` | `REG`, nested golden/discriminator/persistence; add failure-atomicity test | ignored sync token -> application token last |

### New V2 state synchronization

`mcsl.instance.catalog.get` and `mcsl.event.instance.catalog.changed` have no V1 equivalent. They are a new consistency protocol, not a compatibility path. Tests must cover subscribe-before-read, buffered deltas, version gaps, conflicting duplicates, reconnect resync, stale snapshot visibility, and retained historical states.

## 4. Event And Notification Inventory

| V1 surface -> V2 | Current source metadata -> target | Exact producer, client, and WPF symbols | Existing tests; required target tests | Cancellation/disposal |
|---|---|---|---|---|
| `EventType.InstanceLog` / `EventPacket` -> `mcsl.event.instance.log` | `RpcEnvelopeContext.Default.EventPacket`; `EventDataContext.Default.InstanceLogEventMeta` + `.InstanceLogEventData` -> built-in typed event descriptor | producer wiring `HandleStartInstance.HandleAsync` -> `IEventService.OnInstanceLog` -> `EventService.OnInstanceLog` -> `WsEventPlugin`; client `WsReceivedPlugin.MaterializeEventMeta/MaterializeEventData` -> `Daemon.InstanceLogEvent`; WPF `InstanceDataManager.InitializeAsync:108-111`, `OnInstanceLog:329`, `DisposeAsync:354-364` | `RpcGoldenCharacterizationTests`, `ConverterParityCharacterizationTests`, `DaemonOutboundTransportSerializationTests`, `DaemonDaemonClientTransportConvergenceTests`, `DaemonClientInboundTransportParsingTests`; add exact/wildcard/explicit-null, ordering, serialization-once, slow-consumer, reconnect, and headless WPF tests | V1 publish/subscription has no request cancellation; connection close clears server state. V2 publish is awaited with token, remote enqueue is non-blocking, and caller-held typed subscription disposal controls reconnect restoration. |
| `EventType.DaemonReport` / `EventPacket` -> `mcsl.event.daemon.report` | `RpcEnvelopeContext.Default.EventPacket`; no meta type; `EventDataContext.Default.DaemonReportEventData` -> built-in typed event descriptor | producer `DaemonServiceComposition.AttachDaemonLifecycle` timer -> `IEventService.OnDaemonReport` -> `EventService.OnDaemonReport` -> `WsEventPlugin`; client `WsReceivedPlugin.MaterializeEventData`; no dedicated WPF consumer | daemon-report golden, outbound serialization, convergence and client parsing tests; add cadence-independent typed publish, ordering, slow-consumer, reconnect tests | timer has no caller token; daemon stop stops timer. V2 publisher uses daemon lifetime token, and connection/subscription disposal follows the typed owner model. |
| direct `NotificationPacket` -> `mcsl.event.notification` | `RpcEnvelopeContext.Default.NotificationPacket` -> typed notification meta/data `JsonTypeInfo` in built-in descriptor | producer `EventTriggerService.SendNotificationAsync` direct websocket broadcast; parser/callback `WsReceivedPlugin.ParseNotificationPacket`, `OnNotificationReceived`; public `TouchSocketClientTransport` does not wire this callback and WPF has no daemon-notification consumer | `PublicContractSafetyTests`, `PublicSurfaceLeakageTests`, `DaemonClientInboundTransportParsingTests`, `EventTriggerNotificationCharacterizationTests` for the real producer payload/source/rule fields without freezing all-connection fan-out; add bounded queue, typed client, and WPF notification-flow tests | V1 direct broadcast has no publish token/owner disposal. V2 event-rule application publishes with its token; remote subscription and connection cleanup use normal typed event ownership. |
| `RelayPacket` -> deleted | `RpcEnvelopeContext.Default.RelayPacket` -> none | no daemon producer; parser/callback only in `WsReceivedPlugin.ParseRelayPacket`, `OnRelayReceived`; no public transport/WPF wiring | Common public-contract and client heuristic-parser tests are deletion evidence only | no lifecycle to preserve; delete parser/callback/contracts. |

### Legacy raw-binary upload shortcut

This is a separate V1 transport surface, not `ActionType.FileUploadChunk`:

| Surface | Current metadata/layout | Exact path | Evidence | Cancellation/ownership target |
|---|---|---|---|---|
| raw upload frame + heuristic text ack -> versioned binary `upload_chunk` + typed control ack | no `JsonTypeInfo` for inbound binary; 44-byte `[Guid .NET bytes][host-endian Int64][SHA1][payload]`; ack uses daemon-local `BinaryUploadResponse`/`BinaryUploadErrorResponse` resolved by `DaemonRpcTypeInfoCache`, then client root-property detection | client `DaemonExtensions.UploadFileAsync:208` -> `Daemon.SendBinaryFileChunkAsync` -> `ClientConnection.SendBinaryAsync` -> `TouchSocketClientTransport.SendBinaryAsync`; daemon `WsActionPlugin.OnWebSocketReceived:39-42` -> private `HandleBinaryFileUploadChunk`; client ack `WsReceivedPlugin.ParseInboundEnvelopeFromBytes` | `LegacyBinaryUploadMigrationCharacterizationTests` drives the real daemon seam and partial ack; `LegacyBinaryUploadMigrationBenchmarks` records 64 KiB frame build and ack parse; V2 adds header/endian/malformed/offset/hash/ownership/expiry/disconnect/ack-without-subscription/two-frame tests | V1 pending ack is keyed only by file id and can survive disconnect; daemon does not enforce permission or `WsContext` ownership. V2 binds session to connection+permission, allows one in-flight chunk, cancels on disconnect/stop/expiry, and routes ack through the connection-owned control queue. |

Remote event envelopes preserve field state exactly: missing means absent, present `null` means explicit null, and object/value means a typed payload. Filterable metadata uses a concrete immutable type with unmapped-member rejection and canonical reserialization; `JsonElement` is not a filter contract.

## 5. V1 Bugs And Debt Explicitly Excluded From Parity

1. Raw upload bypasses permission and connection ownership, has host-endian/no-version headers, silently drops short frames, leaves completed sessions registered, uses heuristic plain JSON acknowledgements, and can leave client waits pending forever.
2. Download range parsing uses an unsafe regex, `int` offsets, ignores actual bytes read, converts arbitrary bytes through `BigEndianUnicode`, and swallows invalid close errors.
3. Event fan-out uses an unbounded global channel and `Task.WhenAll`, so one slow connection blocks all; subscription values contain unsynchronized `HashSet` instances; null-meta InstanceLog subscription is not a wildcard despite the intended model.
4. Notification bypasses the event bus and subscription model; relay is a dead envelope.
5. Daemon requests have no per-request cancellation; `OperationCanceledException` becomes an unexpected error; settings use sync-over-async or omit tokens; exception text/details can leak onto the wire.
6. Event-rule save mutates the live list before persistence and does not roll back on write failure.
7. `FileSystemWatcherPlugin` / `InstancesManagerFsWatcher` mutate runtime instance state outside the application command boundary.
8. Dual registry/reflection fallback, array event batching, V1 required-null quirks, root-property envelope guessing, and unknown-enum parser details are transport debt, not business behavior.
9. DaemonClient has two independent `SubscribedEvents` instances, clears a copy rather than the real set, records subscribe intent before server success, removes unsubscribe intent before server success, and does not wire notification/relay callbacks into the public daemon path. None of these bugs is retained.

## 6. Existing Evidence To Preserve Or Migrate

- Registry/inventory: `ActionRegistryCharacterizationTests`, `ActionHandlerInventoryTests`, `GeneratedRegistryParityTests`.
- Parser/error/null/cancellation: `ActionFailureParityCharacterizationTests`, `ConverterParityCharacterizationTests`, `DaemonInboundTransportPipelineTests`.
- Golden migration inputs: `RpcGoldenCharacterizationTests` and `Fixtures/Rpc/**`; fixtures may remain only as migration input.
- Event/transport seams: `DaemonOutboundTransportSerializationTests`, `DaemonDaemonClientTransportConvergenceTests`, `DaemonClientInboundTransportParsingTests`.
- Daemon-client pending/reconnect: `DaemonClientTransportModernizationTests`, `DaemonClientOutboundTransportAndCallbackTests`.
- Phase 0 transport/application seams: `LegacyBinaryUploadMigrationCharacterizationTests`, `LegacyDaemonClientRestartMigrationCharacterizationTests`, `LegacyFileTransferMigrationCharacterizationTests`, `LegacyEventSubscriptionMigrationCharacterizationTests`, `LegacyFileOperationsMigrationCharacterizationTests`, and `EventTriggerNotificationCharacterizationTests`.
- Lifecycle: `InstanceManagerKillTests`, `InstanceCommandTests`, `InstanceSettingsCoordinatorTests`.
- Event rules/persistence: `EventRuleDiscriminatorCharacterizationTests`, `PersistenceMigrationCharacterizationTests`.
- Source-generation/public surface: `PublicContracts/**`, `JsonBoundaryConfigurationTests`.
- Performance: xUnit performance gates, existing BenchmarkDotNet V1 classes, and `PerformanceGateHarness` patterns.

Phase 0 added the six migration/producer test classes listed above plus `LegacyBinaryUploadMigrationBenchmarks`; the final Release protocol suite passes 382/382. The remaining rows marked “add” are V2 implementation/acceptance tests or legacy-bug corrections, not contracts that require more V1 runtime assertions before Phase 1. Before each corresponding V1 path is deleted, its target application/transport tests must pass: complete file error/ownership semantics; V2 binary ranges and two-frame ordering; event backpressure/ordering; event-rule persistence atomicity; public typed reconnect subscriptions; and WPF file-progress/cancellation/notification flows.

## 7. Exact Deletion Manifest

### Common

- Delete/replace V1 envelopes and discriminators under `ProtoType/Action`: `ActionType`, packet, request status, retcode, execution method, marker interfaces, and empty wrappers.
- Rehome domain-owned instance settings/install metadata, event-rule, file metadata, reports, and factory-setting DTOs before deleting V1 marker/base types.
- Delete/replace `ProtoType/Event` V1 enum/packet/marker/extensions; rehome typed event payloads.
- Delete `ProtoType/Relay/RelayPacket.cs`; migrate notification payload and delete `NotificationPacket` envelope.
- Replace V1 serializer contexts/resolver registrations and delete `.Resources/proto_type.yml`.

### Daemon

- Delete `Remote/Action/**`, `WsActionPlugin`, `WsEventPlugin`, and old `Remote/Event` bus interfaces/implementation after application/catalog bindings replace them.
- Refactor `EventTriggerService` to application services and typed publisher; remove direct websocket notification and manager access.
- Replace legacy `WsContext` subscription/session state and connection cleanup.
- Replace `/api/v1` registration/verification/public URLs/info responses and embedded V1 docs.
- Delete `FileSystemWatcherPlugin` and `InstancesManagerFsWatcher` runtime mutation path.
- Remove generated/reflection action-registry selection, `UseGeneratedActionRegistry`, analyzer reference, and startup selection.
- Replace V1 RPC serializer contexts/caches and delete reflection fallback policy after catalog metadata coverage is complete.

### Generator

- Delete `generators/MCServerLauncher.Daemon.Generators/**`, solution entry, registry tests, release tracking, startup benchmarks, and generated/legacy runtime switch.

### Daemon client

- Replace `IDaemon`, `Daemon`, `DaemonExtensions`, `RequestAsync(ActionType)`, `SubscribedEvents`, `DaemonRequestException` retcode model, and `UploadContext`.
- Rewrite `ClientConnection`, endpoint, pending calls, binary control, typed subscriptions, ready/resync, and reconnect state.
- Delete root-property heuristic parsing and V1 action/event/binary-upload/notification/relay branches.
- Replace V1 serializer contexts/caches/fallback policy and action/event metrics.

### WPF

- Replace all `DaemonExtensions` consumers with typed application services/subscriptions, especially connection, instance manager, console, file manager/editor/scanner, settings, and create-instance providers.
- Remove direct `EventType` references and map `DaemonError.Code` through resource-backed i18n.

### Docs, tests, and benchmarks

- Replace static V1 Apifox and action/event/file-transfer topics with catalog-generated V2 docs and plugin guidance.
- Delete or rewrite V1 runtime registry/parser/endpoint/client tests; keep explicit migration fixtures only under the deletion-script allowlist.
- Replace all current V1 protocol benchmarks and reflection/generated registry startup benchmarks with V2 parse/dispatch/serialization, frozen catalog, published state, local event, remote fan-out, binary, and daemon-client round-trip benchmarks.
- Add `tools/VerifyNoV1Runtime.ps1`, protocol docs tool, performance gate, normalized V1 baseline, and plugin integration projects.

## 8. Phase 4 Deletion Proof

The cutover is not complete until all non-allowlisted runtime matches for `/api/v1`, `ActionType`, `ActionRequest`, `ActionResponse`, `ActionError`, `ActionRetcode`, `ActionRequestStatus`, `EventType`, `EventPacket`, `RelayPacket`, V1 registry selection, and V1 envelope heuristics are gone. The script must also verify that the generator project directory and solution entry do not exist.

## Changelog

- 2026-07-11: Captured the 36-action V1 baseline, current event/notification/binary behavior, benchmark evidence, application/V2 ownership, known bugs excluded from parity, reusable tests, and exact deletion manifest.
