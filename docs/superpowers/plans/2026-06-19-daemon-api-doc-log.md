# Daemon API docs startup log implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Print the Postman collection URL when the daemon starts.

**Architecture:** Keep the existing WebSocket and HTTP startup logs. Add one adjacent log line that points to `/postman_collection.json`, the only machine-readable API documentation artifact.

**Tech Stack:** C# 14, .NET 10, Serilog, protocol tests.

---

## Touched areas

- `backend`
- `docs`
- `tests`

## Tasks

### Task 1: Lock log expectation

- [ ] Add a source-inspection protocol test requiring the startup log to mention `/postman_collection.json`.

### Task 2: Add log line

- [ ] Add `Log.Information` after the HTTP server startup log.

### Task 3: Verify

- [ ] Run focused inbound tests.
- [ ] Build daemon.
- [ ] Run `git diff --check`.

## Changelog

- 2026-06-19: Planned a daemon startup log line for the Postman API documentation URL.
