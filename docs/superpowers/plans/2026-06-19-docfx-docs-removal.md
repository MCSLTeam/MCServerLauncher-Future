# DocFX Docs Removal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove daemon DocFX documentation scaffolding and stale DocFX references.

**Architecture:** Keep the current machine-readable daemon documentation resources: `apifox.json` and `protocol/topics/*.md`. Delete only the legacy DocFX site files under `MCServerLauncher.Daemon/.Resources/Docs` and remove source comments/docs that still describe DocFX as active.

**Tech Stack:** Markdown, repository documentation, C# comments.

---

### Task 1: Remove DocFX Files

**Files:**
- Delete: `MCServerLauncher.Daemon/.Resources/Docs/docfx.json`
- Delete: `MCServerLauncher.Daemon/.Resources/Docs/index.md`
- Delete: `MCServerLauncher.Daemon/.Resources/Docs/project-view.drawio`
- Delete: `MCServerLauncher.Daemon/.Resources/Docs/toc.yml`
- Delete: `MCServerLauncher.Daemon/.Resources/Docs/docs/getting-started.md`
- Delete: `MCServerLauncher.Daemon/.Resources/Docs/docs/introduction.md`
- Delete: `MCServerLauncher.Daemon/.Resources/Docs/docs/manual.daemon.md`
- Delete: `MCServerLauncher.Daemon/.Resources/Docs/docs/toc.yml`
- Delete: `MCServerLauncher.Daemon/.Resources/Docs/docs/ws-api.md`
- Modify: `MCServerLauncher.WPF/App.xaml.cs`
- Modify: `AGENTS.md`

- [x] **Step 1: Locate DocFX references**

Run: `rg -n "docfx|DocFX|toc\\.yml|project-view|index\\.md|getting-started|manual\\.daemon|ws-api" .`

Expected: find legacy DocFX files and stale references.

- [x] **Step 2: Delete legacy DocFX resources**

Remove the DocFX site config, TOCs, landing pages, Draw.io diagram, and old DocFX markdown pages. Keep `apifox.json` and `protocol/topics/*.md`.

- [x] **Step 3: Remove stale references**

Update the WPF `App.xaml.cs` comment so it no longer mentions DocFX. Update `AGENTS.md` daemon docs guidance so it points at `apifox.json` and `protocol/topics/*.md`.

- [x] **Step 4: Verify**

Run:

```bash
rg -n "docfx|DocFX|toc\\.yml|project-view|index\\.md|getting-started|manual\\.daemon|ws-api" .
git diff --check
git status --short --branch
```

Result: no active DocFX files remain under `MCServerLauncher.Daemon/.Resources/Docs`; only `apifox.json` and embedded `protocol/topics/*.md` resources remain. Remaining search hits are historical plan notes and this removal plan itself.

## Changelog

- 2026-06-19: Removed legacy daemon DocFX site files while retaining Apifox and embedded protocol-topic resources.
