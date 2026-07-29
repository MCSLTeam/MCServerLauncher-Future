# MCSL Future HTTP MCP Plugin Plan

## Status And Dependency Gate

- Planning date: 2026-07-19.
- **Decision freeze:** 2026-07-20. Authoritative overrides live in
  `docs/spec/2026-07-20-plugin-sdk-mcp-decisions.md`.
  When this plan and that spec disagree, **the spec wins**.
- Delivery stages: **MCP-0..5** after SDK Preview-1 packages; **MCP-6..7**
  after SDK Preview-2. Full end-state inventory remains mandatory.
- PR #52 is merged into `master`; it is a historical prerequisite, not the
  implementation branch for this plugin.
- This is a separate project/repository and delivery unit.
- MCP-0..5 implementation is blocked until the SDK plan publishes an accepted
  exact **Preview-1** `MCServerLauncher.Daemon.Plugin.Sdk 2.0.0-preview.N` and
  matching Daemon API package. Replace `N` with the accepted version and
  package hash before the first implementation commit. Do not wait for backup/
  monitoring/automation/audit Preview-2 domains.
- Package id is `mcp` (not `mcsl.mcp`); RPC/permission namespace is `plugin.mcp.*`.
- The verified official MCP C# SDK baseline is `v1.4.1`, published 2026-07-09.
  Pin exact `ModelContextProtocol.AspNetCore`; use the Tasks API from its core
  dependency chain and do not use floating versions.
- The Tasks extension is experimental in v1.4.1. Opt in locally, isolate it
  behind an adapter, and treat an SDK upgrade as an explicit compatibility PR.

## Goal

Deliver an HTTP-only daemon plugin that lets an external AI MCP client turn
natural-language operator intent into safe structured daemon workflows:

- resolve, validate, provision, and start a Minecraft server;
- observe long-running download/install/backup progress;
- query system, instance, log, metrics, event, operation, and audit state;
- perform routine lifecycle operations;
- create, test, and enable deterministic automatic-operations policies;
- plan and confirm sensitive or destructive mutations.

The MCP plugin does not embed an LLM, interpret natural language itself, or
store a model-provider API key. The MCP client/model performs language
interpretation and calls curated typed tools/resources.

## Touched Areas

In the MCP project/repository:

- `backend`
- `protocol`
- `serialization`
- `storage`
- `tests`
- `benchmarks`
- `docs`
- `workflow`
- `integrations`

The MCSL Future repository is touched only for accepted SDK package/version
documentation or integration fixtures explicitly owned by the SDK plan.

## Fixed Boundaries

1. Streamable HTTP only. No stdio transport, bridge process, or legacy SSE
   endpoint.
2. Use the official MCP C# SDK; do not hand-roll MCP framing.
3. Run stateless Streamable HTTP at `/mcp`.
4. Start an independent plugin-owned Kestrel listener after daemon plugin
   activation. Do not share the TouchSocket `/api/v2` port. Load ASP.NET/MCP
   assemblies privately in the plugin ALC (daemon shares DI.Abstractions only
   among Microsoft.Extensions stacks needed for type identity).
5. Default to loopback. Public exposure requires an external TLS reverse proxy
   or identity-aware gateway; the plugin does not implement an OAuth 2.1
   authorization server in the first release.
6. The plugin calls generated, feature-gated application facades directly in
   process via **ForPrincipal** for every tool. No loopback `DaemonClient`,
   raw RPC method strings, `rpc.invoke`, service locator, or silent Host
   fallback for user tools.
7. Keep a curated tool/resource inventory. Do not automatically export the
   frozen RPC catalog. MCP-0..5 inventory is the install-server main path only;
   MCP-6+ completes the full inventory in this plan.
8. Use stateless MCP Tasks plus polling for long work. Do not promise server
   push; v1.4.1 Tasks explicitly relies on polling. Durable task mappings live
   in `storage.private`; operations remain daemon-owned.
9. Plugin `config.json` is an immutable cold-start snapshot. No watcher, hot
   reload, mutation endpoint, or writeback.
10. Every request passes plugin feature, caller permission, and MCP policy
    gates; execution rechecks all gates under CallerContext proxies.
11. Destructive plans (MCP-6+) are immutable, expiring, version checked, hash
    bound, and single use. Preview-1 provisioning is **routine**: permissions
    suffice for `plan_execute` (no `plan_confirm`).
12. The daemon, not the MCP plugin, owns provisioning providers, operations,
    backups, monitoring, automation execution, and audit facts.
13. Package id is `mcp`; permission/RPC namespace `plugin.mcp.*`. Do not request
    `rpc.register` / `event.*` in the first release.
14. `auth.allow_main_token` defaults to **true** (local superuser convenience).
    Main token is recognized only through daemon `auth.verify` with
    `AllowMainToken`. MCP does **not** expose token issuance tools; issuance is
    daemon `mcsl.auth.token.issue` (main-token only).
15. MCP-0 published-host Kestrel spike is a **hard gate** before business tools.

## Repository And Project Layout

Create an independent repository, recommended name
`MCSLTeam/MCServerLauncher.MCP`:

```text
MCServerLauncher.MCP/
  src/MCServerLauncher.MCP/
    Plugin/
    Configuration/
    Hosting/
    Authentication/
    Authorization/
    Policy/
    Tools/
      Read/
      Lifecycle/
      Provisioning/
      Files/
      Backups/
      Automation/
      Plans/
    Resources/
    Tasks/
    Serialization/
    Storage/
    mcsl-plugin.json
    config.example.json
  tests/MCServerLauncher.MCP.Tests/
  tests/MCServerLauncher.MCP.IntegrationTests/
  benchmarks/MCServerLauncher.MCP.Benchmarks/
  docs/
  MCServerLauncher.MCP.slnx
```

Target `net10.0`, C# 14, nullable enabled, warning clean, source-generated
JSON, and untrimmed JIT plugin publishing. The project must set the SDK's plugin
bundle build property so shared MCSL contract assemblies are excluded from the
sidecar bundle.

## Exact Package Policy

Pin:

```text
MCServerLauncher.Daemon.Plugin.Sdk = accepted 2.0.0-preview.N
ModelContextProtocol.AspNetCore = 1.4.1
```

Do not add `ModelContextProtocol` separately when already transitively owned by
the ASP.NET Core package unless implementation needs a direct compile asset.
The experimental Tasks API in 1.4.1 is part of that core dependency chain;
there is no separate `ModelContextProtocol.Extensions.Tasks` 1.4.1 package.
Record package lock files and license inventory. SDK upgrades require protocol,
auth, Tasks, stateless concurrency, and published-host reruns.

## MCP Plugin Manifest

The initial manifest requests only features the plugin consumes:

```json
{
  "$schema": "https://mcsl-team.github.io/schemas/mcsl-plugin-2.0.schema.json",
  "package": {
    "id": "mcp",
    "version": "0.1.0"
  },
  "entry": {
    "assembly": "MCServerLauncher.MCP.dll",
    "type": "MCServerLauncher.MCP.Generated.DaemonPluginAdapter"
  },
  "requires": {
    "api": "[2.0.0,3.0.0)",
    "features": [
      "auth.verify",
      "instance.manage",
      "instance.query",
      "network.http.listen",
      "operation.cancel",
      "operation.query",
      "provisioning.manage",
      "storage.private",
      "system.query"
    ]
  }
}
```

**MCP-0..5** requests only the Preview-1 feature set above. MCP-6+ may add
`file.*`, `backup.manage`, `automation.manage`, `audit.query`, `event-rule.manage`,
`monitoring.query` when SDK Preview-2 grants them.

Do not request `rpc.register`, `event.publish`, or `event.subscribe` in the
first release: the MCP endpoint is independent HTTP, monitoring/automation are
daemon-owned, and real-time MCP push is out of scope.

## Cold-Start `config.json`

### Minimal Schema

```json
{
  "http": {
    "listen": "http://127.0.0.1:11453",
    "path": "/mcp",
    "canonical_uri": "http://127.0.0.1:11453/mcp"
  },
  "auth": {
    "allow_main_token": true
  },
  "tools": {
    "disabled": []
  },
  "history": {
    "retention_days": 7,
    "maximum_bytes": 268435456
  }
}
```

Rules:

- Bind loopback by default and reject wildcard/non-loopback binding unless
  explicitly configured. IP:port must not collide with daemon port or another
  plugin listener (`network.http.listen` registration).
- Normalize `path` to one absolute route.
- `canonical_uri` is the token audience/resource. It must be absolute and
  stable; a public reverse proxy uses its external HTTPS MCP URI here.
- Authentication is always required. `auth.allow_main_token` (default true)
  allows the daemon main token as MCP superuser via `auth.verify` options; it
  is not an "auth off" switch.
- `disabled` accepts only known tool ids and is validated at startup.
- MCP-0..5 has no automation block and no `allow_sensitive_auto_execute`; those
  return with MCP-6+ when destructive/sensitive consumers exist.
- Config contains no bearer token, signing secret, daemon main token value,
  model key, provider URL override, or plugin data path.
- Unknown fields, invalid values, or missing explicit JSON metadata fail plugin
  startup atomically.
- Read exactly once before Kestrel starts.

The 7-day/256-MiB plugin history cap applies to MCP task mappings and
MCP-specific request history under `storage.private` (stricter of history vs
storage quota wins). Daemon operations, plans, backups, policies, and audit
remain in daemon-owned stores.

## Lifecycle And Independent Kestrel Hosting

1. Generated adapter validates manifest/config and builds plugin-private DI.
2. Plugin `StartAsync` waits for daemon activation.
3. Validate the configured binding through `network.http.listen`.
4. Build one plugin-owned ASP.NET Core host using the private service
   collection, official MCP services, authentication, policy, tools, resources,
   and singleton task store.
5. Bind only the configured address/path and publish endpoint diagnostics.
6. Return from startup only after Kestrel is listening or fail atomically.
7. On daemon shutdown, stop accepting requests, cancel request/task adapters,
   stop Kestrel with a deadline, flush bounded state, then dispose the private
   provider.

Kestrel listener failure must skip/fail this plugin without preventing the
daemon's own `/api/v2` listener from serving.

## Authentication And Per-Request Authorization

> Authoritative detail: decision spec §5 and §9.

### Request Flow

1. Require `Authorization: Bearer <token>` on every MCP GET/POST route.
2. Pass the token, configured `canonical_uri`, and
   `AllowMainToken = config.auth.allow_main_token` to generated `auth.verify`.
3. Receive an immutable verified principal with subject, token id, audience,
   expiry, and permission set (main token → `sub=daemon-main`, `perms=*` when
   allowed).
4. Store only the principal in request-scoped ASP.NET Core state.
5. Bind generated authorized application features with **ForPrincipal** — never
   Host for tool execution.
6. Filter unavailable entries from `tools/list` by whether the principal can
   pass the underlying `mcsl.*` method checks.
7. Recheck permission inside every tool proxy before the application call.

Reject missing/malformed/expired/wrong-audience tokens without revealing which
validation step failed. Never log tokens or return them in MCP errors/audit.

### First-Release Deployment Model

- Local default: main token may be used when `allow_main_token=true`.
- Recommended multi-token: issue audience-bound JWTs via daemon
  `mcsl.auth.token.issue` (main-token caller only on `/api/v2` or CLI wrapper).
  MCP does not expose issuance tools.
- JWT `aud` must equal `canonical_uri`. `/api/v2` uses
  `security.api_canonical_uri` and dual-accepts legacy audience for one period.
- Public Internet deployment requires an external IdP/reverse proxy/TLS design
  and a separate security review.
- Do not advertise OAuth protected-resource metadata unless a compatible
  external authorization deployment is actually configured.

### Automation Principal

Deferred to MCP-6+ / SDK Preview-2. When present, unattended policies use a
daemon service principal, never an interactive MCP request principal.

## MCP Tasks And Daemon Operations

The official v1.4.1 Tasks extension is polling-only and experimental. Implement
one adapter layer so its wire/API changes do not spread into domain tools.

### Task Store

- Implement a singleton durable `IMcpTaskStore`; do not use the SDK in-memory
  store outside tests.
- Under stateless HTTP, the same singleton is shared across all request-created
  MCP server instances.
- Persist MCP task id, owning principal/token id, tool, daemon operation id,
  created/updated/expiry, terminal tool result, and outstanding confirmation or
  input request.
- Enforce task ownership: one principal cannot poll/cancel another principal's
  task unless it has an explicit administrator permission.
- Terminal writes are idempotent; late cancellation cannot replace completion.
- Clean up by age and the plugin history byte cap.

### Operation Mapping

- Long tool calls create or receive a daemon `operationId`.
- MCP task state maps daemon queued/running to `working`, daemon success/domain
  error to a completed MCP tool result, cancellation to `cancelled`, and
  unexpected protocol failure to `failed`.
- Daemon `interrupted` becomes a completed domain error that truthfully tells
  the model the daemon restarted; do not fabricate resumption.
- `tasks/cancel` requests cooperative daemon cancellation only when the
  caller has `mcsl.operation.cancel`.
- Suggest a one-second poll interval initially and honor bounded backoff.
- Task polling includes the current stage in status text and returns a link to
  `mcsl://operations/{id}` for full progress.
- Progress percentage/bytes are authoritative only from the daemon operation
  snapshot. Do not infer progress from elapsed time.
- Persist task mappings under plugin `storage.private` (not daemon operation
  storage).

Clients without the Tasks extension receive an immediate structured result
containing `operationId`, current snapshot, and resource URI; they can poll
with `operation_get` or the resource.

## Resource Inventory

### MCP-0..5 (Preview-1)

```text
mcsl://daemon/status
mcsl://instances
mcsl://instances/{id}
mcsl://instances/{id}/logs
mcsl://operations/{id}
```

### MCP-6+ (still mandatory end-state)

```text
mcsl://metrics/system
mcsl://metrics/instances/{id}
mcsl://events/recent
mcsl://audit/recent
```

Rules:

- Every URI parser uses typed identifiers and daemon-side validation.
- List/log/history resources have explicit limits, cursors, and time ranges.
- Resource reads use ForPrincipal and recheck the corresponding method
  permissions.
- Logs are bounded, redact configured secrets where daemon contracts support
  it, and never accept arbitrary filesystem paths.
- Operation resources expose stage/progress/result references but no internal
  paths or stack traces.
- Resource templates are deterministic and do not expose resources the
  principal cannot read.

## Curated Tool Inventory

Tool names are stable snake_case identifiers. Every input/output DTO is
source-generated JSON. Tool authorization checks the underlying **method-name
permissions** (`mcsl.*`) via ForPrincipal — not a separate MCP permission graph.

### MCP-0..5 (Preview-1) — frozen

#### Read Tools

| Tool | Purpose | Underlying methods (illustrative) |
|---|---|---|
| `daemon_status` | Daemon time/version/features and high-level health. | `mcsl.system.info.get` (+ related) |
| `instance_list` | List immutable instance summaries. | `mcsl.instance.catalog.get` / report.list |
| `instance_get` | Read report/settings for one instance. | `mcsl.instance.report.get`, settings.get |
| `instance_logs` | Read bounded retained log lines. | `mcsl.instance.log.get` |
| `operation_list` | List caller-visible operations (owner-scoped). | `mcsl.operation.list` |
| `operation_get` | Read stage/progress/result. | `mcsl.operation.get` |
| `plan_get` | Read immutable provisioning plan (ready/blocked). | `mcsl.provisioning.get` |

#### Lifecycle And Provisioning Tools

| Tool | Behavior | Notes |
|---|---|---|
| `provision_plan` | Resolve structured intent → ready/blocked plan. | `mcsl.provisioning.resolve` |
| `plan_execute` | Execute ready routine plan → operation. | `mcsl.provisioning.execute`; no confirm in P1 |
| `instance_start` | Enter Starting; poll report for Running. | `mcsl.instance.start` |
| `instance_stop` | Enter Stopping; poll for Stopped. | `mcsl.instance.stop` |
| `instance_restart` | Tool orchestration: stop → wait → start. | no daemon restart method |
| `operation_cancel` | Cooperative cancel when permitted. | `mcsl.operation.cancel` |

#### Resources (MCP-0..5)

```text
mcsl://daemon/status
mcsl://instances
mcsl://instances/{id}
mcsl://instances/{id}/logs
mcsl://operations/{id}
```

### MCP-6+ (full plan inventory — still mandatory end-state)

Deferred until SDK Preview-2: `instance_metrics`, `file_*`, `backup_*`,
`automation_*`, `audit_recent`, `instance_change_plan`, `file_change_plan`,
`backup_restore_plan`, `event_rule_plan`, `automation_plan`, `plan_confirm`,
and related resources.

Do not add a generic raw-method tool, general shell, dynamic C#, unrestricted
console-command background action, arbitrary URL provider tool, automatic RPC
export, or MCP-side token issuance tool.

## Safety And Confirmation Policy

> Daemon owns the generic plan kernel. MCP maps tools onto it.

### Risk Classes

- **Read:** immediate.
- **Routine (MCP-0..5):** provisioning execute after ready plan; start/stop/
  restart orchestration. Permission gated; **no** `plan_confirm`.
- **Sensitive / Destructive (MCP-6+):** settings/file/restore/automation/remove/
  etc. Immutable plan + confirmation as in the full inventory; auto-execute
  only where cold config explicitly allows sensitive (never destructive).

### Immutable Plan Rules (daemon kernel)

- Include plan id/hash/version, creator principal, target facts/versions,
  exact action, risk, expiry, required permissions, and expected side effects.
- Blocked plans persist; only ready plans execute; completing intent creates a
  new plan id.
- Default expiry 15 minutes; revalidate on execute; single-use CAS.
- Permission reduction, target drift, or hash mismatch invalidates execution.
- Confirmation (when required) is bound to the same principal unless an
  administrator approval workflow is later designed.

## Primary Workflow Contracts

### Natural Language To Running Server

The AI client:

1. Reads daemon/system/Java status.
2. Calls `provision_plan` with structured intent derived from user language.
3. Presents unresolved choices or EULA requirements only when the plan reports
   them as blocked; the plugin does not run an LLM.
4. Calls `plan_execute` when the plan is ready and the caller has permissions
   (routine; no plan_confirm in MCP-0..5).
5. Receives an MCP task or operation reference for install stages through
   finalizing to a **startable Stopped** instance.
6. Calls `instance_start` and polls instance report
   (Starting → Running) — first-start is **not** part of provisioning.execute.
7. Returns the final instance resource and truthful failure/rollback facts.

This is one user workflow even though the model performs multiple typed MCP
calls. Do not weaken immutable planning to force one network request.

### Automated Operations

The AI can create, dry-run, and enable deterministic policies. The daemon then
evaluates them using its dedicated service principal when no MCP client is
connected. The MCP plugin remains a management surface, not the policy runtime.

### Backup And Restore

- Backup runs as a long task with file/archive/checksum stages.
- A running instance requires the explicit maintenance flow.
- Restore always returns a destructive confirmation plan, operates only while
  stopped, exposes staging/validation progress, and reports rollback outcome.

## Error Model

Map `DaemonError` into MCP tool errors with stable code, kind, safe message,
correlation id, retryability, plan/operation/resource references, and optional
structured remediation. Do not expose stack traces, secrets, signing details,
private absolute paths, or raw third-party response bodies.

Domain errors return a completed MCP tool result with `isError=true`; reserve
MCP Task `failed`/JSON-RPC failure for malformed protocol or unexpected server
execution failure, matching official Tasks semantics.

## Serialization And Reflection Boundary

- Use explicit `JsonSerializerContext` metadata for config, task storage,
  tool arguments/results, resource payloads, plan confirmations, and audit
  projections.
- Use official SDK metadata hooks/typed tool registration; do not scan the
  plugin assembly for tools/resources.
- Register the curated inventory explicitly in DI.
- Keep daemon contract DTOs in MCSL packages; MCP-specific wire DTOs remain in
  the MCP project.
- Disable reflection serialization fallback in Release and published fixtures.

## Implementation Work Packages And PR Boundaries

Each package is independently reviewable and depends on the accepted prior
package. **MCP-0..5 require SDK Preview-1 only.** MCP-6..7 require SDK Preview-2.

### MCP-0: Dependency And Compatibility Spike (hard gate)

- Lock exact accepted MCSL SDK **Preview-1** and official MCP 1.4.1 packages.
- Compile one generated plugin module requesting only `system.query`,
  `network.http.listen`, and `auth.verify` (package id `mcp`).
- Prove plugin-private ASP.NET load in ALC, independent loopback Kestrel `/mcp`,
  stateless initialize, authenticated request context, Listen within host start
  timeout, clean shutdown, and `/api/v2` survival on bind failure.
- If this spike fails, stop MCP business work and revisit hosting strategy.
- Record experimental Tasks compiler opt-in and isolate it behind an adapter.

Suggested commit: `build(mcp): establish pinned daemon and protocol sdk baseline`

### MCP-1: Manifest, Cold Config, Private DI, And Host Lifecycle

- Final P1 manifest/config (`auth.allow_main_token`, no automation block).
- Plugin-private DI and independent Kestrel lifecycle via `network.http.listen`.
- Validate binding/canonical URI and startup/shutdown failure isolation.

Suggested commit: `feat(hosting): run stateless mcp over plugin kestrel`

### MCP-2: Bearer Authentication And ForPrincipal Facades

- Bearer handler using `auth.verify` + AllowMainToken option.
- Force ForPrincipal for tools; filter tools/list; execution rechecks.
- Document issuance via daemon `mcsl.auth.token.issue` (not MCP tools).

Suggested commit: `feat(auth): bind mcp requests to daemon principals`

### MCP-3: Read Resources And Tools (P1 inventory)

- daemon/instance/logs/operation resources and read tools only.
- Bounded pagination/time ranges and redaction.

Suggested commit: `feat(resources): expose bounded daemon operations state`

### MCP-4: Durable Tasks And Progress

- Singleton persistent task store under `storage.private` with principal ownership.
- Map daemon operations, cancellation, interrupted state, result, and progress
  resource links.
- Support both Tasks-capable and operation-reference clients.

Suggested commit: `feat(tasks): map daemon operations to durable mcp polling`

### MCP-5: Provisioning And Routine Lifecycle

- `provision_plan` / `plan_get` / `plan_execute` and lifecycle tools.
- Six automatic providers; execute to startable instance; start/stop poll new
  status machine; restart orchestration.
- Real download/install stages via operations (not first-start inside provision).

Suggested commit: `feat(provisioning): expose planned server creation workflows`

### MCP-6: Safety Plans, Files, Backup, Event Rules, And Automation

- Requires SDK Preview-2 features.
- Add plan_confirm and domain plan creators; file/backup/automation/audit tools.

Suggested commits:

- `feat(policy): enforce mcp risk and confirmation plans`
- `feat(operations): expose backup file and automation workflows`

### MCP-7: Packaging, Documentation, And Release Candidate

- Publish bundle with no shared MCSL contract copies.
- Provide `config.example.json`, main-token vs issued-token guide, reverse-proxy
  security requirements, tool/resource/permission reference, and upgrade notes.
- Run published-daemon E2E on supported RIDs.
- Record package/SBOM/license hashes and exact compatibility matrix.

Suggested commit: `release(mcp): prepare http plugin preview`

## Test Matrix

### Configuration And Lifecycle

- Missing config defaults, malformed/unknown fields, non-loopback opt-in,
  canonical URI mismatch, occupied port, invalid path, Kestrel start failure,
  activation ordering, startup timeout, shutdown deadline, and provider disposal.
- Prove config changes do not take effect until daemon/plugin cold restart.
- Prove daemon `/api/v2` remains available when MCP plugin startup fails.

### HTTP And MCP Protocol

- Use the official SDK client for initialize, tools/list, tools/call,
  resources/list/read, Tasks get/cancel, stateless concurrent requests, content
  negotiation, invalid methods, body/argument limits, and cancellation.
- No stdio entry point and no legacy standalone SSE route in bundle/docs.
- Each HTTP request works with a fresh stateless MCP server instance and shared
  singleton durable task store.

### Authentication And Authorization

- Missing/malformed/expired/wrong-issuer/wrong-audience tokens.
- Daemon main-token rejection and MCP-specific token success.
- Exact and wildcard permissions for every tool/resource.
- `tools/list` filtering plus execution-time recheck after token/policy change.
- Cross-principal task/plan/resource access rejection.
- Automation permission intersection and zero-permission default.
- No bearer value in logs, errors, storage, or audit.

### Safety

- Every risk class and default.
- Sensitive auto-execute off/on boundaries.
- Destructive action always requires confirmation.
- Plan hash/version/expiry/target drift/permission drift/identity drift.
- Confirmation replay, concurrent consume, daemon restart, and late execution.
- Arbitrary command/file/custom URL inputs never bypass planning/path/network
  validation.

### Tasks And Operations

- Durable create-before-return, singleton stateless visibility, concurrent poll,
  terminal idempotency, cancel race, interrupted restart, TTL/byte cleanup, and
  incomplete JSONL tail recovery.
- Determinate and indeterminate stage projection.
- Clients without Tasks receive usable operation references.
- Domain error completes with `isError=true`; protocol failure uses failed.

### Domain Workflows

- Natural-language-derived structured intent fixture to Vanilla/Paper/Fabric/
  Forge/NeoForge/Quilt provision plans and fake executions.
- Provider unresolved choice, EULA missing, checksum failure, mirror policy,
  cancel, rollback, first-start failure, and final success resource.
- Lifecycle state conflicts and graceful restart.
- Cold backup, maintenance backup, restore confirmation, checksum failure, and
  rollback.
- Bounded file operations and traversal/symlink rejection.
- Typed automation validate/test/apply/enable/trigger/audit with no MCP client.

### Published Host

- Publish the actual daemon version and exact SDK preview.
- Install the MCP sidecar under the daemon plugin directory with feature grants.
- Start daemon, wait for Kestrel, connect official MCP client over loopback,
  authenticate, read state, run a fake/isolated long operation, observe progress,
  cancel, and shut down.
- Assert no shared Daemon API/Common copies in bundle and no forbidden daemon,
  TouchSocket, MessagePipe, or Serilog references.

## Performance And Capacity Gates

> **Deferral (2026-07-25, user decision):** the `benchmarks/` project and the
> gates in this section are deferred to **MCP-7** (packaging/RC). MCP-0..5 is an
> internal implementation/test acceptance stage with no distribution, so
> baseline measurement is not a completion condition for it. The known hot path
> (poll-driven full-snapshot task persistence) was fixed with change-driven
> coalescing during MCP-0..5 review remediation. p50/p95/p99 and 64-client
> baselines must be measured when the benchmarks project lands in MCP-7; they
> remain mandatory before any release candidate.

- Benchmark authenticated stateless `tools/list`, one read tool, one resource,
  task polling, and permission denial.
- Define explicit p50/p95/p99 latency and allocation budgets after baseline
  measurement; do not invent an unmeasured concurrency claim.
- Run at least 64 concurrent clients polling separate operations and a shared
  operation; verify bounded memory and no cross-principal data.
- Verify task/progress persistence rate is bounded by coalescing rather than
  download callback count.
- Verify tools/resources are registered once and stateless requests do not
  rebuild immutable catalogs or source-generated metadata.

## Verification Commands

The new repository should provide equivalent commands:

```powershell
dotnet build MCServerLauncher.MCP.slnx /m:1
dotnet test tests/MCServerLauncher.MCP.Tests/MCServerLauncher.MCP.Tests.csproj -c Release /m:1
dotnet test tests/MCServerLauncher.MCP.IntegrationTests/MCServerLauncher.MCP.IntegrationTests.csproj -c Release /m:1
dotnet run --project benchmarks/MCServerLauncher.MCP.Benchmarks/MCServerLauncher.MCP.Benchmarks.csproj -c Release
dotnet publish src/MCServerLauncher.MCP/MCServerLauncher.MCP.csproj -c Release
```

The published-host gate additionally publishes the matching daemon from
MCSL Future and runs the plugin E2E fixture against that executable. Every
commit must also pass the MCSL SDK package compatibility/build fixture selected
by the dependency plan.

Finish every PR with `git diff --check`, package-content inspection, secret
search, and `git status --short --branch`.

## Release Acceptance Scenarios

1. A permitted caller asks its AI client to create and start a Paper instance.
   The model creates an immutable plan, executes it, observes real download and
   install stages, and receives the running instance resource.
2. A read-only token sees only read tools/resources and receives permission
   denial if it invokes a cached mutating tool name directly.
3. A daemon restart during Forge installation produces an `interrupted`
   operation/task result, never a false resume or success.
4. A destructive removal/restore cannot execute without a current, matching,
   single-use confirmation.
5. A typed crash-loop policy continues applying bounded backoff with its service
   principal after every MCP client disconnects.
6. A plugin Kestrel bind failure leaves the daemon and `/api/v2` healthy.
7. Changing MCP `config.json` has no effect until cold restart.

## Exit Criteria

- The plugin is independently versioned/published and depends on exact accepted
  MCSL SDK packages (Preview-1 for MCP-0..5, Preview-2 for MCP-6..7).
- Package id is `mcp`; no `rpc.register` / `event.*` in first release.
- It exposes only stateless Streamable HTTP at the configured `/mcp` route.
- It starts/stops an independent Kestrel listener through isolated private DI
  and private ASP.NET load; MCP-0 hard spike is green.
- Every tool request uses ForPrincipal after `auth.verify` (including optional
  main-token superuser path) and rechecks method-name permissions.
- MCP-0..5 tool/resource inventory matches the decision freeze; full inventory
  completes in MCP-6+.
- Long provisioning operations expose truthful daemon stages/progress through
  Tasks and operation resources; lifecycle uses status polling.
- Routine provisioning needs no plan_confirm; destructive confirmations ship in
  MCP-6+ via daemon plan kernel.
- Config is startup-only, contains no secrets, and defaults
  `auth.allow_main_token=true`.
- Published-host, protocol, auth, safety, storage, concurrency, performance, and
  packaging gates pass with documented evidence.
- Decision freeze doc remains the conflict authority until this plan is fully
  normalized to it.

## Explicitly Deferred

- stdio transport and bridge executables.
- Legacy standalone SSE transport.
- Embedded LLM/provider keys or server-side natural-language parsing.
- Automatic export of daemon RPCs.
- Built-in OAuth 2.1 authorization server.
- Public-Internet deployment without an external security layer.
- Server-push task progress.
- Online hot backups.
- Arbitrary scripts, shell, dynamic C#, or unrestricted console automation.
- Runtime plugin install/reload/unload.
