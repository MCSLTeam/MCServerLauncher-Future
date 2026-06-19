# Internal Project Layout Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reorganize project-internal folders so each project groups files by feature and responsibility without changing runtime behavior.

**Architecture:** Keep namespaces, protocol contracts, XAML class names, and public APIs stable. Move files into clearer folders, then update XAML, project-item, documentation, and path-sensitive references that depend on physical locations. Defer large-file splitting to a later task.

**Tech Stack:** C# 14, .NET 10, WPF XAML, xUnit protocol tests, Visual Studio solution/project files.

---

### Task 1: Daemon Project Folder Grouping

**Files:**
- Move within `src/MCServerLauncher.Daemon/Remote/Action/Handlers/`
- Move daemon startup/root files under `src/MCServerLauncher.Daemon/App/` if references allow a low-risk move.
- Update daemon project docs.

- [ ] Group action handlers by responsibility: `System`, `Instances`, `Files`, and `Events`.
- [ ] Keep namespaces stable unless the compiler requires a targeted update.
- [ ] Leave `Management`, `Storage`, `Serialization`, and `Bootstrap` behavior unchanged.

### Task 2: Daemon Client Folder Grouping

**Files:**
- Move public daemon client API helpers under a clear API folder.
- Keep connection and WebSocket plugin folders intact unless references are purely physical.
- Update daemon client project docs.

- [ ] Group high-level client entry points and extensions separately from transport internals.
- [ ] Preserve public API names and namespaces.

### Task 3: WPF Feature Folder Grouping

**Files:**
- Move WPF provider folders under feature-oriented subfolders.
- Update XAML `x:Class`, local namespace mappings, code-behind references, and project docs when physical paths require it.

- [ ] Group create-instance provider, pre-create provider, and related create-instance components together.
- [ ] Group resource-download provider and resource-download item views together.
- [ ] Leave translations submodule, resources, and app entry files in place.

### Task 4: Protocol Test Folder Grouping

**Files:**
- Move tests under `tests/MCServerLauncher.ProtocolTests/` into feature folders such as `Rpc`, `Transport`, `Persistence`, `Configuration`, `Registry`, and `PublicContracts`.
- Update path-sensitive helper references only when needed.

- [ ] Group transport tests together.
- [ ] Group RPC and serialization characterization tests together.
- [ ] Group project configuration, registry, and public contract tests into obvious folders.
- [ ] Keep fixtures and helpers in their existing roots unless a move is mechanically required.

### Task 5: Verification And Documentation

**Files:**
- Modify active docs that describe project-internal structure.
- Build/test changed projects and inspect final diff.

- [ ] Update `AGENTS.md`, project `AGENTS.md` files, and `PROJECT_PLAN.md`/`EXECUTE_PLAN.md` if internal structure descriptions change.
- [ ] Run `dotnet build src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj /m:1`.
- [ ] Run `dotnet build src/MCServerLauncher.WPF/MCServerLauncher.WPF.csproj /m:1`.
- [ ] Run `dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj /m:1`.
- [ ] Run `dotnet build MCServerLauncher.sln /m:1`.
- [ ] Run `git diff --check`.

## Changelog

- Planned low-risk project-internal folder grouping after the top-level `src/`, `tests/`, `benchmarks`, and `generators` migration.
