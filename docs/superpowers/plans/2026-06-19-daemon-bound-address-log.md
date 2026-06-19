# Daemon Bound Address Log Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Log daemon WebSocket, HTTP, and Apifox documentation URLs with the address actually bound by TouchSocket.

**Architecture:** `Application.ServeAsync` should derive one display address after `HttpService.StartAsync()` from `HttpService.Monitors` and reuse it for all startup URLs. If no monitor endpoint can be read, keep the existing fallback shape based on `AppConfig.Get().Port`.

**Tech Stack:** C# 14, .NET 10, TouchSocket, xUnit protocol tests.

---

### Task 1: Lock Startup Log Address Source

**Files:**
- Modify: `MCServerLauncher.ProtocolTests/DaemonInboundTransportPipelineTests.cs`
- Modify: `MCServerLauncher.Daemon/Application.cs`

- [x] **Step 1: Write the failing test**

Update `ApplicationStartupLog_PointsToApifoxDocsOnly` so it requires startup logs to call `GetBoundRemoteAddress()` and forbids the old hard-coded `0.0.0.0:{0}` Apifox URL.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj --filter ApplicationStartupLog_PointsToApifoxDocsOnly /m:1`

Expected: FAIL because `Application.cs` still logs `http://0.0.0.0:{0}/apifox.json`.

- [x] **Step 3: Write minimal implementation**

Add a private helper in `Application.cs` that reads the first `IPEndPoint` from `HttpService.Monitors` and formats it as `host:port`, bracketing IPv6 hosts. Use the helper for the three startup log lines.

- [x] **Step 4: Run focused tests**

Run: `dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj --filter ApplicationStartupLog_PointsToApifoxDocsOnly /m:1`

Expected: PASS.

- [x] **Step 5: Final verification**

Run:

```bash
dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj /m:1
git diff --check
git status --short --branch
```

Result: `git diff --check` passed. The focused startup-log test passed. After the Apifox documentation resources were fixed, the full protocol test suite passed.

## Changelog

- Added a focused plan for daemon startup log bound-address reporting.
- Changed daemon startup URL logs to use the actual TouchSocket bound endpoint when available.
