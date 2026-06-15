# Project Initialization Plan

Date: 2026-06-15

## Touched Areas

- `docs`: project plan, rules, execution plan, contribution docs, conduct docs.
- `agent-docs`: root agent guide, Claude index, project-specific skill.
- `domain`: daemon, instance, RPC, event, installer, and path vocabulary.
- `frontend`: WPF client workflow and i18n rules.
- `backend`: daemon, daemon client, source generator, protocol tests.
- `integrations`: WebSocket, TouchSocket, Weblate, external launcher projects.
- `workflow`: verification commands and commit conventions.

## Source Material

- `README.md` and `README_ZH.md`
- `AGENTS.md` and subproject `AGENTS.md` files
- `MCServerLauncher.sln`
- Project files under `MCServerLauncher.*/*.csproj`
- Existing daemon documentation under `MCServerLauncher.Daemon/.Resources/Docs/`

## Tasks

- [x] Inspect repo layout, docs, and package metadata.
- [x] Draft `PROJECT_PLAN.md`.
- [x] Draft `RULES.md`.
- [x] Draft `EXECUTE_PLAN.md`.
- [x] Update `AGENTS.md` as the agent workflow entry point.
- [x] Draft `CLAUDE.md`.
- [x] Draft `CONTRIBUTING.md`.
- [x] Keep existing `CODE_OF_CONDUCT.md` and align contribution docs to it.
- [x] Draft `skills/mcsl-future/SKILL.md`.
- [x] Run documentation verification.

## Changelog

- Created `PROJECT_PLAN.md` with product direction, architecture, scope, invariants, and milestones.
- Created `RULES.md` with concrete activation-scoped rules for daemon, client, protocol, WPF, docs, and workflow changes.
- Created `EXECUTE_PLAN.md` with project phases, exit criteria, cross-phase dependencies, and near-term backlog.
- Updated `AGENTS.md` with read-first governance links and touched-area workflow.
- Created `CLAUDE.md` as a compact agent index.
- Created `CONTRIBUTING.md` with workflow, verification commands, terminology, and commit format.
- Created `skills/mcsl-future/SKILL.md` for domain-sensitive project work.
- Rewrote `AGENTS.md` using `skills/lxhtt-init-my-project/SKILL.md` so it is a task-routing operating guide instead of a README-style project summary.
- Integrated the original `AGENTS.md` project structure, commands, code-organization notes, common issues, testing, documentation, and workflow details into the new operating guide.

## Verification

- `rg "TODO|TBD|fill later" ...`: passed; no placeholder markers found in initialized docs.
- `rg "daemon client|WPF client|action|event|Result<T, Error>|Lang.Tr" ...`: passed; expected project terminology appears in governance docs and the project skill.
- `git diff --check`: passed for whitespace and conflict markers; Git reported line-ending conversion warnings for `AGENTS.md` and `CODE_OF_CONDUCT.md` under the current checkout settings.
- `git status --short --branch`: inspected; initialization docs remain uncommitted, with pre-existing user-owned `TASKS.md` deletion and untracked `skills/` contents still present.
- `AGENTS.md` rewrite follow-up: passed; required operating-guide sections and project terminology are present after applying the local `lxhtt-init-my-project` skill.
- `AGENTS.md` structure integration follow-up: passed; original project overview, commands, structure, code organization, style, common issues, performance, documentation, and workflow sections are present alongside the operating-guide sections.

## Follow-Up Risk

- `TASKS.md` is currently deleted before this initialization work; it is treated as user-owned state and is not restored here.
- `skills/init-my-project/`, `skills/lxhtt-init-my-project/`, and `skills/.DS_Store` are user-provided untracked files; this plan only adds the project-specific skill required by the initialization.
