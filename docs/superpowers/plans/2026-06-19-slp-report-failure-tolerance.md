# SLP Report Failure Tolerance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent Minecraft SLP ping/player-list failures from breaking daemon instance report refresh.

**Architecture:** Treat Minecraft server-list ping as best-effort report enrichment. `SlpClient.GetStatusModern` should stop after an invalid status payload instead of reusing a failed connection for latency, and daemon Minecraft report generation should convert non-cancellation SLP failures into an empty player list.

**Tech Stack:** C# 14, .NET 10, System.Net.Sockets, xUnit protocol tests.

---

## File Structure

- Modify: `MCServerLauncher.Common/Network/SlpClient.cs`
  - Owns modern Minecraft SLP status and latency probing.
- Modify: `MCServerLauncher.Daemon/Management/Minecraft/MinecraftInstance.cs`
  - Owns Minecraft-specific report enrichment for online players.
- Create: `MCServerLauncher.ProtocolTests/SlpClientFailureToleranceTests.cs`
  - Verifies invalid SLP status payloads return `null` without a follow-up latency write.

## Task 1: Add SLP Failure Regression Test

- [x] **Step 1: Write the failing test**

Create `MCServerLauncher.ProtocolTests/SlpClientFailureToleranceTests.cs` with a loopback `TcpListener` that accepts the status request, sends an invalid/empty status response, then closes the connection. Assert `SlpClient.GetStatusModern` returns `null` instead of throwing.

- [x] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release --filter "FullyQualifiedName~SlpClientFailureToleranceTests"
```

Expected before implementation: FAIL due `IOException` when the client tries to write the latency packet after invalid status.

- [x] **Step 3: Implement minimal common-layer fix**

In `SlpClient.GetStatusModern`, return `null` immediately when `GetSlpAsync` returns `null`; only call `GetLatencyAsync` after a valid status payload.

- [x] **Step 4: Add daemon-side defense**

In `MinecraftInstance.GetServerPlayersAsync`, let caller-requested cancellation propagate and catch other SLP/report enrichment exceptions as an empty player list.

- [x] **Step 5: Verify focused test**

Run:

```powershell
dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release --filter "FullyQualifiedName~SlpClientFailureToleranceTests"
```

Expected after implementation: PASS.

- [x] **Step 6: Verify relevant project and hygiene**

Run:

```powershell
dotnet build MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj /m:1
dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release --no-build
git diff --check
```

Expected: commands exit 0.

## Changelog

- Added regression coverage for invalid Minecraft SLP status responses closing before latency ping.
- Updated modern SLP probing to stop after invalid status payloads.
- Hardened Minecraft instance report enrichment so player-list probe failures do not fail `GetAllReports`.
- Downgraded invalid SLP payload parse logs from error to debug because report-time Minecraft ping is best-effort.
