# Daemon Status Failure Tolerance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent daemon shutdown or background report refresh from crashing when macOS CPU usage command output is empty or malformed.

**Architecture:** Keep the existing system-info helpers. Add a small parser seam for macOS CPU usage and make the daemon-report timer callback contain system-info refresh failures.

**Tech Stack:** C# 14, .NET 10, xUnit protocol tests.

---

## Touched Areas

- `backend`
- `tests`
- `docs`

## Tasks

### Task 1: Reproduce CPU Usage Parse Failure

**Files:**
- Create: `MCServerLauncher.ProtocolTests/SystemInfoFailureToleranceTests.cs`

- [x] Add a failing test for empty macOS CPU usage output.
- [x] Add a passing baseline test for normal percentage output.

### Task 2: Make Status Refresh Tolerant

**Files:**
- Modify: `MCServerLauncher.Daemon/Utils/Status/CpuInfoHelper.cs`
- Modify: `MCServerLauncher.Daemon/Bootstrap/DaemonServiceComposition.cs`

- [x] Parse macOS CPU usage with `double.TryParse` and return `0` when output is empty or invalid.
- [x] Catch daemon-report timer refresh failures so background status refresh does not become an unhandled exception during shutdown.

### Task 3: Verify

**Commands:**
- [x] `dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj --filter FullyQualifiedName~SystemInfoFailureToleranceTests /m:1`
- [x] `dotnet build MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj /m:1`
- [x] `dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj /m:1`
- [x] `git diff --check`

## Changelog

- 2026-06-19: Added macOS CPU usage parse fallback and contained daemon-report timer refresh failures.
