# Daemon Binding And Sln Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove WPF certificate import, make daemon HTTP/WebSocket startup logs reflect real bind/connect addresses, and reorganize the repository into real source/test/benchmark folders.

**Architecture:** WPF startup keeps data, settings, daemon client creation, and font installation, but no longer embeds or imports the MCSLTeam root certificate. The daemon continues to bind through TouchSocket using the configured port, while startup logs distinguish wildcard bind endpoints from client-connectable loopback/LAN hints. Repository structure uses real top-level folders: `src/` for app/library projects, `generators/` for Roslyn generators, `tests/` for protocol tests, and `benchmarks/` for BenchmarkDotNet projects.

**Tech Stack:** C# 14, .NET 10, WPF, TouchSocket, System.Text.Json, xUnit protocol tests.

---

### Task 1: Remove WPF certificate import

**Files:**
- Modify: `src/MCServerLauncher.WPF/Modules/Initializer.cs`
- Modify: `src/MCServerLauncher.WPF/Modules/Settings.cs`
- Modify: `src/MCServerLauncher.WPF/ViewModels/SettingsViewModel.cs`
- Modify: `src/MCServerLauncher.WPF/MCServerLauncher.WPF.csproj`
- Delete: `src/MCServerLauncher.WPF/Resources/MCSLTeam.cer`

- [x] Remove the `InitCert` method and its cryptography/X509 imports.
- [x] Remove startup checks for `App.IsCertImported`.
- [x] Remove `IsCertImported` from default app settings and the app settings model.
- [x] Remove `Resources\MCSLTeam.cer` from embedded resources and delete the certificate file.

### Task 2: Fix daemon bind/log address semantics

**Files:**
- Modify: `src/MCServerLauncher.Daemon/Application.cs`
- Modify: `src/MCServerLauncher.Daemon/Bootstrap/DaemonTouchSocketTransportProfile.cs`
- Modify: `tests/MCServerLauncher.ProtocolTests/TouchSocketHostingCompositionTests.cs`

- [x] Keep TouchSocket binding through `SetListenIPHosts(AppConfig.Get().Port)`, which binds IPv4 Any for the configured port.
- [x] Replace single `0.0.0.0` URL logs with bind endpoint logs plus connectable loopback/LAN hint URLs.
- [x] Add focused protocol tests that lock the distinction between wildcard bind addresses and connectable URLs.

### Task 3: Real repository layout

**Files:**
- Modify: `MCServerLauncher.sln`
- Modify: `.gitmodules`
- Move: `MCServerLauncher.WPF/` to `src/MCServerLauncher.WPF/`
- Move: `MCServerLauncher.Daemon/` to `src/MCServerLauncher.Daemon/`
- Move: `MCServerLauncher.DaemonClient/` to `src/MCServerLauncher.DaemonClient/`
- Move: `MCServerLauncher.Common/` to `src/MCServerLauncher.Common/`
- Move: `MCServerLauncher.Daemon.Generators/` to `generators/MCServerLauncher.Daemon.Generators/`
- Move: `MCServerLauncher.ProtocolTests/` to `tests/MCServerLauncher.ProtocolTests/`
- Move: `MCServerLauncher.Benchmarks/` to `benchmarks/MCServerLauncher.Benchmarks/`
- Modify: project references, Dockerfile, CI workflows, repository docs, and static path-based tests.

- [x] Add solution folders for apps, libraries, tests, benchmarks, generators, docs, and skills.
- [x] Move project directories into real top-level folders.
- [x] Update `.gitmodules` so the WPF translations submodule lives under `src/MCServerLauncher.WPF/Translations`.
- [x] Update solution paths and project references for the new layout.
- [x] Update Dockerfile, scripts, CI workflows, and path-sensitive tests.
- [x] Update active contributor docs and commands for the new layout.
- [x] Preserve pre-existing `Sign/` deletions as user-owned work.

### Task 4: Verification

**Files:**
- Build/test touched projects and inspect final diff.

- [x] Run `dotnet build src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj /m:1`.
- [x] Run `dotnet build src/MCServerLauncher.WPF/MCServerLauncher.WPF.csproj /m:1`.
- [x] Run `dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj /m:1`.
- [x] Run `dotnet build MCServerLauncher.sln /m:1`.
- [x] Run `git diff --check`.

## Changelog

- Planned removal of WPF root certificate import and embedded certificate.
- Planned daemon startup log correction for wildcard bind endpoints.
- Planned Visual Studio solution grouping and generated-file cleanup without moving project directories.
- Removed WPF certificate import startup flow, `IsCertImported` settings, and the embedded `MCSLTeam.cer` resource.
- Updated daemon startup logs to report bind endpoints separately from connectable WS/HTTP/Apifox URLs.
- Added protocol tests for the new daemon log semantics and updated existing Apifox log assertions.
- Added Visual Studio solution folders for apps, libraries, generators, tests, benchmarks, docs, and skills.
- Verified daemon build, WPF build, protocol tests, and `git diff --check`.
- Expanded cleanup scope from Visual Studio solution folders to real repository folders after user clarification.
- Moved app/library projects into `src/`, the Roslyn generator into `generators/`, protocol tests into `tests/`, and benchmarks into `benchmarks/`.
- Updated `.gitmodules`, solution project paths, project references, CI workflows, scripts, benchmark fixture paths, and active contributor/agent docs for the real folder layout.
- Fixed path-sensitive protocol tests that still searched for old root-level project/resource folders.
- Re-ran protocol tests after the real layout migration: 344 passed, 0 failed.
- Re-ran full solution build after the real layout migration: 0 warnings, 0 errors.
- Re-ran `git diff --check` after the real layout migration with no whitespace errors.
