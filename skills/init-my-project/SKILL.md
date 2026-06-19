---
name: init-my-project
description: Bootstrap reusable project governance for a new or existing repo. Use when the user asks to initialize project docs, create project rules, write project plans, create EXECUTE_PLAN.md, AGENTS.md, CLAUDE.md, CONTRIBUTING.md, CODE_OF_CONDUCT.md, or create a project-specific skill from repo conventions.
---

# Init My Project

## Quick start

Use this skill to turn a repo into an agent-friendly project with clear product direction, phase planning, rules, agent workflow, and a compact project-specific skill.

1. Inspect the current repo before writing files.
2. Ask only for missing facts that cannot be inferred safely.
3. Create or update the governance docs listed below.
4. Keep project-specific facts in `PROJECT_PLAN.md` and `RULES.md`.
5. Keep phase and task planning in `EXECUTE_PLAN.md`; keep agent workflow in `AGENTS.md` and `CLAUDE.md`.
6. Create one project-specific skill under `skills/{project-slug}/SKILL.md`.
7. Add a plan under `docs/superpowers/plans` and record a changelog before finishing.

## Required inputs

Gather these facts from the repo or the user:

- Project name and short codename.
- Product purpose in one paragraph.
- Repo layout and main apps or packages.
- Primary tech stack.
- Business or domain invariants that must not drift.
- Preferred vocabulary and forbidden terms.
- Verification commands for docs, frontend, backend, database, and dependency changes.
- Commit message format.
- Optional external tool or skill policy, only if the project already uses one.

If the user wants a minimal setup, create the files with concise sections and mark unknown facts as decisions to confirm in normal prose. Do not use placeholder strings such as `TODO`, `TBD`, or `fill later`.

## Generated files

Create or update these files when they fit the repo:

- `PROJECT_PLAN.md`: product direction, audience, scope, architecture, business rules, milestones.
- `RULES.md`: implementation rules derived from the project plan.
- `EXECUTE_PLAN.md`: project phases, phase exit criteria, and tasks per phase.
- `AGENTS.md`: main agent operating guide.
- `CLAUDE.md`: short index for Claude-style agents. Keep it as an index, not a duplicate rulebook.
- `CONTRIBUTING.md`: contributor workflow, verification, commit format.
- `CODE_OF_CONDUCT.md`: collaboration standards.
- `skills/{project-slug}/SKILL.md`: compact domain workflow for project-sensitive work.
- `docs/superpowers/plans/YYYY-MM-DD-<topic>.md`: the implementation plan for the initialization work.

Do not create extra README-like files inside the skill folder. A skill should only include files that help the agent do the job.

## Initialization workflow

1. Declare touched areas. Use a default set such as `docs`, `agent-docs`, `frontend`, `backend`, `database`, `domain`, and `integrations`, then adapt it to the repo.
2. Read existing docs and package metadata.
3. Write or update the plan under `docs/superpowers/plans`.
4. Draft `PROJECT_PLAN.md` first because it owns product direction.
5. Draft `RULES.md` from the plan. Rules should be concrete and testable.
6. Draft `EXECUTE_PLAN.md` as the project phase roadmap.
7. Draft `AGENTS.md` as the full agent guide.
8. Draft `CLAUDE.md` as a short index with links and the sharpest rules.
9. Draft `CONTRIBUTING.md` and `CODE_OF_CONDUCT.md`.
10. Draft the project-specific skill with only the project-sensitive workflow and invariants.
11. Run verification based on changed files.
12. Add a changelog to the plan with files changed, checks run, and follow-up risk.

## Document rules

`PROJECT_PLAN.md` should answer:

- What is the product?
- Who uses it?
- What problem does it solve?
- What is in scope now?
- What is out of scope?
- What architecture and stack are expected?
- Which business or domain rules are fixed unless the plan changes?

`RULES.md` should include:

- Product invariants.
- Architecture boundaries.
- Naming and vocabulary.
- Frontend rules, if the repo has a frontend.
- Backend rules, if the repo has a backend.
- Database rules, if the repo has persistence.
- Documentation rules.
- Rule activation scope so agents do not waste context on unrelated checks.
- Optional external tool or skill rules, only if the repo already needs them.

`EXECUTE_PLAN.md` should include:

- Current project status.
- Each delivery phase in order.
- Goal and exit criteria for every phase.
- Task groups for every phase.
- Cross-phase dependencies.
- Cross-phase verification expectations.
- Near-term backlog.

`AGENTS.md` should include:

- Fast start.
- Project map.
- Task routing.
- AI programming workflow.
- Source of truth.
- Vocabulary.
- Stack-specific guidance.
- Business or domain checks.
- Verification.
- Do-not list.

`CLAUDE.md` should include:

- Read-first index.
- Short rules.
- Touched areas.
- When to use the project skill.
- Optional external tool or skill policy, only when present.
- Verification hints.

`CONTRIBUTING.md` should include:

- Read order.
- Conduct summary.
- AI programming workflow.
- Commit message format.
- Terminology.
- Verification.
- Domain-sensitive change process.

`CODE_OF_CONDUCT.md` should be short and direct:

- Respectful, technical communication.
- Evidence-based review.
- No hidden uncertainty.
- No reverting someone else's work without explicit instruction.
- No secrets or private data in commits.

## Project-specific skill recipe

Create `skills/{project-slug}/SKILL.md` with:

```md
---
name: project-slug
description: Guides work that changes or reviews Example Project domain behavior. Use for permissions, data ownership, lifecycle state, integrations, or other domain-sensitive areas, not ordinary docs-only edits.
---

# Example Project

## Quick start

1. Read `PROJECT_PLAN.md`.
2. Read `RULES.md`.
3. Declare touched areas.
4. Use this skill only for domain-sensitive changes.
5. Preserve the invariants below.

## Invariants

- User-owned records must not be reassigned without an audited operation.
- Permission changes must preserve the documented role hierarchy.

## Task recipes

**Permissions.** Read `PROJECT_PLAN.md`, preserve role boundaries, and add behavior tests when executable code exists.

**Data ownership.** Preserve ownership and audit rules. Do not move records between owners without an explicit product rule.

## Red flags

- A frontend-only check becomes authoritative for permissions, ownership, or lifecycle state.
- Domain vocabulary changes without updating `PROJECT_PLAN.md` and `RULES.md`.

## Verification

- Docs: inspect Markdown and run terminology searches.
- Code: run the smallest relevant test or build.

## Commits

Use `type(scope): subject`.
```

Keep this skill under 100 lines when possible. Move details back into `PROJECT_PLAN.md` or `RULES.md` instead of bloating the skill.

## Execution rules to include

Every initialized project should get these working norms unless the user opts out:

- Start AI programming tasks by declaring touched areas.
- Apply only global rules plus rules for touched areas.
- Use `superpowers:writing-plans` before code, runnable config, or project rule changes.
- Store plans under `docs/superpowers/plans`.
- Execute plans with `superpowers:subagent-driven-development` by default.
- Keep plan task status updated.
- Add a changelog to the plan before finishing.
- Use concise Conventional Commits: `type(scope): subject`.

Only include external tool or skill installation rules when the project explicitly depends on installable external skills. If included, require user review before installation and prevent duplicate installs.

## Verification

Pick checks from touched areas:

- Docs: inspect changed Markdown and run `rg` for changed terminology.
- Package metadata: parse changed package files with the native tool, such as Node for `package.json`.
- Frontend: run lint, typecheck, build, and file-type scans when relevant.
- Backend: run module listing, vet, test, or build based on the stack.
- Database: run migration checks or schema validation when available.
- Dependencies: run install or lockfile checks and peer checks when available.

Before finishing, run `git diff --check` and inspect `git status --short`. Report user-owned or generated files that were intentionally left out.

## Handoff

End with:

- Files created or changed.
- Verification run and result.
- Any unresolved risk.
- Whether commits were created.

Never claim work is complete before fresh verification supports that claim.
