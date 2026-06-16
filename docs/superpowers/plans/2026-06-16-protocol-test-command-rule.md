# Protocol Test Command Rule Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Update commit-time protocol test guidance to use the Release `--no-build` command and verify it locally.

**Architecture:** Keep the project-rule change in `RULES.md`, where commit gates are defined. Verify with the exact command requested by the user, building Release artifacts first only when `--no-build` has no current output to run.

**Tech Stack:** Markdown, PowerShell, .NET 10 test runner.

---

## Touched Areas

- `workflow`: commit-time project rule.
- `tests`: protocol test verification command.
- `agent-docs`: implementation plan and changelog.

## Tasks

- [x] Update `RULES.md` so commit-time protocol verification uses `dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release --no-build`.
- [x] Build Release protocol test artifacts if the `--no-build` test command fails due stale or missing outputs.
- [x] Run `dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release --no-build`.
- [x] Run `git diff --check`.

## Changelog

- Updated `RULES.md` commit-time protocol test guidance to name the exact Release `--no-build` command.
- Built `MCServerLauncher.ProtocolTests` in Release before re-running the `--no-build` test command because the initial `--no-build` run used stale or missing outputs.
- Verified `dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release --no-build` with 317 passed, 0 failed, 0 skipped.
- Verified `git diff --check`; no whitespace errors were reported.
