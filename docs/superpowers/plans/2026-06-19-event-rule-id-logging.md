# Event Rule ID Logging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Log event rule UUIDs instead of mutable rule display names in daemon event trigger execution logs.

**Architecture:** Keep event trigger behavior unchanged and only adjust structured logging arguments in `EventTriggerService`. Use `EventRule.Id` for rule identity while retaining instance UUID and execution mode.

**Tech Stack:** C# 14, .NET 10, Microsoft.Extensions.Logging.

---

## File Structure

- Modify: `MCServerLauncher.Daemon/Remote/Event/EventTriggerService.cs`
  - Replace rule name placeholders with rule UUID placeholders for daemon event trigger logs.

## Task 1: Use Rule IDs In Event Trigger Logs

- [x] **Step 1: Locate affected logs**

Search `EventTriggerService` for `RuleName` and identify logs that describe rule execution/evaluation/action failures.

- [x] **Step 2: Replace mutable name identity**

Change structured log templates from `{RuleName}` to `{RuleId}` and pass `rule.Id` instead of `rule.Name`.

- [x] **Step 3: Verify**

Run:

```powershell
dotnet build MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj /m:1 /p:OutDir=e:\MCSLCode\MCServerLauncher-Future\.artifacts\daemon-build-check\
dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release --no-build
git diff --check
```

Expected: commands exit 0.

## Changelog

- Updated daemon event trigger logs to identify rules by immutable UUID instead of display name.
