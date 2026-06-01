# MCServerLauncher-Future Tasks

Task tracking for the MCServerLauncher-Future project. This document organizes pending work, technical debt, and improvement opportunities.

**Last Updated**: 2026-05-03

---

## 🔴 High Priority

_All high priority items completed!_

---

## 🟡 Medium Priority

### Performance Optimization

- [x] **Event batching for WebSocket** (464c703, current)
  - Implemented Channel-based event collection with 10ms batching window
  - Groups events by client to reduce redundant serialization
  - Supports up to 100 events per batch for high-frequency scenarios
  - Lazy initialization to avoid CPU contention when idle
  - Proper event handler cleanup in DisposeAsync to prevent leaks
  - Updated performance baseline to 4650 ns/op (reflects batching infrastructure overhead)
  - All 297 protocol tests passing consistently
  - Reduces WebSocket overhead for high-frequency events

- [x] **Merge event sends into single WebSocket frame** (4d0d167)
  - Added EventPacket[] to RpcEnvelopeContext for batch serialization
  - Combines multiple events into single frame in SendEventsToClientAsync
  - Client parses both single EventPacket and EventPacket[] arrays
  - Updated tests to reflect new protocol (arrays are now valid)
  - All 298 protocol tests passing
  - Further reduces WebSocket overhead by eliminating per-event frame overhead

### Feature Development

- [ ] **Library local cache for Forge installer**
  - Location: `MCServerLauncher.Daemon/Management/Installer/MinecraftForge/ForgeInstallerV1.cs`
  - Location: `MCServerLauncher.Daemon/Management/Installer/MinecraftForge/ForgeInstallerV2.cs`
  - Feature: Check local cache first, verify SHA1, download from mirror if needed
  - Benefit: Faster installation, reduced bandwidth

- [ ] **Clean up stale NeoForge BMCLAPI TODOs**
  - Location: `MCServerLauncher.Daemon/Management/Factory/MCForgeFactory.cs`
  - Current: NeoForge BMCLAPI mirror flow is already wired from WPF create-instance providers, but daemon still carries stale TODO comments
  - Target: Remove stale comments and document the current mirror path clearly

- [ ] **Relay packet support**
  - Location: `MCServerLauncher.DaemonClient/WebSocketPlugin/WsReceivedPlugin.cs`
  - Feature: Support for relay/proxy packets in WebSocket protocol
  - Status: Not yet implemented

- [ ] **Event-trigger notification push to WebSocket clients**
  - Location: `MCServerLauncher.Daemon/Remote/Event/EventTriggerService.cs`
  - Feature: Implement `SendNotificationAction` so event rules can push notifications to connected clients via WebSocket
  - Status: General event delivery exists; notification action path is still TODO

### Architecture Improvements

- [ ] **Convert remaining file operations to Result type**
  - Location: `MCServerLauncher.Daemon/Management/InstanceConfigExtensions.cs`
  - Current: Partially migrated; some factory/file helpers already use `Result<T>`
  - Target: Finish converting remaining raw file operations to `Result<T>` pattern
  - Benefit: Better error handling, functional approach

- [ ] **Add CancellationToken to remaining async manager methods**
  - Location: `MCServerLauncher.Daemon/Management/IInstanceManager.cs`
  - Current: Cancellation support is partial (`StopAllInstances` already supports it)
  - Target: Add `CancellationToken` parameter to remaining async manager methods
  - Benefit: Proper cancellation support, better resource cleanup

- [ ] **Complete UTF-8 span-based deserialization migration**
  - Location: `MCServerLauncher.Daemon/Remote/Action/IActionExecutor.cs`
  - Current: `ReadOnlySpan<byte>` / `ReadOnlyMemory<byte>` overloads exist, but the main interface still exposes string input
  - Target: Move the main action-executor boundary fully to UTF-8 span/memory input
  - Benefit: Zero-copy deserialization, reduced allocations

### Error Handling

- [x] **DaemonRequestException handling** (1d026bc)
  - Added proper exception handling in download operations
  - Added proper exception handling in upload operations (already existed)

- [x] **IOException handling for file operations** (1d026bc)
  - Added proper exception handling for file write operations in download

### Configuration

- [x] **Configurable file chunk upload timeout** (1d026bc)
  - Added `chunkTimeout` parameter to `UploadFileAsync`
  - Passed to `FileUploadRequestParameter.Timeout`

- [x] **Configurable file chunk download timeout** (1d026bc)
  - Added `chunkTimeout` parameter to `DownloadFileAsync`
  - Passed to `FileDownloadRequestParameter.Timeout`

### WPF Improvements

- [x] **Complete Java / Forge / Fabric / NeoForge create instance flows** (current)
  - Implemented FinishSetup in `CreateMinecraftJavaInstanceProvider`
  - Implemented FinishSetup in `CreateMinecraftForgeInstanceProvider`
  - Implemented FinishSetup in `CreateMinecraftFabricInstanceProvider`
  - Implemented FinishSetup in `CreateMinecraftNeoForgeInstanceProvider`
  - Loader-based providers now build the proper core / installer URL and call `daemon.AddInstanceAsync(setting)`
  - Daemon now routes Forge-family installers through a shared installer resolver
  - Remaining stubbed providers still include: Bedrock, Terraria, OtherExecutable, Quilt

- [ ] **Re-audit nullable reference hotspots in WPF project**
  - Location: `MCServerLauncher.WPF/` (multiple files)
  - Current: Project builds clean, but nullable suppressions / null-forgiving hotspots still exist in several WPF files
  - Target: Remove remaining unsafe null-forgiving usage, add proper null checks, and reduce warning-prone patterns
  - Files worth reviewing first:
    - `Modules/Download.cs`
    - `View/Components/Generic/DownloadProgressItem.xaml.cs`
    - `View/Components/CreateInstance/ForgeLoaderSet.xaml.cs`
    - `View/Components/CreateInstance/NeoForgeLoaderSet.xaml.cs`
    - `View/Components/CreateInstance/QuiltLoaderSet.xaml.cs`
    - `View/Components/ResDownloadItem/MCSLSyncResCoreVersionItem.xaml.cs`

- [ ] **Harden download component error handling**
  - Location: `MCServerLauncher.WPF/View/Components/Generic/DownloadProgressItem.xaml.cs`
  - Current: Basic failure notifications exist, but cancellation cleanup and null-safety are still fragile
  - Target: Improve cancellation/error cleanup and remove null-sensitive call paths in download UI
  - Benefit: More robust download UI, better user experience

- [ ] **Refactor loader set components**
  - Location: `MCServerLauncher.WPF/View/Components/CreateInstance/`
  - Current: ForgeLoaderSet, FabricLoaderSet, NeoForgeLoaderSet, QuiltLoaderSet still share a lot of duplicated step logic
  - Target: Extract common functionality, reduce code duplication
  - Benefit: Easier maintenance, consistent behavior across loaders

- [ ] **Expand input validation for user-facing create-instance forms**
  - Location: `MCServerLauncher.WPF/View/Components/CreateInstance/`
  - Current: Providers now guard against missing values, but validation is still mostly non-empty checks
  - Target: Validate paths, selected values, and instance naming rules before submission
  - Benefit: Better user experience, prevent invalid submissions

---

## 🟢 Low Priority / Future Enhancements

### Code Modernization

- [ ] **Consider async event handlers**
  - Location: `MCServerLauncher.DaemonClient/Daemon.cs`
  - Location: `MCServerLauncher.DaemonClient/WebSocketPlugin/WsReceivedPlugin.cs`
  - Current: Synchronous event handlers
  - Consider: Async event handlers or BeginInvoke pattern

### Source Generator Improvements

- [ ] **Enable analyzer release tracking**
  - Warnings: RS2008 for rules MCSLDAG001-005
  - Location: `MCServerLauncher.Daemon.Generators/DaemonActionRegistryGenerator.cs`
  - Add release tracking for analyzer diagnostics
  - Reference: <https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md>

---

## ✅ Recently Completed

### Configuration & Error Handling (1d026bc)

- ✅ Added configurable file chunk timeouts for upload and download operations
- ✅ Implemented proper DaemonRequestException handling in file transfer operations
- ✅ Implemented proper IOException handling in file write operations
- ✅ Improved i18n spacing in documentation comments

### Critical TODOs & Stability Improvements (7b435a5)

- ✅ Fixed potential deadlock in ConnectionsCommand expire_all operation
  - Added 5-second timeout to prevent indefinite blocking on WebSocket close operations
  - Improved user feedback with timeout notification
- ✅ Fixed WebSocket graceful shutdown edge case
  - Integrated GracefulShutdown CancellationToken into WsContext
  - Modified ActionExecutor to use linked CancellationToken combining task and shutdown tokens
  - Prevents SendAsync failures during daemon shutdown
- ✅ Improved i18n compliance in user-facing strings
  - Added proper spacing between English/Chinese characters and numbers/Chinese characters

### Code Quality & Warnings (ac7e173)

- ✅ Fixed all nullable reference type warnings (CS8618) across 12 files
  - Added `required` modifiers to Forge installer JSON models
  - Initialized fields with `= null!` for netstandard2.1 compatibility
  - Handled object pooling constraints in ActionExecutor
- ✅ Fixed switch expression exhaustiveness (CS8524) in InstanceConfigExtensions
- ✅ Fixed potential null reference warnings (CS8602) in MemoryInfoHelper
- ✅ Fixed unused field warning (CS0649) in Manifest.cs
- ✅ **Build Status**: 0 warnings, 0 errors

### Event Metadata Handling

- ✅ Unified explicit JSON null and missing meta semantics in GetEventMeta (ce50cc9)
- ✅ Reject explicit JSON null meta in GetEventMeta for meta-bearing events (ee5fe60)
- ✅ Address review round 2 test quality feedback (C9-C11) (1c5e353)

### Serialization & Performance

- ✅ Align UsesReflectionFallback with CreateStjResolver runtime guard (fad1f9c)
- ✅ Update performance baselines with CI-measured values (7d58066)
- ✅ Add [PERF] measurement logging and CI detailed output (8400f36)
- ✅ Complete TouchSocket low-level transport spike (281b3bd)
- ✅ Reduce event fan-out serialization duplication (52f6998)
- ✅ Reuse outbound serialization for replay (48fd896)
- ✅ Converge inbound envelope parsing (938e9cc)

### Code Quality

- ✅ Harden daemon trim boundaries for AOT (6bc4f04)
- ✅ Tighten RPC boundary source-gen ownership (361ad13)
- ✅ Extend common STJ contract ownership (d027219)

---

## 📊 Project Metrics

### Build Status

- **Solution**: MCServerLauncher.sln
- **Projects**: 7 (WPF, Daemon, DaemonClient, Common, Generators, ProtocolTests, Benchmarks)
- **Target Framework**: .NET 10.0 (C# 14)
- **Build Warnings**: 0 ✅ (fixed in ac7e173)

### Test Coverage

- **Test Project**: MCServerLauncher.ProtocolTests
- **Benchmark Project**: MCServerLauncher.Benchmarks
- **Performance Baselines**: Tracked in CI

### Technical Debt Score

- **High Priority Items**: 0
- **Medium Priority Items**: 11
- **Low Priority Items**: 2
- **Total TODO Comments**: 19

---

## 🎯 Recommended Focus Areas

### Q2 2026 Priorities

1. **Code Quality Sprint**
   - Re-audit WPF nullable hotspots and remove fragile suppressions
   - Harden download component error handling
   - Keep clean build status while reducing risky null-sensitive paths

2. **Runtime Protocol & Eventing**
   - Complete UTF-8 span/memory migration at the action-executor boundary
   - Implement relay packet support
   - Implement WebSocket push for `SendNotificationAction`

3. **Instance Creation & Installer UX**
   - Add Forge installer library local cache
   - Clean up stale NeoForge BMCLAPI TODOs / comments
   - Finish remaining stubbed create-instance providers (Quilt, Bedrock, Terraria, OtherExecutable)

4. **WPF Maintainability**
   - Refactor duplicated loader set components
   - Expand create-instance form validation
   - Consolidate repeated provider finish/setup logic where practical

---

## 📝 Notes

- **Performance Focus**: Recent work emphasizes transport layer optimization and serialization efficiency
- **Testing**: Performance baselines are tracked in CI with [PERF] logging
- **Build Configuration**: Daemon supports Native AOT with trimming enabled

## 🔗 Related Documentation

- [AGENTS.md](AGENTS.md) - Development guide for agents
- [MCServerLauncher.Daemon/README.md](MCServerLauncher.Daemon/README.md) - Common development issues
- [MCServerLauncher.Daemon/.Resources/Docs/](MCServerLauncher.Daemon/.Resources/Docs/) - API documentation
- [README.md](README.md) - Project overview and setup
