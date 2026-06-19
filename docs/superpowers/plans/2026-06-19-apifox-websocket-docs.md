# Apifox WebSocket docs implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop representing WebSocket actions as HTTP GET requests and add an Apifox-native WebSocket export.

**Architecture:** Keep Postman collection for HTTP helpers and human-readable WebSocket examples, but do not encode WebSocket actions as HTTP request items. Add `apifox.json` with HTTP helpers in `apiCollection` and all WebSocket actions in `webSocketCollection`.

**Tech Stack:** C# 14, .NET 10, TouchSocket HTTP, Postman Collection v2.1 JSON, Apifox project JSON.

---

## Touched areas

- `backend`
- `docs`
- `tests`

## Tasks

### Task 1: Tests

- [x] Require embedded `/apifox.json` resource.
- [x] Require Apifox WebSocket collection to contain all `ActionType` actions.
- [x] Forbid HTTP `GET` methods under the Postman WebSocket action section.

### Task 2: Artifacts

- [x] Generate `apifox.json` with WebSocket actions in `webSocketCollection`.
- [x] Convert Postman WebSocket action section to documentation examples, not HTTP request items.
- [x] Serve `/apifox.json` from embedded docs.

### Task 3: Docs and verify

- [x] Update `ws-api.md` with Apifox import guidance.
- [x] Run focused tests, daemon build, full protocol tests, JSON checks, and diff whitespace checks.

## Changelog

- 2026-06-19: Planned Apifox-native WebSocket docs so action examples no longer appear as HTTP GET requests.
- 2026-06-19: Added Apifox-native WebSocket export and converted Postman WebSocket actions into non-HTTP examples.
