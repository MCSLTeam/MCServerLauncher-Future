# Protocol Documentation Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Update the sibling `mcsl-future-protocol` Writerside documentation so it describes the current MCServerLauncher Future C# implementation instead of the older MessagePack draft.

**Architecture:** Treat the C# code in `MCServerLauncher.Common`, `MCServerLauncher.Daemon`, and `MCServerLauncher.DaemonClient` as the source of truth. Rewrite the protocol docs around JSON-over-WebSocket action/event packets, current HTTP endpoints, current action/error/model names, and current file transfer behavior.

**Tech Stack:** Writerside Markdown, System.Text.Json protocol contracts, TouchSocket WebSocket/HTTP daemon transport.

---

### Task 1: Rewrite protocol concepts

**Files:**
- Modify: `../mcsl-future-protocol/topics/introduction.md`
- Modify: `../mcsl-future-protocol/topics/connection.md`
- Modify: `../mcsl-future-protocol/topics/action.md`
- Modify: `../mcsl-future-protocol/topics/event.md`

- [x] **Step 1: Replace old transport wording**

Replace MessagePack wording with System.Text.Json over WebSocket, and document `/api/v1?token=<token>` as the WebSocket endpoint.

- [x] **Step 2: Document current packet envelopes**

Action requests use `action`, `params`, and `id`. Action responses use `status`, `retcode`, `data`, `message`, and `id`. Event packets use `event`, `meta`, `data`, and `time`.

- [x] **Step 3: Document current HTTP endpoints**

Document `GET /`, `GET /info`, and `POST /subtoken` as implemented in `MCServerLauncher.Daemon/Remote/HttpPlugin.cs`.

### Task 2: Rewrite action and event catalog

**Files:**
- Modify: `../mcsl-future-protocol/topics/actions.md`
- Modify: `../mcsl-future-protocol/topics/events.md`
- Modify: `../mcsl-future-protocol/topics/file-transfer.md`

- [x] **Step 1: List current actions**

Use `MCServerLauncher.Common/ProtoType/Action/ActionType.cs` and handler attributes under `MCServerLauncher.Daemon/Remote/Action/Handlers` as the inventory.

- [x] **Step 2: List current events**

Use `MCServerLauncher.Common/ProtoType/Event/EventType.cs` and `IEventServiceExtensions.cs` as the event inventory.

- [x] **Step 3: Replace HTTP upload/download draft**

Document current JSON chunk actions and WebSocket binary upload frame behavior. Do not describe `/upload/<id>` or `/download/<id>` as current behavior.

### Task 3: Rewrite models and error codes

**Files:**
- Modify: `../mcsl-future-protocol/topics/models.md`
- Modify: `../mcsl-future-protocol/topics/action-errcode.md`

- [x] **Step 1: Align error code table**

Use `ActionRetcode.cs` values, including current `31001`, `31002`, and `31003` meanings.

- [x] **Step 2: Align protocol models**

Use current records and enums in `MCServerLauncher.Common/ProtoType`, keeping the documentation concise and focused on wire-facing fields.

### Task 4: Verify docs

**Files:**
- Inspect: `../mcsl-future-protocol/topics/*.md`
- Inspect: `../mcsl-future-protocol/mcsl-future-protocol.tree`
- Inspect: `../mcsl-future-protocol/labels.list`

- [x] **Step 1: Search stale terms**

Run searches for stale MessagePack, `subscriber`, `/upload/`, `/download/`, `result`, and old subtoken path wording.

- [x] **Step 2: Inspect git diffs**

Review diffs in both repositories and report any sandbox or verification limits.

## Changelog

- 2026-06-19: Created plan for syncing protocol documentation to current C# implementation.
- 2026-06-19: Rewrote protocol topics to match JSON-over-WebSocket action/event implementation, current HTTP endpoints, file transfer behavior, models, permissions, and retcodes.
