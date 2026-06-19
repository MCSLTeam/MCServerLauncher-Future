# Postman-only protocol docs implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove OpenAPI artifacts and keep only Postman-compatible daemon protocol documentation.

**Architecture:** The daemon serves `/postman_collection.json` and protocol markdown docs. WebSocket action schemas and examples live in the Postman collection instead of OpenAPI. Existing daemon action/event protocol remains unchanged and WebSocket-only.

**Tech Stack:** C# 14, .NET 10, TouchSocket HTTP, Postman Collection v2.1 JSON.

---

## Touched areas

- `backend`
- `protocol`
- `docs`
- `tests`

## Tasks

### Task 1: Tests

- [x] Require embedded Postman collection and protocol docs.
- [x] Forbid embedded OpenAPI resources, `/openapi.json` route entries, and generated `openapi.json` files.

### Task 2: Remove OpenAPI artifacts

- [x] Remove daemon OpenAPI embedded resource registration.
- [x] Remove `/openapi.json` from `EmbeddedDocumentation`.
- [x] Delete daemon and protocol `openapi.json` files.

### Task 3: Update Postman docs

- [x] Update `ws-api.md` to list `/postman_collection.json` as the only machine-readable protocol artifact.
- [x] Keep Postman collection valid and free from OpenAPI/Swagger references.

### Task 4: Verify

- [x] Run focused inbound documentation tests.
- [x] Build daemon.
- [x] Run full protocol tests.
- [x] Validate Postman JSON and diff whitespace.

## Changelog

- 2026-06-19: Planned removal of OpenAPI artifacts in favor of Postman-only protocol docs.
- 2026-06-19: Removed OpenAPI files/routes/resources, updated Postman-only docs, and verified daemon behavior.
