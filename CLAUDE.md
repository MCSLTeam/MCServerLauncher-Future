# MCServerLauncher Future Agent Index

## Read First

1. `PROJECT_PLAN.md` for product direction and fixed invariants.
2. `RULES.md` for activation-scoped implementation rules.
3. `EXECUTE_PLAN.md` for phase status and backlog.
4. `AGENTS.md` for the full operating guide.
5. `skills/mcsl-future/SKILL.md` for domain-sensitive changes.

## Short Rules

- Declare touched areas before code or project-rule changes.
- Use only rules relevant to the touched areas plus global rules.
- Preserve daemon/client/protocol boundaries.
- Keep daemon serialization AOT and trim friendly.
- Use `Lang.Tr[...]` for WPF user-facing text.
- Run the smallest relevant build or test, then `git diff --check`.
- Before every commit, run the full `MCServerLauncher.ProtocolTests` suite and ensure it passes.
- Commit format: `type(scope): subject`.

## Touched Areas

Common areas are `docs`, `agent-docs`, `frontend`, `backend`, `protocol`, `serialization`, `installer`, `storage`, `tests`, `benchmarks`, `workflow`, and `integrations`.

## Project Skill

Use `skills/mcsl-future/SKILL.md` for work that changes daemon protocol behavior, instance lifecycle, file ownership/path handling, installer logic, WPF create-instance submission, serialization, or event semantics. Do not use it for trivial docs-only edits.

## Verification Hints

- WPF: `dotnet build src/MCServerLauncher.WPF/MCServerLauncher.WPF.csproj /m:1`
- Daemon: `dotnet build src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj /m:1`
- DaemonClient: `dotnet build src/MCServerLauncher.DaemonClient/MCServerLauncher.DaemonClient.csproj /m:1`
- Generator: `dotnet build generators/MCServerLauncher.Daemon.Generators/MCServerLauncher.Daemon.Generators.csproj /m:1`
- Protocol: `dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj /m:1`
- Final hygiene: `git diff --check` and `git status --short`
