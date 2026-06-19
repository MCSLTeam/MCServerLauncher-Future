# Swagger Action HTTP Bridge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Swagger UI able to execute daemon action APIs by adding a real HTTP bridge for WebSocket action envelopes.

**Architecture:** Keep WebSocket as the canonical daemon protocol. Add `POST /api/v1/actions/{action}` as a debugging and Swagger bridge that validates the same token, builds a transient `WsContext`, and invokes the same action handlers.

**Tech Stack:** C# 14, .NET 10, TouchSocket HTTP, System.Text.Json, OpenAPI 3.1.

---

## Touched Areas

- `backend`
- `protocol`
- `docs`
- `tests`

## Tasks

### Task 1: Lock Swagger Bridge Expectations

**Files:**
- Modify: `MCServerLauncher.ProtocolTests/DaemonInboundTransportPipelineTests.cs`

- [x] Add a test requiring `HttpPlugin` to route `POST /api/v1/actions/*` to an action bridge.
- [x] Add a test requiring OpenAPI action paths to describe a real HTTP bridge instead of documentation-only paths.

### Task 2: Implement HTTP Action Bridge

**Files:**
- Create: `MCServerLauncher.Daemon/Remote/HttpActionBridge.cs`
- Modify: `MCServerLauncher.Daemon/Remote/HttpPlugin.cs`

- [x] Validate token from `Authorization: Bearer <token>` or `?token=<token>`.
- [x] Read the JSON action envelope from the request body.
- [x] Require the URL action name to match the envelope action.
- [x] Invoke sync and async handlers directly through `IActionExecutor`.
- [x] Return `ActionResponse` JSON.

### Task 3: Update OpenAPI And Docs

**Files:**
- Modify: `../mcsl-future-protocol/openapi.json`
- Modify: `MCServerLauncher.Daemon/.Resources/Docs/openapi.json`
- Modify: `MCServerLauncher.Daemon/.Resources/Docs/docs/ws-api.md`

- [x] Mark `/api/v1/actions/*` as a real HTTP bridge for Swagger/debugging.
- [x] Document token usage and the canonical WebSocket endpoint.

### Task 4: Verify

**Commands:**
- [x] `dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj --filter FullyQualifiedName~DaemonInboundTransportPipelineTests /m:1`
- [x] `dotnet build MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj /m:1`
- [x] `dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj /m:1`
- [x] `git diff --check`

## Changelog

- 2026-06-19: Planned a real HTTP bridge so Swagger UI can execute action envelopes.
- 2026-06-19: Added the HTTP action bridge implementation, OpenAPI bridge metadata, and usage documentation.
- 2026-06-19: Verified daemon build, focused inbound tests, full protocol tests, OpenAPI JSON formatting, and diff whitespace checks.
