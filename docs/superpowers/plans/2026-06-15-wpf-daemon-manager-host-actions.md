# WPF Daemon Manager Host Actions Plan

Date: 2026-06-15

## Touched Areas

- `frontend`: daemon manager WPF page, card model, and view model.
- `protocol`: extend the existing daemon `GetSystemInfoAsync` system-resource contract with daemon version metadata and multi-drive data.
- `serialization`: keep the extended status records `System.Text.Json` source-generation friendly.
- `workflow`: focused WPF build and git hygiene checks.

## Goal

Complete remote host management in the WPF daemon manager by making edit/delete flows reliable and by showing remote host CPU, memory, disk, OS, and daemon-version state on each host card. Keep daemon and instance manager refresh behavior configurable from the page.

## Tasks

- [x] Keep edit operations from removing the original host until the replacement connection is validated.
- [x] Keep delete operations behind the existing countdown confirmation and ensure the daemon connection is closed before local removal.
- [x] Add daemon card model fields for resource usage display.
- [x] Render CPU, memory, and drive usage in `DaemonManagerPage`.
- [x] Add resource-label translations.
- [x] Build WPF and run final git hygiene checks.
- [x] Add configurable auto-refresh controls to daemon manager and instance manager pages.
- [x] Include remote OS and daemon version in daemon manager cards.
- [x] Rework daemon card padding/layout so resource usage stays compact.
- [x] Adapt disk usage reporting to multiple mounted drives/partitions.
- [x] Re-run WPF, protocol, and git hygiene checks after the refresh/version extension.

## Verification

- `dotnet build MCServerLauncher.WPF/MCServerLauncher.WPF.csproj /m:1 /p:OutDir=..\artifacts\verify\wpf\` passed with 0 warnings and 0 errors.
- `dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj /m:1 --no-restore --filter "FullyQualifiedName~ActionRegistryCharacterizationTests.LegacyGetSystemInfoDispatch_EmptyParams_ReturnsOkEnvelopeWithTypedPayload|FullyQualifiedName~GeneratedRegistryParityTests.StopGate_Ping_And_GetSystemInfo_SuccessPathOutputs_MatchExactly" /p:OutDir=..\artifacts\verify\protocol\` passed. Existing warnings remain in source-generation and protocol-test files unrelated to this task.
- `git diff --check` passed for the main repository.
- `git -C MCServerLauncher.WPF/Translations diff --check` passed for the translation repository.

## Changelog

- Added edit handling that validates the replacement daemon connection before updating the saved host config or visible card.
- Kept delete behind countdown confirmation, closes the daemon connection, removes the saved host, and reports success/failure through notifications.
- Added CPU, memory, and drive usage display on daemon cards using the existing `GetSystemInfoAsync` contract.
- Added resource and delete-result translation keys across the WPF translation resources.
- Added per-page auto-refresh toggles and interval controls for daemon and instance manager pages.
- Extended daemon system information with OS/architecture text, daemon version, and multi-drive disk data while preserving the legacy single-drive field.
- Aggregated total disk usage across all ready non-optical drives and exposed per-drive usage in the daemon card tooltip.
- Kept the extended status records JSON-deserializable and adjusted record equality so array-backed drive data compares by contents in protocol tests.
- Aligned OS and daemon-version rows with the remote-address and connection-status rows, kept resource usage text visible, and shortened the resource progress bars.
- Removed `SYSLIB1225` source-generation warnings by keeping runtime `Encoding` properties out of JSON metadata and exposing stable web-name string proxy properties for `input_encoding` and `output_encoding`.
- Replaced auto-refresh interval number boxes with fixed 5/20/30/45/60 second choices, made switch text explicitly show on/off state, and formatted daemon resource percentages with two decimal places.
- Renamed the daemon card version label to "node version" in localized resources.
- Added CPU core/thread counts to daemon system status and daemon cards while keeping the legacy CPU `count` field as the logical processor count.
- Formatted daemon memory and disk used/total values with two decimal places.
