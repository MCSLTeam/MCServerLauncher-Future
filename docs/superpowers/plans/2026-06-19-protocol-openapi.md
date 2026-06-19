# Protocol OpenAPI Artifact Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an OpenAPI artifact to `mcsl-future-protocol` that documents the current daemon HTTP endpoints and describes the WebSocket action/event protocol through vendor extensions.

**Architecture:** Standard OpenAPI `paths` cover only implemented HTTP endpoints: `GET /`, `GET /info`, and `POST /subtoken`. WebSocket behavior is documented under `x-mcsl-websocket` so the artifact does not pretend action frames are HTTP routes.

**Tech Stack:** OpenAPI 3.1 JSON, Writerside Markdown, current MCServerLauncher Future JSON-over-WebSocket protocol.

---

### Task 1: Create OpenAPI artifact

**Files:**
- Create: `../mcsl-future-protocol/openapi.json`

- [x] **Step 1: Model HTTP endpoints**

Document `GET /`, `GET /info`, and `POST /subtoken` with schemas matching `MCServerLauncher.Daemon/Remote/HttpPlugin.cs`.

- [x] **Step 2: Model WebSocket extension**

Add `x-mcsl-websocket` with endpoint, action envelope, response envelope, event envelope, action names, event names, and retcodes.

### Task 2: Link docs

**Files:**
- Modify: `../mcsl-future-protocol/topics/connection.md`

- [x] **Step 1: Add OpenAPI reference**

Mention `openapi.json` on the connection page and state that WebSocket action/event details live in the vendor extension.

### Task 3: Verify

**Files:**
- Inspect: `../mcsl-future-protocol/openapi.json`
- Inspect: `../mcsl-future-protocol/topics/connection.md`

- [x] **Step 1: Validate JSON**

Parse `openapi.json` with `python3 -m json.tool`.

- [x] **Step 2: Check diffs**

Run `git diff --check` in both repositories.

## Changelog

- 2026-06-19: Created plan for adding protocol OpenAPI artifact.
- 2026-06-19: Generated `../mcsl-future-protocol/openapi.json` and linked it from the connection documentation.
- 2026-06-19: Expanded `openapi.json` with all WebSocket action parameter/result schemas and synced the daemon embedded copy.
- 2026-06-19: Added Swagger-visible documentation paths for every WebSocket action so the embedded UI lists the action API.
