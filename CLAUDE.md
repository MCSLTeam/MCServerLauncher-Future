# MCServerLauncher Future Agent Index

## Read First

1. `PROJECT_PLAN.md` for product direction and fixed invariants.
2. `RULES.md` for activation-scoped implementation rules.
3. `EXECUTE_PLAN.md` for phase status and backlog.
4. `AGENTS.md` for the full operating guide.
5. `skills/mcsl-future/SKILL.md` for domain-sensitive changes.
6. `harness.md` for agent runtime workflow requirements (e.g. verify against `microsoft-docs` before writing .NET / Microsoft code).

## Short Rules

- Declare touched areas before code or project-rule changes.
- Use only rules relevant to the touched areas plus global rules.
- Preserve Common serialized DTO, Daemon API contract, daemon implementation, daemon-client transport, and WPF boundaries.
- Use the sole `/api/v2` JSON-RPC endpoint plus versioned binary frames; do not add V1 fallback or envelope heuristics.
- Keep protocol/plugin serialization on explicit source-generated `JsonTypeInfo` with no reflection fallback.
- Plugin-enabled publishing is untrimmed JIT single-file plus sidecars; keep trim analysis but do not claim Native AOT/trimmed plugin-host support.
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
- Daemon API: `dotnet build src/MCServerLauncher.Daemon.API/MCServerLauncher.Daemon.API.csproj /m:1`
- DaemonClient: `dotnet build src/MCServerLauncher.DaemonClient/MCServerLauncher.DaemonClient.csproj /m:1`
- Protocol: `dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj /m:1`
- Generated docs: `dotnet run --project tools/MCServerLauncher.ProtocolDocs/MCServerLauncher.ProtocolDocs.csproj -- --check`
- V1 deletion: `pwsh -File tools/VerifyNoV1Runtime.ps1`
- Final hygiene: `git diff --check` and `git status --short`
