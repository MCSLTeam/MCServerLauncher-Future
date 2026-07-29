# Plugin SDK 2.0 Feature-Gated Application Expansion Plan

## Status And Baseline

- Planning date: 2026-07-19.
- **Decision freeze:** 2026-07-20. Authoritative overrides live in
  `docs/spec/2026-07-20-plugin-sdk-mcp-decisions.md`.
  When this plan and that spec disagree, **the spec wins**.
- Delivery is staged as **Preview-1** (MCP main path) then **Preview-2**
  (backup/monitoring/automation/audit and remaining features). Full end-state
  scope of this plan remains mandatory; previews only stage merge order.
- PR #52, `feat(daemon): complete typed v2 cutover and plugin host`, was merged
  into `master` as `b4753c38766660d68f96a61cad2185482b4410a2` on 2026-07-19.
- The verified planning baseline is `master`/`origin/master` at
  `69b59bbd63153ea48fce4ea7d519135c708b722c`.
- This work starts after #52. It must not be appended to, rebased into, or
  described as part of #52.
- The independently delivered HTTP MCP plugin is specified by
  `2026-07-19-http-mcp-plugin-plan.md`. MCP-0..5 may begin only after this plan
  publishes an exact **Preview-1** `MCServerLauncher.Daemon.Plugin.Sdk`
  `2.0.0-preview.N` package (not after the entire SDK-1..9 program).

## Goal

Turn the startup-only trusted plugin boundary merged in #52 into a
Cargo-inspired, feature-gated developer SDK that exposes approved
`IDaemonApplication` interfaces directly, enforces individual caller
permissions on every application method, and supplies the daemon-owned
long-running operation, provisioning, cold-backup, monitoring, automation,
and audit services required by an MCP operations plugin.

The final developer experience must be explicit at compile time:

- `mcsl-plugin.json` declares required `feature` values.
- An incremental source generator reads that manifest as an `AdditionalFile`.
- Generated plugin context types contain only interfaces selected by those
  features.
- Generated adapters build an isolated plugin-private DI container.
- Runtime admission independently validates and grants the same features.
- Per-request authorized proxies enforce the verified caller's individual
  method permissions before delegating directly to the application core.

The daemon remains a trusted in-process plugin host, not a malicious-code
sandbox. Feature admission is a contract, audit, least-privilege, and developer
correctness boundary; it cannot prevent arbitrary native or BCL calls by a
hostile assembly running in the daemon process.

## Touched Areas

- `docs`
- `backend`
- `protocol`
- `serialization`
- `installer`
- `storage`
- `tests`
- `benchmarks`
- `workflow`
- `integrations`

## Relationship To Other Work

- #52 is the accepted V2/application-core/startup-plugin foundation.
- This plan is a new Plugin SDK 2.0 and application-domain expansion.
- The mandatory plugin Contracts/dependency-DAG follow-up remains a separate
  plan (Phase 7). This plan does not add plugin dependencies, shared provider
  contracts, plugin-to-plugin service discovery, or RPC fallback between plugins.
- **SDK locks the `mcsl-plugin.json` root grammar first.** Phase 7 may only add
  a versioned dependency section without renaming the package, entry, API, or
  feature fields established here. `event.subscribe` remains Phase 7 owned.
- The MCP plugin is an independent delivery unit and consumes only a published,
  exact `MCServerLauncher.Daemon.Plugin.Sdk` **Preview-1** version for MCP-0..5.

## Fixed Decisions

1. Replace every public plugin `capability` name with `feature`.
2. Replace `plugin.json` with the single Cargo-inspired
   `mcsl-plugin.json` manifest.
3. Do not provide a dual reader, alias, obsolete enum, or compatibility switch.
   The manifest/API migration is release-atomic and requires a Plugin API major
   version bump.
4. The manifest contains identity, entry, API requirement, and required
   features only. It has no `configuration` field.
5. A same-directory optional `config.json` is read once during plugin startup.
   There is no watcher, reload, writeback, or runtime mutation.
6. Configuration read access is a base plugin API. It is not a feature.
7. `storage.private` is a distinct, write-capable feature.
8. Plugins call approved application interfaces directly in process. They do
   not call the daemon through loopback RPC.
9. Plugins never receive daemon root DI, `IServiceProvider`, manager/storage
   implementations, TouchSocket, MessagePipe, Serilog, mutable runtime
   collections, or disposable daemon handles.
10. The generated adapter creates a plugin-private DI container containing only
    base services, generated feature facades, and plugin-owned registrations.
11. The daemon creates one DI scope per long-running operation. Stable executor
    dependencies use constructor injection; the operation context is an
    explicit method parameter.
12. `IOperationContext` owns progress reporting only. The coordinator alone can
    set terminal status.
13. Automatic provider resolution belongs to daemon provisioning, not the MCP
    plugin. Official sources are preferred and existing daemon mirror policy is
    reused explicitly.
14. First-release automatic providers are Vanilla, Paper, Fabric, Forge,
    NeoForge, and Quilt. Other types require explicit local file, URL, archive,
    script, or executable sources.
15. First-release backup/restore is cold only. Online save coordination is
    deferred.
16. Monitoring and typed automation continue independently of MCP client
    connections. They are daemon application domains.
17. Shared persistence uses source-generated `System.Text.Json`, immutable JSON
    snapshots, and bounded rolling JSONL. Do not add SQLite or a native database.
18. **Staging (2026-07-20):** Preview-1 implements the MCP main-path feature set
    in the decision spec; Preview-2 completes remaining domains. Full scope of
    this plan remains mandatory.
19. **Authorization (2026-07-20):** Permission name equals method name. Official
    methods use `mcsl.*`; plugins use `plugin.<package.id>.*`. Enforce via
    generated proxies holding `ICallerContext` (Host vs ForPrincipal). Do **not**
    implement a Requires permission graph, login prebake, or callee inference.
    Use deterministic segment wildcards and V2 connection-level permission cache.
20. **Admission (2026-07-20):** `grant_level` (default Medium) plus optional
    Custom `feature_grants`, per-plugin `plugin_grants` / `admissions`, silent
    load within ceiling, TTY Approve Permanent outside ceiling. FeatureCatalog
    owns summary, risk, and host method expansion.
21. **Plans (2026-07-20):** Generic plan kernel in Preview-1; provisioning is
    the first adapter. Routine provisioning executes with permissions only;
    destructive confirm consumers arrive in Preview-2.
22. **Lifecycle status (2026-07-20):** Extend instance status with Starting/
    Stopping (and Crashed when detectable). Start/Stop return after entering
    intermediate states; only `provisioning.execute` creates Operations in P1.
23. **Developer package:** plugins reference only
    `MCServerLauncher.Daemon.Plugin.Sdk`. Modules use ConfigureServices/Start/
    Stop; generated adapter implements `IDaemonPlugin`.

## Clean-End-State Project Layout

```text
src/MCServerLauncher.Common/
  Contracts/{Operations,Provisioning,Backups,Monitoring,Automation,Audit}/
  Contracts/Serialization/

src/MCServerLauncher.Daemon.API/
  Application/
  Authorization/
  Plugins/
    mcsl-plugin.schema.json

generators/MCServerLauncher.Daemon.Plugin.Generators/
  Manifest/
  Features/
  Authorization/
  Diagnostics/

src/MCServerLauncher.Daemon.Plugin.Sdk/
  buildTransitive/
  README.md

src/MCServerLauncher.Daemon/Application/
  Operations/
  Provisioning/
  Backups/
  Monitoring/
  Automation/
  Audit/
  Authorization/

src/MCServerLauncher.Daemon/Plugins/
  Configuration/
  Features/
  Storage/
  Authentication/
  Network/

tests/MCServerLauncher.Daemon.ApiTests/{Generators,Authorization,Operations}/
tests/MCServerLauncher.ProtocolTests/{Application,Provisioning,Backups,Monitoring,Automation}/
tests/MCServerLauncher.PluginIntegrationTests/Fixtures/
```

Names may be consolidated into existing folders when local ownership is
clear, but the Common/API/daemon/generator/SDK boundaries above are mandatory.

## Manifest 2.0

### Canonical Shape

The source generator and runtime host consume the same strict schema:

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
      "instance.query",
      "system.query"
    ]
  }
}
```

Official sample MCP package id is `mcp` (permission/RPC prefix `plugin.mcp.*`).
Do not use `mcsl.mcp` as package id.

Rules:

- All properties shown above are required except `$schema`.
- Unknown properties, unknown features, duplicate features, invalid package
  identifiers, invalid NuGet versions/ranges, absolute entry paths, and entry
  types outside the plugin assembly are errors.
- `requires.features` is a sorted set semantically. The build emits a
  diagnostic if checked-in ordering is non-deterministic.
- Every requested feature is required in this milestone. Optional features are
  deferred until a real consumer requires graceful degradation.
- The build copies the exact validated manifest into the publish bundle as
  `mcsl-plugin.json`.
- Generator output embeds a normalized manifest digest. Runtime admission
  rejects a bundle when the on-disk manifest and generated digest disagree.
- Runtime API range checking reuses the daemon's `NuGet.Versioning` dependency.

### Release-Atomic Migration

- Delete `PluginCapability`, `Capabilities`, `HasCapability`, capability
  error codes, and capability documentation.
- Add `PluginFeature`, `Features`, `HasFeature`, and feature diagnostics.
- Delete `plugin.json` parsing and fixtures in the same release.
- Migrate the health/error/throwing published-host fixtures before bumping the
  host version.
- Set the host Plugin API version to `2.0.0` and the Daemon API/Plugin SDK
  package line to `2.0.0-preview.N` until full acceptance passes.
- Do not silently accept a 1.x manifest in a 2.x host.

## Complete Plugin SDK Expansion Scope

### Base Services Available To Every Admitted Plugin

| Service | Contract | Boundary |
|---|---|---|
| Identity | `PluginIdentity` | Immutable package id/version from the admitted manifest. |
| Logging | `ILogger` | Category scoped to the plugin id; no Serilog types. |
| Errors | `IPluginErrorFactory` | Produces plugin-owned structured errors. |
| Configuration | `IPluginConfiguration` | Reads optional plugin-local `config.json` once using explicit `JsonTypeInfo<T>`. No path, watcher, `IConfiguration`, or `IOptions`. |
| Activation | `Task Activation` | Completes only after catalog commit and host activation. |
| Lifetime | `CancellationToken LifetimeToken` | Cancelled during reverse-order shutdown. |
| Private DI bootstrap | generated adapter | Builds and disposes a plugin-private provider; never exposes daemon root DI. |

Missing `config.json` is distinguishable from invalid JSON. The API never
returns its resolved path.

### Declared Features

| Feature | Generated surface | Runtime owner and purpose |
|---|---|---|
| `rpc.register` | `IPluginRpcRegistrar` | Existing typed RPC registration. |
| `event.publish` | `IPluginEventRegistrar` and typed publishers | Existing typed plugin event publication. |
| `event.subscribe` | `IPluginEventSubscriber` | Typed application-event subscription with owner cleanup. |
| `instance.query` | `IInstanceQueryApplication` | Catalog, report, log, and settings reads. |
| `instance.manage` | `IInstanceManagementApplication` | Create, lifecycle, command, removal, and settings mutation. |
| `file.read` | `IFileReadApplication` | Contained metadata and bounded reads/downloads. |
| `file.write` | `IFileWriteApplication` | Contained create/upload/move/copy/rename/delete. |
| `system.query` | `ISystemQueryApplication` | System facts and Java discovery. |
| `event-rule.manage` | `IEventRuleApplication` | Rule read, validate, test, and update. |
| `operation.query` | `IOperationQueryApplication` | List/read immutable operation snapshots. |
| `operation.cancel` | `IOperationControlApplication` | Cooperative cancellation requests. |
| `provisioning.manage` | `IProvisioningApplication` | Resolve and execute immutable provisioning plans. |
| `backup.manage` | `IBackupApplication` | List/create/prune cold backups and restore plans. |
| `monitoring.query` | `IMonitoringApplication` | Current/retained metrics and significant events. |
| `automation.manage` | `IAutomationApplication` | Validate/test/apply/enable typed policies. |
| `audit.query` | `IAuditApplication` | Query bounded structured audit records. |
| `storage.private` | `IPluginPrivateStorage` | Validated plugin-private snapshot/JSONL storage with quota. |
| `network.http.listen` | `IPluginHttpEndpointPolicy` | Validates/reports HTTP bindings; not an OS sandbox. |
| `auth.verify` | `IPluginAuthentication` | Verifies audience-bound daemon tokens into principals. |

`event.subscribe` is reserved in the common feature vocabulary but remains
owned and implemented by the separate mandatory Phase 7
Contracts/dependency-DAG plan. It is not implemented by the SDK work packages
below and is not an MCP prerequisite. This plan must consume the Phase 7
surface if Phase 7 lands first, rather than creating a competing subscriber
contract.

No generated type contains a nullable service bag. A plugin declaring only
`instance.query` cannot compile references to management, files, operations,
provisioning, or authentication.

## Incremental Source Generator

Create `generators/MCServerLauncher.Daemon.Plugin.Generators` as a new project.
It is unrelated to and must not reuse the deleted V1 action generator.

### Inputs And Output

Inputs are exactly one manifest `AdditionalFile`, one annotated partial plugin
module, the exact referenced Daemon API permission metadata, and MSBuild
package identity/version properties.

For a module named `McslMcpPlugin`, generate:

- `McslMcpPluginFeatures`, containing only declared feature entry points;
- `McslMcpPluginAuthorizedFeatures`, containing only declared application
  views bound to a `VerifiedPrincipal`;
- `McslMcpPluginServiceRegistration`, registering base/selected facades into
  the plugin-private `IServiceCollection`;
- `DaemonPluginAdapter`, the explicit `IDaemonPlugin` implementation named by
  the manifest;
- `McslMcpPluginMetadata`, containing normalized identity, API range, feature
  set, and manifest digest;
- permission proxy metadata for every selected application method.

The adapter validates the digest, builds the private provider, resolves the
user module, forwards lifecycle calls, and disposes on every failure/shutdown
path.

The SDK package may depend on
`Microsoft.Extensions.DependencyInjection.Abstractions`; the Daemon API public
surface remains free of `IServiceProvider` and container types.

### Required Diagnostics

At minimum diagnose missing/multiple manifests, malformed JSON/schema,
unknown/duplicate/conflicting/unsorted features, entry mismatch, manifest and
MSBuild identity/version drift, unsupported API range, missing/multiple module
types, manual `IDaemonPlugin` implementation, raw host-context use,
undeclared services, missing explicit JSON metadata, malformed/duplicate
permission metadata, and application/RPC permission disagreement.

Generator output must be deterministic, incremental, nullable-warning clean,
release tracked, and covered by Roslyn snapshots plus external compile fixtures.

## Runtime Feature Admission And Reporting

The daemon does not trust generated output alone.

1. Parse and validate `mcsl-plugin.json` with explicit source-generated JSON
   metadata and `UnmappedMemberHandling.Disallow`.
2. Load entry assembly metadata without executing plugin code.
3. Compare manifest identity, API range, feature set, and digest with generated
   metadata.
4. Apply daemon administrator feature policy by plugin id.
5. Produce immutable requested/granted/denied feature sets for startup logs,
   diagnostics, and daemon status.
6. Skip the plugin atomically if any required feature is denied or unsupported.
7. Construct only granted host facades.
8. Preserve #52 transactional configure/start/commit/activate and reverse-stop
   cleanup.

Admission uses daemon `config.json` `plugins.grant_level` (default Medium),
Custom `feature_grants`, `plugin_grants`, and `admissions` as specified in the
decision spec. Features within the ceiling load silently; outside ceiling uses
TTY Approve/Approve Permanent or no-TTY skip. Never infer grants from assembly
references. FeatureCatalog supplies risk text for preflight display.

## Plugin-Private DI

- The generated parameterless adapter remains compatible with startup discovery.
- During `Configure`, the adapter creates one private `ServiceCollection`.
- Generated code adds immutable base services and granted feature facades.
- The module can register plugin-owned services, including Kestrel/MCP services.
- The provider is built once after registrations close and is disposed on
  configure/start failure or reverse shutdown.
- Daemon services appear only as generated proxy/facade instances.
- A plugin may use `IServiceProvider` internally for its own provider, but it
  is never a daemon service locator or generated feature.
- Published-host tests prove undeclared daemon interfaces cannot be resolved.

## Method-Level Authorization Model

> **Authoritative detail:** `docs/spec/2026-07-20-plugin-sdk-mcp-decisions.md` §5.
> The 2026-07-20 freeze **removes** permission-graph Requires edges, login
> prebake, and callee inference. Implement CallerContext proxies instead.

There are three independent gates:

1. **Plugin feature:** which interface groups the plugin can obtain (admission +
   Host principal method expansion from FeatureCatalog).
2. **Caller permission:** method name == permission name on every application
   proxy invocation (`ICallerContext`).
3. **Consumer policy:** whether MCP enables, plans, confirms, or automatically
   executes a tool (MCP-side; Preview-2 for destructive confirm).

### Permission Metadata And Enforcement

- Permission string **is** the RPC/application method name.
- Official methods: `mcsl.*`. Plugin methods: `plugin.<package.id>.*` with SG
  prefix completion from relative developer names.
- Migrate legacy file permission strings (`mcsl.daemon.file.*`) to method names.
- Replace `"*"` placeholders on built-in instance/system/event-rule descriptors.
- Generator emits authorized proxies; proxy closures hold `ICallerContext`.
- Host principal = union of methods from granted features; user path uses
  `ForPrincipal` and rechecks leaves. MCP tools **must** use ForPrincipal.
- Built-in RPC descriptors and application methods share exact permission
  equality (method name).
- Deterministic segment wildcards (`*`, `**`); V2 connection-level allow/deny
  cache; MCP per-request evaluation.
- Do **not** implement Requires graphs, login prebake, or SG call-graph
  authorization inference.

### Initial Method Permission Inventory (Preview-1 core)

```text
mcsl.instance.catalog.get
mcsl.instance.create
mcsl.instance.start
mcsl.instance.stop
mcsl.instance.halt
mcsl.instance.remove
mcsl.instance.command.send
mcsl.instance.report.get
mcsl.instance.report.list
mcsl.instance.log.get
mcsl.instance.settings.get
mcsl.instance.settings.update

mcsl.directory.* / mcsl.file.*   # existing file RPCs; permission = method name

mcsl.system.info.get
mcsl.java.list
mcsl.instance.event-rules.get
mcsl.instance.event-rules.update

mcsl.operation.list
mcsl.operation.get
mcsl.operation.cancel
mcsl.provisioning.resolve
mcsl.provisioning.get
mcsl.provisioning.execute

mcsl.auth.token.issue            # main-token only; not a plugin feature
```

Preview-2 adds backup/monitoring/automation/audit method names under the same
method=permission rule. See decision spec FeatureCatalog tables for Host
expansion.

### Principals And Authorized Access

`IPluginAuthentication.VerifyAsync(token, expectedAudience, options)` returns an
immutable `VerifiedPrincipal` (subject, token id, issuer, audience, expiry,
permissions). Options include `AllowMainToken` for MCP local convenience.

- Main token on `/api/v2`: always full `*`.
- Main token on MCP: only when plugin config `auth.allow_main_token` is true
  (default true), via verify options → `sub=daemon-main`, `perms=*`.
- JWT: `aud` resource binding; issue via `mcsl.auth.token.issue` (**main token
  only** + `security.allow_main_token_issue`); not exposed as an MCP tool.
- Generated `ForPrincipal(principal)` binds user context to proxies. Host
  facades use `plugin:<id>` context from feature→methods expansion.
- Background automation (Preview-2) uses a dedicated service principal from a
  cold-start allowlist; never inherits main token or interactive request token.

## `IDaemonApplication` Expansion

Keep `IDaemonApplication` as the complete transport-neutral aggregate and
introduce narrow views used by generated features:

```text
IDaemonApplication
  Instances    -> IInstanceApplication
  Files        -> IFileApplication
  System       -> ISystemApplication
  EventRules   -> IEventRuleApplication
  Operations   -> IOperationApplication
  Provisioning -> IProvisioningApplication
  Backups      -> IBackupApplication
  Monitoring   -> IMonitoringApplication
  Automation   -> IAutomationApplication
  Audit        -> IAuditApplication
```

`IInstanceApplication` composes query/management views,
`IFileApplication` composes read/write views, and
`IOperationApplication` composes query/control views. Generated plugin
contexts expose only selected narrow views and never the aggregate root.

All operator-domain methods remain transport neutral. Add matching typed V2
descriptors, daemon-client implementations, explicit `JsonTypeInfo`, OpenRPC,
and protocol tests for operation/provisioning/backup/monitoring/automation/audit.
Plugin-host configuration/storage/network/token-verification contracts are not
remotely invocable RPC methods.

## Daemon-Wide Operation Model

### Public Contracts

Define immutable source-generated contracts for operation id/idempotency key,
kind/target, status, stage, progress, monotonic version, timestamps,
cancellability, structured result reference, and error projection.

Statuses are exactly `queued`, `running`, `succeeded`, `failed`,
`cancelled`, and `interrupted`. Progress is determinate or indeterminate and
can include completed/total work, unit, transferred/total bytes, and rate.

`IOperationQueryApplication` lists and reads retained snapshots.
`IOperationControlApplication` requests cooperative cancellation and has a
separate permission.

### Dependency-Inverted Execution

```csharp
Task<Result<TResult, DaemonError>> ExecuteAsync(
    TPlan plan,
    IOperationContext operation,
    CancellationToken cancellationToken);
```

`IOperationContext` provides only immutable id, `SetStage`, thread-safe
non-blocking `ReportProgress`, and weighted child-step creation.

The coordinator:

- creates one DI scope per operation;
- resolves executors and stable dependencies from that scope;
- passes context explicitly instead of using `AsyncLocal` or an accessor;
- owns status, persistence, cancellation, and result transitions;
- supplies a shared no-op context for intentional non-operation calls;
- coalesces high-frequency callbacks in memory and persists at a bounded rate
  plus stage/terminal transitions;
- prevents late progress/cancel from overwriting terminal state;
- marks nonterminal work `interrupted` after restart and never claims to resume
  process-local execution.

Adapt every Downloader core/library fetch, Forge-family dependency download,
checksum, extraction, installation, config write, and first-start step. An
outer core-download percentage alone is insufficient.

## Provisioning Application

`IProvisioningApplication` has two phases:

1. Resolve structured intent into a validated immutable plan.
2. Execute that exact plan as a daemon operation.

The plan records id/hash/version/expiry/idempotency, provider and requested
versions, final source URL, checksum and source, Java decision, JVM settings,
paths/conflicts/disk, EULA, source type, rollback, weighted steps, and required
confirmation.

Execution revalidates hash, expiry, provider metadata version, target state,
filesystem conflicts, disk, Java, and permissions before mutation. It never
silently re-resolves a different source.

Provider rules:

- daemon-owned adapters for Vanilla, Paper, Fabric, Forge, NeoForge, Quilt;
- prefer official upstream metadata/source;
- reuse existing daemon mirror settings explicitly when allowed;
- record source/checksum in plan and audit;
- reuse cache, factory, installer, EULA, path, and transaction behavior;
- never let MCP synthesize automatic-provider URLs;
- arbitrary URLs require explicit custom-source mode, SSRF/network checks,
  checksum policy, separate permission, and confirmation;
- other instance types accept explicit local file, URL, archive, script, or
  executable sources only.

## Cold Backup And Restore

Add daemon-owned `IBackupApplication` and storage outside plugin-private data.

- Direct backup accepts stopped instances only.
- Maintenance backup is graceful stop, backup, restart as weighted child steps.
- Each archive manifest contains backup/instance/config versions, file list,
  sizes, timestamp, compression metadata, and SHA-256.
- Path validation rejects traversal, symlink escape, backup-root escape, and
  restore outside the instance.
- Retention enforces age/count/bytes and preserves archives referenced by active
  restore plans.
- Restore accepts stopped instances only, validates checksum/current version,
  stages beside the target, and uses atomic replacement with rollback.
- Restore always uses immutable plan plus two-phase confirmation.
- Online hot backup and server-specific save coordination are deferred.

## Monitoring, Automation, And Audit

### Monitoring

- Sample system and running-instance metrics every 15 seconds by default.
- Retain seven days under a shared 256 MiB default cap.
- Persist immutable current snapshots plus bounded rolling JSONL history.
- Store CPU, memory, disk, process responsiveness, lifecycle, and significant
  events; do not duplicate full console logs.
- Bound range point counts and downsample deterministically.
- Sampling failure records a structured gap and never fabricates zero.

### Typed Automation

The daemon evaluates policies independently of MCP connectivity. Initial
triggers cover crash loop, unexpected exit, sustained memory, disk,
unresponsive instance, maintenance window, duration, and cooldown. Initial
actions cover backoff restart, graceful stop, maintenance state, restart
suppression, notification, diagnostic capture, audit, and creation of a
confirmation plan.

#### Preview-2 delivered scope (amendment, 2026-07-29)

Preview-2 ships a subset of the monitoring metrics and trigger/action vocabulary
above. Recorded here so the delivered state is not read as the planned one:

| Area | Preview-2 | Deferred |
|---|---|---|
| Metrics | system CPU, memory used/total, per-instance status | disk, responsiveness, significant events |
| Triggers | crash loop, unexpected exit, sustained metric, maintenance window | unresponsive, disk, duration |
| Actions | backoff restart, graceful stop, notification, confirmation plan | maintenance state, restart suppression, diagnostic capture, explicit audit |

Cooldown is delivered as a policy-level guard rather than a distinct trigger
kind. Each deferred item widens a closed union and therefore needs its own
`JsonTypeInfo`, converter discriminator, daemon/RPC/DaemonClient parity,
validation and protocol tests; they are tracked as follow-up issues with
acceptance criteria.

"Sampling failure records a structured gap and never fabricates zero" is a
Preview-2 requirement, not a deferred one — it is load-bearing for sustained
triggers, which must not fire on evidence that was never collected.

Policies support validation, dry run against recorded facts, apply, enable,
disable, version conflicts, cooldowns, execution caps, and explicit service
principal permissions. Shell, arbitrary scripts, dynamic C#, unrestricted
background console commands, and daemon-side model calls are forbidden.

### Audit

Record principal, plugin, tool/RPC/application method, permission, target,
plan id/hash, operation id, result/error, confirmation identity, and timestamps.
Redact bearer tokens, secrets, sensitive console content, and resolved private
roots. Queries are bounded and permission checked.

## Persistence And Retention

- Use atomic JSON snapshots for indexes/current state and bounded rolling JSONL
  for history.
- Daemon operations, plans, backups, metrics, policies, and audit use
  daemon-owned validated roots; only plugin-owned state uses private storage.
- Every file has explicit source-generated `JsonTypeInfo`; no reflection or
  assembly scanning.
- Validate length, count, total bytes, containment, and checksums.
- Crash recovery truncates an incomplete final JSONL record only.
- Retention is observable/audited without recursively flooding audit.

## Implementation Work Packages And PR Boundaries

Each package is an independently reviewable PR based on the accepted preceding
package. Do not combine the entire program into one change.

**Staging map (authoritative):** see decision spec §10.
Preview-1 = SDK-1..6 + SDK-5b + SDK-9a (MCP main path).
Preview-2 = SDK-7..8 + remote parity follow-ups + SDK-9b.

### SDK-1: Manifest 2.0, FeatureCatalog, Admission, Config

- Add strict schema/model and FeatureCatalog (summary, risk, methods).
- Atomically migrate host, fixtures, docs, tests, and bundles from
  `plugin.json`/capability to `mcsl-plugin.json`/feature.
- Implement grant_level / plugin_grants / admissions / preflight + config.json
  `security`/`plugins` sections (decision spec §4).
- Bump Plugin API/package major version.

Suggested commit: `feat(plugins): adopt feature-gated manifest v2`

### SDK-2: Generator And Developer SDK Package

- Create incremental generator and `MCServerLauncher.Daemon.Plugin.Sdk`.
- Generate digest, typed feature contexts, adapter, DI registration, permission
  metadata, and diagnostics.
- Module API: ConfigureServices/Start/Stop; forbid hand-written IDaemonPlugin.
- Add compile-success fixture per Preview-1 feature and undeclared-feature failures.
- Pack analyzer/buildTransitive assets and pin ABI dependencies exactly.

Suggested commits:

- `feat(plugin-sdk): generate manifest-gated feature contexts`
- `build(plugin-sdk): package generator and bundle targets`

### SDK-3: Runtime Facades, Config, Storage, Network, And Private DI

- Replace raw developer context use with generated adapters.
- Add cold plugin `config.json`, private storage quota API, HTTP endpoint policy
  (validate/register/IP:port exclusive), ALC share of DI.Abstractions, and
  private provider lifecycle.
- Preserve transactional admission and reverse cleanup; start_timeout from config
  (default 30s).

Suggested commit: `feat(plugins): isolate generated plugin service providers`

### SDK-4: CallerContext Permissions And Token Surfaces

- Method name = permission; segment wildcards; generated proxies + Host/
  ForPrincipal; migrate file permission strings; remove `"*"` placeholders.
- Audience-bound JWT (sub/aud), main-token issue RPC, auth.verify options
  including AllowMainToken recognition for callers that pass it.
- V2 connection permission cache; no Requires graph / prebake.

Suggested commits:

- `feat(auth): enforce caller-context method permissions`
- `feat(plugin-sdk): generate authorized feature facades`

### SDK-5: Operation Core And Progress Injection

- Add contracts, coordinator, store, operation DI scopes, explicit context,
  cancellation, recovery, and application methods
  `mcsl.operation.list|get|cancel`.
- Instrument core/Forge-family downloads and installers with weighted progress.
- Add no-op context for non-operation internal paths.

Suggested commits:

- `feat(operations): add persistent daemon task coordination`
- `feat(installer): report weighted provisioning progress`

### SDK-5b: Instance Status Machine And Lifecycle Observer

- Extend status enum; Starting on spawn for all types; Minecraft ready observer;
  Stopping/Crashed rules per decision spec §6.
- Start/Stop return on intermediate transition; WPF minimum compatibility;
  protocol/DTO/event tests.

Suggested commit: `feat(instances): expose starting and stopping lifecycle states`

### SDK-6: Provisioning Providers And Plan Kernel

- Generic plan kernel (metadata, expiry, CAS, blocked+ready persistence).
- `mcsl.provisioning.resolve|get|execute`; six automatic providers; dual-path
  keep CreateInstanceAsync; execute to startable Stopped only.
- Idempotency key + single-flight execute; routine risk → no confirm.

Suggested commit: `feat(provisioning): add provider-backed instance plans`

### SDK-7: Cold Backup And Restore (Preview-2)

- Add manifests, archive store, maintenance flow, retention, restore planning,
  checksum, staging, atomic replacement, and rollback.

Suggested commit: `feat(backups): add cold backup and confirmed restore`

### SDK-8: Monitoring, Typed Automation, And Audit (Preview-2)

- Add sampling/history, deterministic policies, dedicated service principal,
  bounded audit, and corresponding application/RPC/client surfaces.

Suggested commits:

- `feat(monitoring): retain bounded daemon metrics`
- `feat(automation): execute typed operational policies`
- `feat(audit): record authorized daemon mutations`

### SDK-9a: Preview-1 Package Gate (MCP readiness)

- Publish exact `2.0.0-preview.N` Daemon API/Plugin SDK pair for Preview-1
  surface; published-host fixture; record hashes for MCP-0..5.

Suggested commit: `release(plugin-sdk): prepare feature sdk preview.1`

### SDK-9b: Preview-2 Package Gate

- Complete remaining domains, remote parity as scheduled, second exact pin.

Suggested commit: `release(plugin-sdk): prepare feature sdk preview.2`

## Test Matrix

### Contract And Generator

- Public surface and forbidden dependency traversal.
- Exact NuGet closure and analyzer/buildTransitive contents.
- Manifest schema, normalization, digest, API range, identity, ordering, and
  every feature diagnostic.
- Incremental determinism and diagnostic release tracking.
- Compile-success fixture for every feature.
- Compile failures for undeclared services, raw host context, manual adapter,
  missing JSON metadata, and daemon internal references.

### Runtime Feature And DI Lifecycle

- Requested/granted/denied matrix and atomic skip.
- Generated/runtime manifest mismatch.
- Private DI disposal on configure error, start error/timeout, activation
  conflict, and shutdown.
- No undeclared feature resolvable from the private provider.
- Published single-file daemon plus sidecar generated plugin on supported RIDs.

### Authorization

- Exact method/descriptor permission agreement.
- Exact, `*`, and `**` segment wildcard cases; malformed names; prefix attacks;
  empty permissions; canonicalization.
- Audience, issuer, expiry, token id, service principal, main-token rejection,
  and secret-free logs.
- Recheck after plan creation and immediately before execution.

### Operations

- Monotonic versions under concurrent reporters/readers.
- Weighted progress, indeterminate stages, coalescing, late reports, domain
  failure, exception, cancellation race, terminal idempotency.
- Restart maps queued/running work to `interrupted`.
- One scope per operation and no context bleed.
- Downloader and Forge multi-download progress integration.

### Provisioning

- Provider metadata fixtures for all six automatic providers.
- Official/mirror policy, exact source retention, checksum, cache corruption,
  Java, disk, EULA, path conflicts, idempotency, cancellation, rollback, and
  first-start failure.
- Custom URL SSRF/path/checksum/confirmation policy.

### Backup

- Running-instance rejection and maintenance workflow.
- Traversal, symlink escape, hash mismatch, disk shortage, version conflict,
  retention race, restore rollback, confirmation replay, cancel, and restart.

### Monitoring, Automation, And Audit

- Fake-time sampling, gaps, downsampling, age/byte retention, JSONL tail
  recovery, and cap enforcement.
- Every trigger/action, cooldown, backoff, crash-loop suppression, dry run,
  version conflict, disabled policy, and insufficient service permission.
- Audit completeness, bounds, redaction, and no recursive retention flood.

## Performance Gates

- Benchmark authorized proxy overhead against direct application calls and set
  an explicit reviewed latency/allocation threshold.
- `ReportProgress` remains non-blocking and bounded for at least 100,000
  high-frequency updates; persistent writes follow coalescing, not callback
  count.
- Monitoring 100 running instances at 15-second cadence remains within a
  reviewed CPU/allocation/storage budget.
- Existing published-state 0 B/op contracts remain intact.
- Protocol-sensitive changes update an existing benchmark or document why its
  current gate fully covers the path.

### Deferral for Preview-2 internal acceptance (amendment, 2026-07-29)

The first and third gates above — authorized proxy overhead, and monitoring 100
instances at the 15-second cadence — are **not implemented**. Preview-2 closes
as an internal implementation and test acceptance state without them.

This deferral is recorded here rather than only in the tracking issue, because
an issue does not amend an acceptance contract. The gates are stated in this
plan, so this plan is where they can be relaxed; leaving them written as binding
while treating them as waived is the same "decision living outside the
authoritative document" that moving these files under version control was meant
to end.

Scope of the deferral:

- It covers **internal** acceptance only. Both gates become hard requirements
  again before any public package distribution or an RC, and reopening SDK-9a/9b
  must not proceed while they are outstanding.
- Tracked as **#66**, which also records that `.github/workflows/benchmarks.yml`
  runs only on `workflow_call`, `workflow_dispatch` and a weekly cron, so it
  gates nothing at PR time. Adding benchmark cases without changing that leaves
  them inert.
- #66 closes when both gates have committed thresholds with the target hardware
  and run parameters recorded, and an explicit decision — not a default — about
  whether they run per-PR or as a separate acceptance job.

The other three gates in this section remain in force and are unaffected.

## Verification Commands

```powershell
dotnet build src/MCServerLauncher.Daemon.API/MCServerLauncher.Daemon.API.csproj /m:1
dotnet build generators/MCServerLauncher.Daemon.Plugin.Generators/MCServerLauncher.Daemon.Plugin.Generators.csproj /m:1
dotnet pack src/MCServerLauncher.Daemon.Plugin.Sdk/MCServerLauncher.Daemon.Plugin.Sdk.csproj -c Release /m:1
dotnet build src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj /m:1
dotnet build src/MCServerLauncher.DaemonClient/MCServerLauncher.DaemonClient.csproj /m:1
dotnet test tests/MCServerLauncher.Daemon.ApiTests/MCServerLauncher.Daemon.ApiTests.csproj -c Release /m:1
dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release /m:1
dotnet run --project tools/MCServerLauncher.ProtocolDocs/MCServerLauncher.ProtocolDocs.csproj -- --check
dotnet build MCServerLauncher.slnx /m:1
```

Published-host gate:

```powershell
dotnet publish src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj -c Release -r win-x64 --self-contained
$env:MCSL_PUBLISHED_DAEMON = '<published-daemon-path>'
dotnet test tests/MCServerLauncher.PluginIntegrationTests/MCServerLauncher.PluginIntegrationTests.csproj -c Release /m:1
```

Before every commit run the repository-required Release protocol gate. Before
finishing every PR run `git diff --check` and
`git status --short --branch`.

## Exit Criteria

- Only `mcsl-plugin.json` and feature vocabulary remain.
- Plugin API 2.0 has no capability/plugin.json compatibility surface.
- Developers reference one SDK package and receive deterministic generated
  adapters and feature-specific compile-time surfaces.
- Runtime admission and generated declarations agree exactly.
- Supported SDK APIs expose neither daemon root DI nor undeclared services.
- Every application method has a method-name permission; CallerContext proxies
  recheck it (no Requires graph / prebake).
- Operations report real nested provisioning progress and restart as truthful
  `interrupted` records.
- Instance status machine exposes Starting/Stopping (and Crashed when
  detectable) with Start/Stop intermediate return semantics.
- Provisioning + generic plan kernel ship in Preview-1; cold backup/restore,
  monitoring, typed automation, and audit complete in Preview-2 with eventual
  local/remote contract coverage.
- Persistence is bounded source-generated JSON with no SQLite/reflection.
- Exact Preview-1 packages unlock MCP-0..5; Preview-2 packages unlock MCP-6..7.
- Accepted package versions and hashes are recorded for MCP implementation.
- Decision freeze doc remains the conflict authority until plans are fully
  normalized to it.

## Explicitly Deferred

- Plugin dependency DAGs and shared Contracts assemblies.
- Plugin hot install/reload/unload and collectible load contexts.
- Hostile-code sandboxing or OS network/filesystem enforcement.
- Plugin factory/installer/provider extension points.
- Online Minecraft backup coordination.
- Arbitrary automation scripts, shell, dynamic C#, or model execution.
- OAuth 2.1 authorization-server implementation.
- MCP implementation itself.
