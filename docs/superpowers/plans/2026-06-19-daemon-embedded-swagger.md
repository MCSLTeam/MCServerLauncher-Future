# Daemon Embedded Swagger Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Embed the current protocol OpenAPI and related protocol docs into the daemon and expose an offline Swagger UI over the existing TouchSocket HTTP plugin.

**Architecture:** The daemon keeps using TouchSocket `HttpPlugin`; no ASP.NET Core or Swashbuckle pipeline is introduced. Protocol docs and Swagger UI files are packaged as `EmbeddedResource` items and served through explicit `GET` routes.

**Tech Stack:** C# 14, .NET 10, TouchSocket HTTP, embedded resources, Swagger UI static assets.

---

## Touched Areas

- `docs`
- `agent-docs`
- `protocol`
- `backend`
- `tests`

## Tasks

### Task 1: Lock Embedded Documentation Expectations

**Files:**
- Modify: `MCServerLauncher.ProtocolTests/DaemonInboundTransportPipelineTests.cs`

- [x] Add source/resource tests that require:
  - `openapi.json` to be embedded in `MCServerLauncher.Daemon`.
  - Swagger UI static files to be embedded.
  - `HttpPlugin` to expose `/openapi.json`, `/swagger`, `/swagger/index.html`, and `/docs/protocol/...`.

### Task 2: Add Embedded Protocol And Swagger Assets

**Files:**
- Create: `MCServerLauncher.Daemon/.Resources/Docs/openapi.json`
- Create: `MCServerLauncher.Daemon/.Resources/Docs/protocol/topics/*.md`
- Create: `MCServerLauncher.Daemon/.Resources/Docs/swagger/index.html`
- Create: `MCServerLauncher.Daemon/.Resources/Docs/swagger/swagger-ui.css`
- Create: `MCServerLauncher.Daemon/.Resources/Docs/swagger/swagger-ui-bundle.js`
- Create: `MCServerLauncher.Daemon/.Resources/Docs/swagger/swagger-ui-standalone-preset.js`
- Create: `MCServerLauncher.Daemon/.Resources/Docs/swagger/LICENSE`
- Modify: `MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj`

- [x] Copy the current `mcsl-future-protocol` OpenAPI and topic docs into daemon resources.
- [x] Add local Swagger UI assets so `/swagger` works without internet access.
- [x] Embed docs with stable logical names under `MCServerLauncher.Daemon.Resources.Docs.*`.

### Task 3: Serve Embedded Documentation

**Files:**
- Create: `MCServerLauncher.Daemon/Remote/EmbeddedDocumentation.cs`
- Modify: `MCServerLauncher.Daemon/Remote/HttpPlugin.cs`

- [x] Add a small helper that maps request paths to embedded documentation resources and content types.
- [x] Serve `/openapi.json`.
- [x] Serve `/swagger` and `/swagger/index.html`.
- [x] Serve `/swagger/*` static assets.
- [x] Serve `/docs/protocol/*` Markdown resources.
- [x] Keep existing `/`, `/info`, and `/subtoken` behavior unchanged.

### Task 4: Verify

**Commands:**
- [x] `dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj --filter FullyQualifiedName~DaemonInboundTransportPipelineTests /m:1`
- [x] `dotnet build MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj /m:1`
- [x] `python3 -m json.tool MCServerLauncher.Daemon/.Resources/Docs/openapi.json >/tmp/mcsl-daemon-openapi.json`
- [x] `git diff --check`

## Changelog

- Added plan for embedding protocol docs and offline Swagger UI in the daemon.
- Embedded `openapi.json`, protocol topic Markdown, and offline Swagger UI assets in the daemon.
- Added TouchSocket HTTP routes for `/openapi.json`, `/swagger`, `/swagger/index.html`, `/swagger/*`, and `/docs/protocol/*`.
- Added protocol tests that lock resource embedding and route registration expectations.
