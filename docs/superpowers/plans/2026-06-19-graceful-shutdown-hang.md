# Graceful Shutdown Hang Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent daemon shutdown from hanging when shutdown callbacks wait on work that is also waiting for the shutdown signal.

**Architecture:** `GracefulShutdown.Shutdown()` should be idempotent and publish the shutdown signal before awaiting registered cleanup callbacks. `WaitForShutdownAsync()` should observe already-completed shutdowns and should not rely on a single semaphore release that can be lost or delayed by callbacks.

**Tech Stack:** C# 14, .NET 10, xUnit protocol tests.

---

### Task 1: Fix GracefulShutdown Signal Ordering

**Files:**
- Create: `MCServerLauncher.ProtocolTests/GracefulShutdownTests.cs`
- Modify: `MCServerLauncher.Daemon/GracefulShutdown.cs`

- [x] **Step 1: Write the failing tests**

Add tests proving:

- `WaitForShutdownAsync()` completes even when an `OnShutdown` callback is still blocked.
- Calling `Shutdown()` more than once is harmless and does not throw.

- [x] **Step 2: Run focused tests and verify red**

Run: `dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj --filter GracefulShutdownTests /m:1`

Expected before implementation: callback-order test fails because `WaitForShutdownAsync()` waits behind `OnShutdown`.

- [x] **Step 3: Implement minimal fix**

Use a `TaskCompletionSource` as the shutdown signal, set it before invoking callbacks, and make repeat `Shutdown()` calls return without throwing.

- [x] **Step 4: Verify**

Run:

```bash
dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj --filter GracefulShutdownTests /m:1
dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj /m:1
git diff --check
```

Result: focused `GracefulShutdownTests` passed, full protocol tests passed with 343/343 tests, and whitespace check passed.

## Changelog

- 2026-06-19: Planned shutdown signal ordering fix for daemon `GracefulShutdown`.
- 2026-06-19: Fixed shutdown wait ordering by publishing the shutdown signal before awaiting callbacks and making repeated shutdown requests harmless.
