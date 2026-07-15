# Contributing

## Read Order

1. `PROJECT_PLAN.md`
2. `RULES.md`
3. `EXECUTE_PLAN.md`
4. `AGENTS.md`
5. The nearest subproject `AGENTS.md`, if one exists

## Conduct Summary

Use respectful, technical communication. Ground review feedback in evidence. State uncertainty directly. Do not include secrets or private data in commits. Do not revert someone else's work without explicit instruction.

## Workflow

- Declare touched areas before changing code, runnable config, or project rules.
- Use a plan under `docs/superpowers/plans` for architecture, protocol, workflow, or rule changes.
- Keep edits scoped to the task.
- Update docs when behavior, vocabulary, or invariants change.
- Run the smallest relevant verification before submitting.

## Commit Messages

Use concise Conventional Commits:

```text
type(scope): subject
```

Examples:

```text
fix(wpf): validate create instance paths
refactor(daemon): wrap eula operations in result
test(protocol): cover relay packet parsing
docs(workflow): add project rules
```

## Terminology

- Use `daemon` for `MCServerLauncher.Daemon`.
- Use `daemon client` for `MCServerLauncher.DaemonClient`.
- Use `WPF client` for `MCServerLauncher.WPF`.
- Use `action`, `event`, `meta`, and `payload` for protocol concepts.
- Do not rename domain concepts without updating `PROJECT_PLAN.md` and `RULES.md`.

## Verification

- Docs-only: inspect Markdown and run `git diff --check`.
- WPF: `dotnet build src/MCServerLauncher.WPF/MCServerLauncher.WPF.csproj /m:1`.
- Daemon: `dotnet build src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj /m:1`.
- Daemon client: `dotnet build src/MCServerLauncher.DaemonClient/MCServerLauncher.DaemonClient.csproj /m:1`.
- Protocol behavior: `dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj /m:1`.
- Benchmarks: `dotnet run --project benchmarks/MCServerLauncher.Benchmarks/MCServerLauncher.Benchmarks.csproj -c Release`.

## Domain-Sensitive Changes

For daemon protocol, instance lifecycle, file/path handling, installer behavior, serialization, WPF submission flows, or event semantics:

- Read `PROJECT_PLAN.md` and `RULES.md`.
- Use `skills/mcsl-future/SKILL.md`.
- Add or update protocol tests when wire behavior changes.
- Keep AOT/trimming constraints visible in daemon changes.
