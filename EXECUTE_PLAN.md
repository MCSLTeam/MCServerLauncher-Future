# MCServerLauncher Future Execute Plan

## Current Status

The repository contains a WPF desktop client, .NET daemon, daemon client library, shared protocol contracts, source generators, protocol tests, and benchmarks. Recent work has focused on transport modernization, nullable cleanup, create-instance validation, analyzer release tracking, and backlog completion.

## Phase 1: Protocol And Serialization Stability

Goal: Keep client-daemon contracts stable, trim-safe, and allocation-conscious.

Exit criteria:

- Protocol tests pass.
- Source generator builds without analyzer release-tracking warnings.
- Action/event parsing avoids avoidable string allocations in hot paths.
- Contract changes are reflected in docs and tests.

Task groups:

- Maintain UTF-8 transport boundaries.
- Extend protocol tests for new action/event shapes.
- Keep source-generated JSON coverage current.
- Track benchmark baselines for transport-sensitive changes.

## Phase 2: Daemon Reliability

Goal: Make daemon instance lifecycle, installers, file transfer, and storage paths predictable.

Exit criteria:

- Daemon builds warning-clean.
- Normal failure paths return `Result<T, Error>` or typed daemon errors.
- File/path operations validate trust boundaries.
- Installer workflows support cancellation where practical.

Task groups:

- Harden instance config and settings update flows.
- Audit file transfer and file manager error paths.
- Keep Forge/Fabric/NeoForge installer behavior tested through protocol or integration coverage.
- Validate publish trimming and single-file behavior on publish-sensitive changes.

## Phase 3: WPF Workflow Quality

Goal: Keep user-facing flows clear, validated, localized, and warning-clean.

Exit criteria:

- WPF builds warning-clean with `/m:1`.
- Create-instance forms validate required names, paths, versions, and selections before submission.
- User-facing text is resource-backed.
- Navigation and notification services handle nullable and disconnected states safely.

Task groups:

- Improve remaining stubbed create-instance providers.
- Expand validation messages as localized resource keys.
- Keep instance console workflows resilient to daemon disconnects and transfer failures.
- Continue reducing duplicated WPF component logic.

## Phase 4: Contributor And Agent Workflow

Goal: Make repo work easy to route, verify, and review.

Exit criteria:

- `PROJECT_PLAN.md`, `RULES.md`, `EXECUTE_PLAN.md`, `AGENTS.md`, `CLAUDE.md`, and project skill stay aligned.
- Contribution docs list verification commands and commit format.
- Plans are stored under `docs/superpowers/plans` for rule or architecture changes.

Task groups:

- Keep governance docs short and current.
- Update docs when task routing or invariants change.
- Use `type(scope): subject` commits.

## Cross-Phase Dependencies

- Protocol changes affect daemon, daemon client, WPF, tests, and benchmarks.
- Shared contract changes affect `MCServerLauncher.Common` first.
- WPF UX changes may require daemon-side validation or protocol support.
- Installer behavior depends on storage, network download, mirror settings, and instance factory boundaries.

## Verification Expectations

- Docs-only: inspect Markdown and run `git diff --check`.
- WPF: `dotnet build MCServerLauncher.WPF/MCServerLauncher.WPF.csproj /m:1`.
- Daemon: `dotnet build MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj /m:1`.
- Daemon client: `dotnet build MCServerLauncher.DaemonClient/MCServerLauncher.DaemonClient.csproj /m:1`.
- Generator: `dotnet build MCServerLauncher.Daemon.Generators/MCServerLauncher.Daemon.Generators.csproj /m:1`.
- Protocol: `dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj /m:1`.
- Benchmarks: `dotnet run --project MCServerLauncher.Benchmarks/MCServerLauncher.Benchmarks.csproj -c Release`.

## Near-Term Backlog

- Localize detailed create-instance validation messages instead of reusing generic missing-data text.
- Recreate or replace `TASKS.md` if the team wants a lightweight checklist in addition to this execution plan.
- Add focused tests for async daemon-client event subscribers.
- Review README_ZH target framework statements; they appear older than the current `.csproj` targets.
