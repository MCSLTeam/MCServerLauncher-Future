# MCServerLauncher-Future Agent Harness Guide

Runtime workflow requirements for **any** AI coding agent (Claude Code, Codex,
Cursor, etc.) working in this repository.

This file is **agent-agnostic** and complements the project guide: `AGENTS.md`
describes *what the project is*; this file describes *how agents must behave
while working in it*. Read both. `RULES.md` and `PROJECT_PLAN.md` remain
authoritative for scoped rules and fixed invariants.

## 1. Verify against official docs before writing code

Before producing or modifying code that targets a Microsoft / .NET technology,
consult the `microsoft-docs` plugin (skills + MCP) for current best practices,
API signatures, and deprecation status. **Do not write .NET / BCL / Azure /
Microsoft SDK code from memory alone.** This project targets `net10.0` across
Common, Daemon API, daemon, and daemon client. The plugin-enabled daemon is an
untrimmed JIT single-file host with trim analysis and source-generated JSON;
outdated or hallucinated API usage is a real correctness and compatibility
risk.

### When this is required

Trigger this check when the task involves any of:

- .NET BCL or C# language features (`System.Text.Json`, `System.Threading.Lock`,
  `FrozenDictionary`, source generators, C# 14 features, etc.).
- Target-framework behavior or publish model (trimming, single-file, Native AOT).
- Microsoft/.NET libraries already in use: TouchSocket, Serilog,
  `Microsoft.Extensions.*`, Brigadier.NET, RustyOptions, JWT, Downloader.
- Any Azure or other Microsoft cloud SDK.

### How to consult (in this order)

1. **Skills** via the `Skill` tool — for primer-level grounding:
   - `microsoft-docs:microsoft-code-reference` when **writing, debugging, or
     reviewing** Microsoft SDK / .NET code. Catches hallucinated methods, wrong
     signatures, deprecated patterns.
   - `microsoft-docs:microsoft-docs` when **understanding** a concept (limits,
     quotas, configuration, behavior) rather than writing code.
2. **MCP tools** — for targeted lookups while coding:
   - `microsoft_docs_search` — breadth-first, official-doc overview.
   - `microsoft_code_sample_search` — working code samples (optional `language`
     filter).
   - `microsoft_docs_fetch` — full page when search is insufficient.
3. **Synthesize** the doc-grounded practice into the code. When the chosen
   approach is non-obvious or contradicts a prior assumption, cite the doc URL
   in the commit message or PR note.

### When this is NOT required

- Pure repo-internal edits: governance docs, comments, renames, refactors that
  do not touch external API surface.
- Trivial mechanical changes: formatting, local-variable renames.
- Edits confined to project vocabulary or Markdown prose.

The goal is **correctness against authoritative sources**, not ritual lookups.
If the same API was already verified in this session and nothing changed, you
do not need to re-query.

### Anti-hallucination rule

If authoritative confirmation cannot be found via `microsoft-docs` for a
Microsoft/.NET API you intend to call, **do not invent it**. Either widen the
search, fetch a candidate doc, or flag the uncertainty to the user before
writing the call.

## 2. Skill / MCP availability

This workflow assumes the `microsoft-docs` plugin is installed
(`microsoft-docs@claude-plugins-official`). If it is unavailable in the current
agent runtime, use the official Microsoft Learn CLI first:

```bash
npx -y @microsoft/learn-cli doctor
npx -y @microsoft/learn-cli search "<query>"
npx -y @microsoft/learn-cli fetch "<learn.microsoft.com URL>"
npx -y @microsoft/learn-cli code-search "<query>"
```

The CLI maps directly to the official Learn MCP search, fetch, and code-sample
tools without requiring a local install. If the CLI is also unavailable, fall
back to official Microsoft Learn via `WebFetch` and **say so explicitly** in
the response. Do not silently substitute blog posts, Stack Overflow, or stale
memory for official docs.

## Changelog

- 2026-06-30: Create `harness.md`. First rule: verify against `microsoft-docs`
  (skills + MCP) before writing any Microsoft / .NET code.
- 2026-07-11: Align framework and publish wording with the approved net10.0,
  untrimmed JIT startup-plugin architecture while retaining the official-docs
  verification workflow.
- 2026-07-11: Add the no-install Microsoft Learn CLI as the verified fallback
  when the `microsoft-docs` plugin or MCP tools are unavailable.
