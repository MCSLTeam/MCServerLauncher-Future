# MCServerLauncher-Future Tasks

Task tracking for the MCServerLauncher-Future project. This document organizes pending work, technical debt, and improvement opportunities.

**Last Updated**: 2026-05-03

---

## 🔴 High Priority

### Code Quality & Warnings

- [ ] **Fix nullable reference type warnings (CS8618)**
  - `MCServerLauncher.Daemon/Management/Installer/MinecraftForge/V2Json/Mirror.cs:5` - Add `required` to `Url` property
  - `MCServerLauncher.Daemon/Management/Installer/MinecraftForge/V1Json/LibraryInfo.cs:8` - Add `required` to `Name` property
  - `MCServerLauncher.Daemon/Management/Installer/MinecraftForge/Json/Version.cs` - Multiple properties need `required` modifier
  - `MCServerLauncher.Daemon/Management/Installer/MinecraftForge/Json/Install.cs` - `Minecraft` and `Json` properties
  - `MCServerLauncher.Daemon/Management/Installer/MinecraftForge/Json/Artifact.cs` - `Domain`, `Name`, `Version` properties
  - `MCServerLauncher.Daemon/Remote/Action/ActionExecutor.cs:181-187` - Multiple fields need initialization

- [ ] **Fix switch expression exhaustiveness (CS8524)**
  - `MCServerLauncher.Daemon/Management/InstanceConfigExtensions.cs:147` - Add missing enum cases for `TargetType`

- [ ] **Fix potential null reference warnings (CS8602)**
  - `MCServerLauncher.Daemon/Utils/Status/MemoryInfoHelper.cs:26` - Add null check
  - `MCServerLauncher.Daemon/Utils/Status/MemoryInfoHelper.cs:76` - Add null check

- [ ] **Fix unused field warning (CS0649)**
  - `MCServerLauncher.Daemon/Management/Installer/MinecraftForge/Json/Manifest.cs:7` - Field `_versions` never assigned

### Critical TODOs

- [ ] **Potential deadlock issue in ConnectionsCommand**
  - Location: `MCServerLauncher.Daemon/Console/Commands/ConnectionsCommand.cs`
  - Issue: Potential blocking problem, needs timeout implementation
  - Priority: High (affects daemon stability)

- [ ] **WebSocket graceful shutdown edge case**
  - Location: `MCServerLauncher.Daemon/Remote/WsContext.cs`
  - Issue: `GetWebsocket().SendAsync` may fail during program shutdown
  - Solution: Use CancellationToken from GracefulShutdown

---

## 🟡 Medium Priority

### Performance Optimization

- [ ] **Event batching for WebSocket**
  - Location: `MCServerLauncher.Daemon/Remote/WsEventPlugin.cs`
  - Current: 1 event = 1 WebSocket send
  - Proposed: Batch multiple events (harvest mode)
  - Impact: Reduces WebSocket overhead for high-frequency events

- [ ] **Merge event sends**
  - Location: `MCServerLauncher.Daemon/Remote/WsEventPlugin.cs`
  - Related to event batching above
  - Combine multiple event payloads into single WebSocket frame

### Feature Development

- [ ] **Library local cache for Forge installer**
  - Location: `MCServerLauncher.Daemon/Management/Installer/MinecraftForge/ForgeInstallerV1.cs`
  - Location: `MCServerLauncher.Daemon/Management/Installer/MinecraftForge/ForgeInstallerV2.cs`
  - Feature: Check local cache first, verify SHA1, download from mirror if needed
  - Benefit: Faster installation, reduced bandwidth

- [ ] **BMCLAPI acceleration for NeoForge**
  - Location: `MCServerLauncher.Daemon/Management/Factory/MCForgeFactory.cs`
  - Feature: Add BMCLAPI mirror support for NeoForge downloads (1.20.2+)
  - Benefit: Faster downloads for Chinese users

- [ ] **Relay packet support**
  - Location: `MCServerLauncher.DaemonClient/WebSocketPlugin/WsReceivedPlugin.cs`
  - Feature: Support for relay/proxy packets in WebSocket protocol
  - Status: Not yet implemented

- [ ] **Event notification to WebSocket clients**
  - Location: `MCServerLauncher.Daemon/Remote/Event/EventTriggerService.cs`
  - Feature: Send notifications to connected clients via WebSocket
  - Status: Placeholder TODO comment

### Architecture Improvements

- [ ] **Convert file operations to Result type**
  - Location: `MCServerLauncher.Daemon/Management/InstanceConfigExtensions.cs`
  - Refactor: Use `Result<T>` pattern for all file operations
  - Benefit: Better error handling, functional approach

- [ ] **Add CancellationToken to async methods**
  - Location: `MCServerLauncher.Daemon/Management/IInstanceManager.cs`
  - Refactor: Add `CancellationToken` parameter to all async methods
  - Benefit: Proper cancellation support, better resource cleanup

- [ ] **Switch from Newtonsoft.Json to System.Text.Json**
  - Location: `MCServerLauncher.Daemon/Remote/Action/ActionHandler.cs`
  - Current: Uses Newtonsoft.Json `JObject`
  - Target: Use System.Text.Json `JsonElement`
  - Benefit: AOT compatibility, better performance

- [ ] **Change input to Span<byte> for deserialization**
  - Location: `MCServerLauncher.Daemon/Remote/Action/IActionExecutor.cs`
  - Current: String-based deserialization
  - Target: Span<byte> with System.Text.Json
  - Benefit: Zero-copy deserialization, reduced allocations

### Error Handling

- [ ] **DaemonRequestException handling**
  - Location: `MCServerLauncher.DaemonClient/DaemonExtensions.cs`
  - Add proper exception handling for daemon request failures

- [ ] **IOException handling for file operations**
  - Location: `MCServerLauncher.DaemonClient/DaemonExtensions.cs`
  - Add proper exception handling for file I/O operations

### Configuration

- [ ] **Configurable file chunk upload timeout**
  - Location: `MCServerLauncher.DaemonClient/DaemonExtensions.cs`
  - Current: `Timeout = null`
  - Add configurable timeout for file chunk uploads

- [ ] **Configurable file chunk download timeout**
  - Location: `MCServerLauncher.DaemonClient/DaemonExtensions.cs`
  - Current: `Timeout = null`
  - Add configurable timeout for file chunk downloads

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
- **Build Warnings**: ~25 (mostly nullable reference types)

### Test Coverage

- **Test Project**: MCServerLauncher.ProtocolTests
- **Benchmark Project**: MCServerLauncher.Benchmarks
- **Performance Baselines**: Tracked in CI

### Technical Debt Score

- **High Priority Items**: 4
- **Medium Priority Items**: 13
- **Low Priority Items**: 2
- **Total TODO Comments**: 20+

---

## 🎯 Recommended Focus Areas

### Q2 2026 Priorities

1. **Code Quality Sprint**
   - Fix all nullable reference type warnings
   - Address critical TODOs (deadlock, graceful shutdown)
   - Target: Zero build warnings

2. **Performance Optimization**
   - Implement event batching for WebSocket
   - Complete Span<byte> migration for deserialization
   - Measure impact with benchmarks

3. **Feature Completion**
   - Library local cache for Forge installer
   - BMCLAPI acceleration for NeoForge
   - Relay packet support

4. **Architecture Modernization**
   - Migrate to System.Text.Json throughout
   - Add CancellationToken support
   - Convert file operations to Result pattern

---

## 📝 Notes

- **Serialization Strategy**: Migrating from Newtonsoft.Json to System.Text.Json for AOT compatibility
- **Performance Focus**: Recent work emphasizes transport layer optimization and serialization efficiency
- **Testing**: Performance baselines are tracked in CI with [PERF] logging
- **Build Configuration**: Daemon supports Native AOT with trimming enabled

## 🔗 Related Documentation

- [AGENTS.md](AGENTS.md) - Development guide for agents
- [MCServerLauncher.Daemon/README.md](MCServerLauncher.Daemon/README.md) - Common development issues
- [MCServerLauncher.Daemon/.Resources/Docs/](MCServerLauncher.Daemon/.Resources/Docs/) - API documentation
- [README.md](README.md) - Project overview and setup
