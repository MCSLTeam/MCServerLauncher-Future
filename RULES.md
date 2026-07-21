# MCServerLauncher Future Rules

## Activation Scope

Start by declaring touched areas: `docs`, `agent-docs`, `frontend`, `backend`, `protocol`, `serialization`, `installer`, `storage`, `tests`, `benchmarks`, `workflow`, or `integrations`. Apply only the global rules plus rules for touched areas.

## Global Rules

- Do not create or update `docs/superpowers/plans` history files, plan checklists, or plan changelogs unless the user explicitly asks for a durable plan.
- Git commit messages must use concise Conventional Commits: `type(scope): subject`.
- Commit message titles must not contain long explanations.
- Do not revert user or teammate changes without explicit instruction.
- Keep nullable reference types warning-clean in changed C# projects.
- Prefer existing patterns, helpers, project boundaries, and naming.
- Keep edits scoped to the task and update docs when behavior or vocabulary changes.
- Before every commit, run `dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release --no-build` and ensure `MCServerLauncher.ProtocolTests` passes.
- Run `git diff --check` before finishing.

## Domain Rules

- `daemon` means the background service in `MCServerLauncher.Daemon`.
- `daemon client` means `MCServerLauncher.DaemonClient`, not the WPF app.
- `instance` means a managed server or console process with persisted config and lifecycle state.
- `RPC` and `event` are the V2 protocol concepts. Use `action` only for the retiring V1 runtime or migration inventory; do not introduce new V2 `ActionType`-style vocabulary.
- Meta-bearing events must preserve documented missing/null metadata semantics.

## Architecture Boundaries

- Shared wire types belong in `src/MCServerLauncher.Common`.
- Transport-neutral application and plugin contracts belong in `src/MCServerLauncher.Daemon.API`.
- Daemon business behavior belongs behind the application services in `src/MCServerLauncher.Daemon/Application`; daemon console, event rules, transport handlers, and plugins delegate to those services.
- JSON-RPC V2 execution and connection-owned outbound queues belong in `src/MCServerLauncher.Daemon/Remote/Rpc`.
- WPF presentation and interaction logic belongs in `src/MCServerLauncher.WPF`.
- Daemon connection and transport code belongs in `src/MCServerLauncher.DaemonClient`.
- The retiring V1 action generator must not be extended; delete it when the V2 deletion gate is reached.

## Serialization And Protocol

- Use `System.Text.Json`; do not add Newtonsoft.Json.
- Protocol and plugin DTOs require explicit source-generated `JsonTypeInfo`; do not add reflection serialization fallback or assembly scanning for DTO discovery.
- The frozen typed RPC/event catalog is the sole source for daemon dispatch, daemon-client metadata, runtime OpenRPC, and generated checked-in Apifox docs.
- `/api/v2` is the sole release endpoint after cutover. Do not add V1 fallback, legacy envelope auto-detection, dual dispatchers, compatibility switches, or obsolete wrappers.
- Preserve the JSON-RPC profile, remote-event missing/null/object semantics, versioned binary-frame layout, and connection-owned writer ordering defined by the active implementation plan.
- Protocol changes need tests in `tests/MCServerLauncher.ProtocolTests`.
- Byte-oriented transport changes should avoid needless string conversion.

## Daemon Rules

- Public application boundaries use `Task<Result<T, DaemonError>>`. The legacy daemon-internal `Error` type may exist only during migration and must be removed at the V2 deletion gate.
- Use `CancellationToken` on async lifecycle or manager paths when the caller can cancel.
- Use daemon path resolution/validation helpers for user-provided paths.
- Preserve factory-driven instance creation through `IInstanceFactory` and `[InstanceFactory]`.
- Plugin-enabled daemon publishing is untrimmed JIT single-file plus sidecar plugins. Keep trim analysis and source-generated JSON, but do not add or claim Native AOT/trimmed plugin-host support.

## Plugin Rules

- First-milestone plugins are trusted, startup-only, and loaded in one non-collectible `AssemblyLoadContext` per plugin bundle.
- Public plugin features are declared in `mcsl-plugin.json`. The host currently implements `rpc.register`, `event.publish`, and `instance.query`; Preview-1 expands the vocabulary (operations/provisioning/auth/HTTP) under the approved SDK 2.0 plan.
- Do not expose root `IServiceProvider`, TouchSocket, MessagePipe, Serilog, daemon implementation types, mutable instance collections, hooks, factory/installer extension points, or plugin filesystem writes.
- Plugin manifest identity and feature declarations are authoritative; invalid or conflicting plugins log an Error and are skipped atomically without preventing daemon startup.
- Plugin failures must not leave RPC definitions, event slots, cancellation sources, or catalog metadata behind. Successful plugins stop in reverse startup order.

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
- Application/API changes need public-surface, dependency-graph, cancellation, error-model, and immutable-state tests.
- Plugin-host changes need external compile fixtures and published-host integration tests.
- Every commit requires `dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release --no-build` to pass, even when the task only changes non-protocol code.
- Performance-sensitive serialization or transport work needs benchmark coverage and comparison against the checked-in baseline. Equivalent request dispatch mean and allocated bytes may not regress beyond the approved gate without an explicit baseline update.
- Existing known test warnings may be reported, but new warnings should be fixed in touched code.

## Documentation Rules

- Update `PROJECT_PLAN.md` when product scope, architecture, or domain invariants change.
- Update `RULES.md` when a repeated review comment becomes a project rule.
- Update `EXECUTE_PLAN.md` when phase status or backlog meaningfully changes.
- Keep `AGENTS.md` as the full operating guide and `CLAUDE.md` as a compact index.
- Daemon machine-readable protocol docs are generated from the built-in typed catalog. Runtime `rpc.discover` produces OpenRPC for the final frozen runtime catalog; the checked-in `src/MCServerLauncher.Daemon/.Resources/Docs/apifox.json` contains built-ins and must pass the generator `--check` gate.
- Keep generated `apifox.json` shaped like a native Apifox project export: WebSocket APIs live under `webSocketCollection` as `root -> folder -> api` items, omit `api.type`, omit `api.method`, store the send payload in `api.requestBody.message`, and place actual query/header/cookie parameters under `api.parameters`.
- Do not hand-edit generated protocol output.
- Do not add Postman, Swagger, OpenAPI, or HTTP action bridge artifacts back to daemon docs unless the task explicitly changes the documentation strategy.
