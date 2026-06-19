# MCServerLauncher Future Rules

## Activation Scope

Start by declaring touched areas: `docs`, `agent-docs`, `frontend`, `backend`, `protocol`, `serialization`, `installer`, `storage`, `tests`, `benchmarks`, `workflow`, or `integrations`. Apply only the global rules plus rules for touched areas.

## Global Rules

- Every AI programming task must have a plan under `docs/superpowers/plans`.
- Agents must use `superpowers:writing-plans` before implementation.
- Agents must execute implementation plans with `superpowers:subagent-driven-development` unless the user explicitly chooses a different mode.
- Agents must write a changelog section in the plan before finishing.
- Git commit messages must use concise Conventional Commits: `type(scope): subject`.
- Commit message titles must not contain long explanations.
- Do not revert user or teammate changes without explicit instruction.
- Keep nullable reference types warning-clean in changed C# projects.
- Prefer existing patterns, helpers, project boundaries, and naming.
- Keep edits scoped to the task and update docs when behavior or vocabulary changes.
- Before every commit, run `dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release --no-build` and ensure `MCServerLauncher.ProtocolTests` passes.
- Run `git diff --check` before finishing.

## Domain Rules

- `daemon` means the background service in `MCServerLauncher.Daemon`.
- `daemon client` means `MCServerLauncher.DaemonClient`, not the WPF app.
- `instance` means a managed server or console process with persisted config and lifecycle state.
- `action` and `event` are protocol concepts; do not rename them casually.
- Meta-bearing events must preserve documented missing/null metadata semantics.

## Architecture Boundaries

- Shared wire types belong in `MCServerLauncher.Common`.
- Daemon RPC action execution belongs in `MCServerLauncher.Daemon/Remote/Action`.
- WPF presentation and interaction logic belongs in `MCServerLauncher.WPF`.
- Daemon connection and transport code belongs in `MCServerLauncher.DaemonClient`.
- Source-generator diagnostics and registry generation belong in `MCServerLauncher.Daemon.Generators`.

## Serialization And Protocol

- Use `System.Text.Json`; do not add Newtonsoft.Json.
- Prefer source-generated or explicitly owned serializers for daemon and protocol paths.
- Keep daemon AOT/trimming constraints in mind; avoid reflection-dependent runtime discovery unless a trim boundary is explicit.
- Protocol changes need tests in `MCServerLauncher.ProtocolTests`.
- Byte-oriented transport changes should avoid needless string conversion.

## Daemon Rules

- Use `Result<T, Error>` where daemon operations can fail as part of normal control flow.
- Use `CancellationToken` on async lifecycle or manager paths when the caller can cancel.
- Use daemon path resolution/validation helpers for user-provided paths.
- Preserve factory-driven instance creation through `IInstanceFactory` and `[InstanceFactory]`.
- Publish-sensitive changes should check trimming or single-file warnings.

## WPF Rules

- Use `Lang.Tr[...]` for user-facing strings.
- Keep UI validation close to provider or component boundaries and avoid submitting known-invalid paths, names, or selections.
- Use existing iNKORE.UI.WPF.Modern patterns and resources.
- Build WPF single-threaded with `/m:1` to avoid transient generated-XAML issues.
- Do not make frontend-only checks authoritative for daemon security or path safety.

## Installer Rules

- Preserve Forge-family installer differences and shared cache behavior.
- Validate downloaded or cached libraries with checksums when data provides them.
- Keep mirror handling explicit and tied to user settings.

## Tests And Benchmarks

- Protocol behavior changes need protocol tests.
- Every commit requires `dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release --no-build` to pass, even when the task only changes non-protocol code.
- Performance-sensitive serialization or transport work needs a benchmark update or a clear reason why existing coverage is sufficient.
- Existing known test warnings may be reported, but new warnings should be fixed in touched code.

## Documentation Rules

- Update `PROJECT_PLAN.md` when product scope, architecture, or domain invariants change.
- Update `RULES.md` when a repeated review comment becomes a project rule.
- Update `EXECUTE_PLAN.md` when phase status or backlog meaningfully changes.
- Keep `AGENTS.md` as the full operating guide and `CLAUDE.md` as a compact index.
- Daemon machine-readable protocol docs are Apifox-only. Keep `MCServerLauncher.Daemon/.Resources/Docs/apifox.json` shaped like a native Apifox project export: WebSocket APIs live under `webSocketCollection` as `root -> folder -> api` items, omit `api.type`, omit `api.method`, store the send payload in `api.requestBody.message`, and place actual query/header/cookie parameters under `api.parameters`.
- Do not add Postman, Swagger, OpenAPI, or HTTP action bridge artifacts back to daemon docs unless the task explicitly changes the documentation strategy.
