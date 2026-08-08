# Plugin SDK 2.0 + HTTP MCP — Frozen Decision Spec

- Status: accepted grill-me decisions (2026-07-20)
- Supersedes conflicting text in:
  - `docs/plan/2026-07-19-plugin-sdk-feature-application-plan.md`
  - `docs/plan/2026-07-19-http-mcp-plugin-plan.md`
  - related `EXECUTE_PLAN.md` backlog wording
- Principle: **the full plans still ship completely**. Preview-1 / Preview-2 only stage
  merge order; they do not permanently delete scope.

When this spec and a plan disagree, **this spec wins** until the plans are fully
rewritten to match.

---

## 1. Delivery Order

```text
SDK Preview-1 (exact 2.0.0-preview.N packages)
  → MCP-0..5 (install-server main path)
  → SDK Preview-2 (backup/monitoring/automation/audit + remaining features)
  → MCP-6..7 (safety plans for destructive domains, packaging RC)
```

| Gate | Rule |
|---|---|
| MCP implementation start | Blocked until accepted exact `MCServerLauncher.Daemon.Plugin.Sdk` **Preview-1** package + matching Daemon API package hash are recorded |
| Phase 7 (Contracts/DAG) | **After** SDK locks `mcsl-plugin.json` root grammar; Phase 7 may only **add** a versioned dependency section — no rename/remeaning of `package`, `entry`, `requires.api`, `requires.features` |
| `#52` | Historical prerequisite only; do not fold work back into it |
| Full scope | Operations, provisioning, backup, monitoring, automation, audit, full MCP inventory all remain mandatory end-state |

### Preview-1 unlocks

Natural-language → structured provision plan → execute operation with real stages →
start/stop with status polling → durable MCP task mapping.

### Preview-2 completes

Cold backup/restore, monitoring, typed automation, audit, destructive
plan/confirm flows, remaining features, full remote parity as scheduled.

---

## 2. SDK Preview Feature Freezes

### Preview-1 implemented + grantable features

```text
system.query
instance.query
instance.manage
operation.query
operation.cancel
provisioning.manage
network.http.listen
auth.verify
storage.private
```

- Full vocabulary may be declared in catalog early.
- Any **unimplemented** feature in a manifest → **atomic admission skip**.
- Optional features remain deferred; every listed feature is required.

### Preview-1 feature → methods (Host principal expansion)

```text
system.query
  mcsl.system.info.get
  mcsl.java.list

instance.query
  mcsl.instance.catalog.get
  mcsl.instance.report.get
  mcsl.instance.report.list
  mcsl.instance.log.get
  mcsl.instance.settings.get

instance.manage
  mcsl.instance.create
  mcsl.instance.start
  mcsl.instance.stop
  mcsl.instance.halt
  mcsl.instance.remove
  mcsl.instance.command.send
  mcsl.instance.settings.update

operation.query
  mcsl.operation.list
  mcsl.operation.get

operation.cancel
  mcsl.operation.cancel

provisioning.manage
  mcsl.provisioning.resolve
  mcsl.provisioning.get
  mcsl.provisioning.execute

auth.verify
network.http.listen
storage.private
  (host APIs only — no business RPC methods)

mcsl.auth.token.issue
  built-in security surface; NOT a plugin feature; main-token only
```

### Feature risk (Preview-1)

| Feature | Risk |
|---|---|
| `system.query`, `instance.query`, `operation.query` | None |
| `storage.private` | Low |
| `instance.manage`, `operation.cancel`, `provisioning.manage`, `auth.verify` | Medium |
| `network.http.listen` | High |

`grant_level` ceiling: None < Low < Medium < High; level grants all **implemented**
features with `risk ≤ level`. Custom uses explicit `feature_grants`.

---

## 3. Manifest 2.0 And Developer Experience

### Manifest

- Single file: `mcsl-plugin.json` (delete `plugin.json` / `capability` release-atomically).
- Required shape: `package`, `entry`, `requires.api`, `requires.features`. `requires.features` must list grantable plugin features only (§2). Host-infra identifiers `rpc.register`, `event.publish`, and `event.subscribe` are injected automatically by the source generator and **must not** appear in `requires.features`; a manifest listing them is rejected at admission with a diagnostic error.
- No `configuration` field; optional same-directory `config.json` cold-read once.
- Manifest entry validation rules (id format, entry assembly filename, CLR type name, version range normalization) are defined canonically once; the generator and runtime apply identical rules. Generator/runtime divergence is a spec violation.
- Generator embeds normalized manifest digest using **SHA-256** as the sole canonical algorithm (defined once in a shared utility; no per-component reimplementation); runtime rejects digest mismatch.

### MCP identity

```text
package.id          = "mcp"
RPC/permission ns   = plugin.mcp.*
config keys         = mcp (admissions / plugin_grants / entries)
product/assembly    = MCServerLauncher.MCP (display/NuGet name OK)
```

Developer registers relative names (`health.get`); source generator prefixes
`plugin.mcp.`.

### Module API

```csharp
// Developer writes partial module — NOT IDaemonPlugin
partial class McpPlugin
{
    void ConfigureServices(IServiceCollection services, McpPluginFeatures features);
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
```

Generated: `DaemonPluginAdapter` (`IDaemonPlugin`), feature bags, authorized
facades, DI registration, metadata/digest, permission proxy metadata.

Hand-written `IDaemonPlugin` is a diagnostic error.

### Package reference

Plugins reference **only** `MCServerLauncher.Daemon.Plugin.Sdk` (transitive
Daemon.API/Common exact pin + analyzer + `buildTransitive` bundle targets).

### MCP first release does **not** request

`rpc.register`, `event.publish`, `event.subscribe`.

---

## 4. Daemon `config.json` Schema

Extend the existing single `config.json` (do not split policy files):

```json
{
  "port": 11452,
  "secret": "...",
  "main_token": "...",
  "file_download_sessions": 3,
  "verbose": false,
  "security": {
    "allow_main_token_issue": true,
    "max_token_ttl_seconds": 2592000,
    "api_canonical_uri": "mcsl://daemon/api/v2"
  },
  "plugins": {
    "start_timeout_seconds": 30,
    "grant_level": "Medium",
    "feature_grants": [],
    "storage": {
      "default_quota_bytes": 268435456,
      "default_max_files": 4096
    },
    "plugin_grants": {
      "mcp": ["network.http.listen"]
    },
    "admissions": {
      "mcp": {
        "decision": "allow",
        "manifest_digest": "...",
        "features": ["..."],
        "decided_at": "..."
      }
    },
    "entries": {
      "mcp": {
        "enabled": true,
        "storage_quota_bytes": null
      }
    }
  }
}
```

### Rules

- **No** `prebake_permissions` (permission-graph prebake removed).
- `feature_grants` used only when `grant_level=Custom`.
- `plugin_grants` = Approve Permanent per-plugin expansions (union into effective set).
- `admissions` records permanent decisions + digest for re-review.
- `entries` holds `enabled` and per-plugin quota overrides.
- Existing configs missing `security`/`plugins`: **cold-merge defaults** on load;
  do not force full first-run Q&A.
- Missing config file + TTY: interactive first-run (port default 11452, main_token
  random editable with confirm-only warning for custom, grant_level default Medium).
- Missing config + no TTY: silent defaults (random secret/main_token, Medium).
- `secret` remains auto-random; not an interactive prompt.

### Effective grants

```text
effective(pluginId) =
  features_allowed_by(grant_level)  // or feature_grants if Custom
  ∪ plugin_grants[pluginId]
```

Load iff `requires.features ⊆ effective` and entry explicitly enabled. A plugin id absent from `entries` is treated as **disabled** (explicit opt-in required); the absence default is `false`. An `entries` record with `"enabled": true` is required to load the plugin.

- Within ceiling → **silent load** (no first-time Approve).
- Outside ceiling + TTY → show features/risks/missing; **Deny | Approve | Approve Permanent**.
- Outside ceiling + no TTY → skip + warning.
- Manifest feature set or digest change → re-preflight (TTY) or skip (no TTY).
- Any required feature denied/unimplemented → **atomic skip** (no degraded start).

### Preflight placement

Independent **preflight phase** before existing DI Bootstrap / PluginHost.
Designed so non-plugin checks can share the phase later.

The admission policy store has a single implementation; no alternative backend is planned for P2. An `IPluginAdmissionStore` interface must not be introduced.

---

## 5. Authorization Model (CallerContext)

### Replaces (do not implement)

- Compile-time permission Requires graph
- Login-time prebake of allowed methods
- Source-generator inference of handler callees for authorization
- Composite OR “has all leaves ⇒ may call composite RPC” auto-edges

### Frozen model

| Layer | Rule |
|---|---|
| Permission name | **Equals** RPC / application method name |
| Official namespace | `mcsl.*` |
| Plugin namespace | `plugin.<package.id>.*` (developer writes relative segment; SG prefixes) |
| Entry check | Dispatch checks token against **entry method name** with segment wildcards |
| Application check | Every application call goes through generated **proxy** holding `ICallerContext` |
| User path | V2 user calls, MCP tools, `ForPrincipal(user)` → check caller's `mcsl.*` leaves |
| Host path | `plugin:<id>` principal; permissions = union of methods from granted features |
| MCP tools | **Must** use user `ForPrincipal`; never silently fall back to Host |
| Cache | V2: per-connection method→bool; MCP: per-request (optional short jti cache) |
| Wildcards | Deterministic segment match: `*` one segment, `**` remainder; case-sensitive |

### Proxy shape

```text
features.ForPrincipal(principal).Instances.StartAsync(req, ct)
// proxy closure holds ICallerContext; public app APIs do not take context params
// no AsyncLocal as sole source of truth
```

### Host principal

Admission expands FeatureCatalog methods for granted features. Host is **not** `*`
unless a feature set effectively covers all methods.

Cross-plugin delegation is **not supported in P1**: a `VerifiedPrincipal` carries the provenance of the original token only. Plugins cannot issue, forward, or proxy `VerifiedPrincipal` instances to other plugins. `VerifiedPrincipalAuthority` is daemon-internal.

### Token / JWT

| Item | Rule |
|---|---|
| Claims | `iss`, `aud` (resource URI), `sub`, `permissions`, `jti`, `exp`/`iat` |
| Future RBAC | Leave claim extension room; **do not implement roles in P1** |
| Issue RPC | `mcsl.auth.token.issue` — **main token only** + `security.allow_main_token_issue` (default true) |
| Issue surface | Built-in `/api/v2` (+ optional CLI wrapper); **not** an MCP tool |
| Issue constraints | `permissions ⊆ caller`; required non-empty `sub`; absolute `aud`; TTL ≤ `max_token_ttl_seconds` |
| V2 audience | Prefer `security.api_canonical_uri`; **P1 dual-accept** legacy `aud=MCServerLauncher.Daemon` |
| MCP audience | Plugin `http.canonical_uri` |
| Main token V2 | Always accepted as full `*` (unchanged) |
| Main token MCP | Only if plugin `auth.allow_main_token=true` (default true), via `auth.verify(..., AllowMainToken)` → `sub=daemon-main`, `perms=*` |
| File permissions | Migrate legacy `mcsl.daemon.file.*` names to **method names** (`mcsl.file.*` etc.) |

### `auth.verify`

```text
VerifyAsync(token, expectedAudience, options { AllowMainToken })
```

- Main token string equality with `AppConfig.MainToken` when `AllowMainToken`.
- Else JWT signature + exp + **aud contains expectedAudience** (the token's audience set must include `expectedAudience`; additional audience values in the token are permitted; requiring the set to have exactly one element is incorrect) + parse permissions.
- Never log raw tokens.
- Permission strings from the `permissions` claim are normalized on intake: trim surrounding whitespace and apply `ToLowerInvariant` before storage and comparison. This normalization is mandatory; removing it is a behavioral regression.

---

## 6. Instance Status Machine (Preview-1)

### Public enum

```text
Stopped
Starting
Running
Stopping
Crashed   // only when a type observer can detect crash
// Faulted reserved — not implemented in P1
```

### Transitions

| Event | Generic | Minecraft |
|---|---|---|
| Process spawned | → Starting | → Starting |
| Ready | process-ready → Running | lifecycle observer (e.g. Done) → Running |
| Ready timeout | stay Starting + observable flag; do not kill; do not fake Running | same |
| Exit during Starting | immediate process exit transitions directly to terminal (Stopped or Crashed per observer); no liveness grace window; the 500 ms delay was removed | same |
| Stop requested | → Stopping → exit Stopped | write `stop` → Stopping → exit Stopped |
| Halt | may skip Stopping → terminal | same |
| Crash detectable | usually Stopped (+ exitCode) | Crashed when observer says so |
| JVM hs_err etc. | Stopped (not Crashed) | Stopped |

### API return semantics

- `StartInstanceAsync` / `StopInstanceAsync`: return Ok when transition to
  **Starting** / **Stopping** succeeds (matches current InstanceManager intent).
- Callers poll `instance report` for intermediate/terminal states.
- Lifecycle is **not** a daemon Operation in P1.

### Lifecycle observer

Abstract `IInstanceLifecycleObserver` (final name bikesheddable): Minecraft ships
first; Terraria/etc. can plug later. Default MC ready heuristic + timeout behavior
as above.

### Consumers in P1

Daemon + Common DTO + domain events + protocol tests required.

WPF: **minimum compatibility** (no crash on new statuses); full UX polish may follow.

---

## 7. Operations, Plans, Provisioning

### Operations

- Only **`provisioning.execute`** creates a daemon Operation in P1.
- `CreateInstanceAsync` / start / stop remain non-operation paths.
- Statuses: `queued`, `running`, `succeeded`, `failed`, `cancelled`, `interrupted`.
- Restart: non-terminal → `interrupted` (never fake resume).
- Result/cancellation race: if the executor has committed side effects before a cancellation signal is observed, the committed terminal status (`succeeded` or `failed`) wins; the result is **not** downgraded to `cancelled`.
- Terminal commit retry cap: background reconciliation of a deferred terminal commit retries with a fixed delay but must stop after a bounded maximum number of attempts; on exhaustion the operation is transitioned to `interrupted`.
- Startup recovery: reconciling terminal commits deferred across a restart is a simple inline sequence of at most two calls; no separate recovery-orchestration wrapper class.
- Retention: ~7 days / 256 MiB rolling for terminal records (configurable).
- Visibility: list/get/cancel **owner-only** by default; main token / `*` sees all.
- Progress stages are **flat** in P1: a single current stage name with optional numeric percentage. Nested sub-stage trees are not part of the P1 contract and must not be implemented.
- Stages (stable):

```text
queued, resolving, downloading, verifying, extracting,
installing, configuring, finalizing
+ terminal succeeded|failed|cancelled|interrupted
```

### Generic plan kernel (P1)

Metadata-driven store:

```text
planId, planHash, kind, riskClass (routine|sensitive|destructive),
requiredPermissions[], requiresConfirmation, creatorPrincipal,
target facts/versions, expiry, single-use CAS, payload (provisioning plans use named typed fields: `instanceName`, `minecraftVersion`, `provider`, `kernelHash`; the payload is not a generic opaque `JsonElement` at the domain layer)
```

| Rule | Value |
|---|---|
| Default expiry | 15 minutes (domain may override) |
| Persistence | daemon-owned JSON |
| Restart | plans remain readable until expiry; execute always revalidates |
| Blocked plans | **persisted**; only `ready` is executable |
| Completing a blocked intent | new resolve → **new planId** (immutable) |
| P1 provisioning risk | `routine` → execute with permissions only (no confirm) |
| Confirm path | kernel supports it; destructive consumers in Preview-2 |
| Validation boundary | kernel validates structural invariants only (required fields non-empty, status shape, fact consistency, payload kind present); provisioning domain field validation (e.g. valid provider name, version format) belongs in the application layer, not the kernel |

- Startup recovery sequence: reconciling deferred terminal commits on coordinator startup is two sequential inline calls; do not introduce a dedicated recovery-orchestration class.

### Provisioning application

```text
mcsl.provisioning.resolve  → immutable plan (ready|blocked + unresolved[])
mcsl.provisioning.get      → plan snapshot
mcsl.provisioning.execute  → operationId + start execution
```

Plugin feature surface: all under **`provisioning.manage`** (no separate plan.* feature in P1).

| Rule | Value |
|---|---|
| Execute boundary | install/configure to **startable Stopped** instance; first-start is separate `instance_start` |
| Providers P1 | Vanilla, Paper, Fabric, Forge, NeoForge, Quilt only |
| Custom URL/archive/script | deferred |
| EULA / choices | blocked plan with structured `unresolved[]`; accept via new resolve inputs |
| Idempotency | optional `idempotencyKey` on resolve; execute single-flight per plan |
| Create dual-path | existing `CreateInstanceAsync` remains for WPF/compat; auto providers' progress path is provisioning |

### MCP lifecycle tools

- `instance_restart` = tool orchestration stop→wait terminal→start (no `mcsl.instance.restart`).
- `halt` **not** in MCP-0..5 tool inventory.

---

## 8. Plugin Host / ALC / HTTP / Storage

| Item | Decision |
|---|---|
| Kestrel | Daemon declares the `Microsoft.AspNetCore.App` runtime framework as required by .NET plugin loading; the Kestrel application/listener, plugin DI provider, and MCP SDK assemblies remain plugin-private and are not daemon services |
| Shared ALC | Daemon.API, Common, RustyOptions, Logging.Abstractions, **DI.Abstractions** |
| DI implementation | May be private to plugin bundle |
| `network.http.listen` | Validate + register + diagnostics; plugin opens its own listener |
| Port conflict | **IP:port exclusive** among plugins and vs daemon `port` |
| Start timeout | Config default **30s**; MCP must Listen before host gives up |
| Start failure | Skip/fail plugin; keep `/api/v2` healthy |
| `storage.private` | Default 256 MiB + max files; path-safe API; no raw root path leak |
| MCP task store | Durable mappings under **storage.private**; operations stay daemon-owned |
| PluginHost lifecycle | `PluginHost` implements `IAsyncDisposable`; callers must `await` disposal to ensure all plugin shutdown work completes before the host exits |
| Shutdown deadline | Each stop or dispose call is governed by a **single** deadline; stacking multiple nested deadline layers for P1 well-behaved plugins is prohibited |
| MCP-0 | **Hard gate**: published-host spike must pass before MCP business tools |

---

## 9. MCP Preview-1 Surface

### Dependency pins

```text
MCServerLauncher.Daemon.Plugin.Sdk = accepted 2.0.0-preview.N (P1)
ModelContextProtocol.AspNetCore = 1.4.1
```

The experimental Tasks API is supplied by the core dependency chain of
`ModelContextProtocol.AspNetCore` 1.4.1 and remains adapter-isolated. There is
no separate `ModelContextProtocol.Extensions.Tasks` 1.4.1 package.

### Manifest features (MCP)

Only the P1 set in §2 (no rpc/event features).

### Plugin `config.json`

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

No secrets, no automation block in P1, cold-read once.

### Tools (MCP-0..5)

```text
daemon_status
instance_list, instance_get, instance_logs
operation_list, operation_get, operation_cancel
provision_plan, plan_get, plan_execute
instance_start, instance_stop, instance_restart
```

### Resources (MCP-0..5)

```text
mcsl://daemon/status
mcsl://instances
mcsl://instances/{id}
mcsl://instances/{id}/logs
mcsl://operations/{id}
```

### Explicitly later (MCP-6+)

`file_*`, `backup_*`, `automation_*`, `audit_*`, `event_rule_*`, `plan_confirm`,
sensitive auto-execute policy consumers, destructive confirm UX.

### Auth for tools

Every tool: Bearer → `auth.verify` → `ForPrincipal` → check underlying `mcsl.*`
method permissions. `tools/list` filters by what the principal can pass.

---

## 10. SDK Work Package Remap

### Preview-1

| Package | Content |
|---|---|
| SDK-1 | Manifest 2.0, FeatureCatalog (summary/risk/methods), grant_level admission, preflight, config schema |
| SDK-2 | Generator + Plugin.Sdk package |
| SDK-3 | Private DI, plugin config, storage.private, http policy, ALC DI.Abstractions share |
| SDK-4 | CallerContext proxies, method=permission, segment wildcards, auth.verify/issue, Host/ForPrincipal |
| SDK-5 | Operation core + progress |
| SDK-5b | Instance status machine + lifecycle observer |
| SDK-6 | Provisioning + generic plan kernel (routine execute) |
| SDK-9a | Exact `2.0.0-preview.N` package gate + hashes for MCP |

### Preview-2

| Package | Content |
|---|---|
| SDK-7 | Cold backup/restore |
| SDK-8 | Monitoring, automation, audit |
| SDK-4b / remote | Full RPC + DaemonClient + OpenRPC for new domains (may ship as P1.1) |
| SDK-9b | Preview-2 package gate |

Remote application parity is **mandatory eventually**; plugin-first staging is OK.

---

## 11. MCP Work Package Remap

| Package | Depends on | Content |
|---|---|---|
| MCP-0 | SDK P1 package | Hard spike: ALC Kestrel, `/mcp`, Bearer, shutdown isolation |
| MCP-1 | MCP-0 | Manifest, cold config, private DI, host lifecycle |
| MCP-2 | MCP-1 | auth.verify binding, AllowMainToken, tool filter + recheck |
| MCP-3 | MCP-2 | Read tools/resources (P1 inventory) |
| MCP-4 | MCP-3 | Durable tasks ↔ operations |
| MCP-5 | MCP-4 | Provisioning + lifecycle tools |
| MCP-6 | SDK P2 | Files, backup, automation, audit, plan_confirm |
| MCP-7 | MCP-6 | Packaging, docs, RC |

---

## 12. Package Distribution

- MCP repo pins exact versions + lockfile.
- nuget.org public preview not required for first accepted pin.

### 12.1 Preview-2 amendment (2026-07-29)

The original clause required publishing exact preview nupkgs as GitHub Release
assets. Where that stands per version:

| Version | State |
|---|---|
| `2.0.0-preview.2` | **Published** — Release `2.0.0-preview.2`, 2026-07-24, carries all three nupkgs. Superseded; retained as history, not a supported baseline. |
| `2.0.0-preview.3` | Not published. Frozen Preview-1 baseline, consumed by the MCP repo through a local feed. |
| `2.0.0-preview.4` | Not published. Superseded Preview-2 internal baseline: recorded before monitoring stored disk, responsiveness and lifecycle events, so its `MCServerLauncher.Common` fingerprint stopped describing the tree. |
| `2.0.0-preview.5` | Not published. Superseded Preview-2 internal baseline. Two payloads differ from `.4`, for unrelated reasons: `MCServerLauncher.Common` because its source changed, and `MCServerLauncher.Daemon.API` because the pinned SDK moved from `10.0.201` to `10.0.302` — both carry source-generated JSON metadata and that generator ships with the SDK. The remaining six packed entries are byte-identical. |
| `2.0.0-preview.6` | Not published. Superseded Preview-2 internal baseline. One payload differs from `.5`: `MCServerLauncher.Common`, because the automation trigger and action vocabulary was completed in it. The other seven packed entries are byte-identical. |
| `2.0.0-preview.7` | Not published. Superseded Preview-2 internal baseline. One of the eight packed entries differs from `.6`: `MCServerLauncher.Common`, because the automation cross-review added an optional `AuditRecord.Detail` and tightened the union converters. The other seven are byte-identical, the SDK facade included — its hash moved mid-branch on an intermediate state of `Common` and moved back, which is the `.6` caveat behaving exactly as recorded. |
| SDK/API `1.0.0`, Common base `0.2.0.0` | Current internal package base-version record. Common packages normalize to NuGet version `0.2.0`. Same local-feed consumption model; public distribution still requires reopening SDK-9a/9b. Fingerprints: `docs/preview2-package-pin.md`, machine-checked by `PinnedPayloadHashesMatchTheAcceptanceRecord`. |

So publication is **suspended from `.3` onward**, not retroactively denied. An
earlier revision of this amendment claimed no Release existed for any of the
three; that was wrong about `.2`, which is public and discoverable.

**Why this is written down rather than silently diverged from:** the
implementation had already stopped publishing while this section still required
it, so a reviewer auditing against the spec correctly read the delivery as
incomplete. The spec is the authority; when the decision changes, the spec
changes with it — including when the change is a correction like the one above.

**Reopening conditions.** Publication resumes only when SDK-9a/9b is explicitly
reopened. The order matters, because acceptance cannot precede the artifact it
tests:

1. A named consumer outside this organization needs the package from a public
   source — a local feed no longer suffices.
2. The payload fingerprints in `docs/preview2-package-pin.md` reproduce from a
   canonical checkout on a machine other than the authoring one.
3. The packages are published to an explicit **candidate source** — a draft
   Release or a staging feed — which is not the supported public artifact.
4. The published-host acceptance suite runs green against that candidate source
   as an external feed, not the self-pack path.
5. Only then is the candidate promoted to a public Release.

Steps 3 and 5 are separate on purpose. Requiring acceptance "against the exact
external source" before any publication existed made the gate unsatisfiable: the
suite needs a real feed to point at, so something must be published for it to
test. A candidate source supplies that without committing to a supported
artifact.

Until then `docs/preview{1,2}-package-pin.md` are the acceptance record, and
SDK-9b closes on internal implementation and test state only.

---

## 13. Open Items (non-blocking for plan write-up)

- Final English FeatureCatalog summary strings
- Exact Minecraft ready regex list + default ready-timeout seconds
- Optional admin operation permission name beyond main-token/`*`
- RBAC role claim design (post-P1)

---

## 14. Document Control

| Date | Change |
|---|---|
| 2026-07-20 | Initial frozen decisions from grill-me session |
| 2026-07-29 | §12.1 suspends preview Release-asset publication for `2.0.0-preview.2/.3/.4` and records the reopening conditions |
