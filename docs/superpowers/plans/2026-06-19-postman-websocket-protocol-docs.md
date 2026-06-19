# Postman protocol docs without HTTP bridge implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace embedded Swagger UI and HTTP action bridge with Postman-compatible protocol artifacts while keeping daemon actions WebSocket-only.

**Architecture:** Keep `/openapi.json` as schema documentation and expose `/postman_collection.json` for Postman import. Remove `/swagger*` routes/resources and remove `POST /api/v1/actions/*` from daemon code and OpenAPI paths. Postman users call helper HTTP endpoints over HTTP and action/event protocol over WebSocket.

**Tech Stack:** C# 14, .NET 10, TouchSocket HTTP, System.Text.Json, Postman Collection v2.1 JSON, OpenAPI 3.1.

---

## Touched areas

- `backend`
- `protocol`
- `docs`
- `tests`

## Tasks

### Task 1: Tests

- [x] Update documentation tests to require embedded OpenAPI, Postman collection, and protocol docs.
- [x] Update documentation tests to forbid Swagger resources, `/swagger` routes, `HttpActionBridge`, and `/api/v1/actions/*` OpenAPI paths.

### Task 2: Daemon resources

- [x] Remove `HttpActionBridge.cs` and `HttpPlugin` routing for `/api/v1/actions/*`.
- [x] Remove Swagger resource embedding and route entries.
- [x] Add `postman_collection.json` as an embedded resource and serve it at `/postman_collection.json`.

### Task 3: Protocol artifacts and docs

- [x] Update daemon and protocol `openapi.json` to remove action HTTP paths and bridge metadata.
- [x] Add Postman collection with HTTP helper requests and WebSocket action examples.
- [x] Update `ws-api.md` to describe Postman import and WebSocket usage.

### Task 4: Verify

- [x] Run focused inbound documentation tests.
- [x] Build daemon.
- [x] Run full protocol tests.
- [x] Validate JSON files and diff whitespace.

## Changelog

- 2026-06-19: Planned Postman-first protocol docs with WebSocket-only actions and no Swagger or HTTP action bridge.
- 2026-06-19: Removed Swagger UI and the HTTP action bridge, added an embedded Postman collection, and verified WebSocket-only action docs.
