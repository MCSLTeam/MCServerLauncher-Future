# Preview-2 Package Pin

Status: internal baseline `2.0.0-preview.4` (2026-07-29) for MCP-6+.
No Release exists for `preview.4`, and none is planned. `2.0.0-preview.2` was
published as a Release on 2026-07-24 and is superseded — it is history, not a
supported baseline, and preview.3/.4 deliberately did not follow it. SDK-9b
closes the Preview-2 gate as an **internal implementation and test acceptance
state only**; public package distribution (GitHub Release assets or nuget.org)
requires explicitly reopening SDK-9a/9b via the staged conditions in the spec.
Decision source: `docs/spec/2026-07-20-plugin-sdk-mcp-decisions.md`,
sections 1, 10, and 12. Preview-1 history: `docs/preview1-package-pin.md`.

## Why preview.4 and not preview.3

`2.0.0-preview.3` is the frozen Preview-1 baseline that MCP-0..5 restores
against. Preview-2 changes the public API surface — the aggregate
`IDaemonApplication` gained four domains, `IFileApplication` split into two
narrow views, and four new application contracts shipped — so it cannot reuse
that version. Same-version/different-payload packages break locked restores
that mix caches (NU1403), which is the same reason preview.2 was retired.

## Exact versions

| Package id | Version |
|---|---|
| `MCServerLauncher.Daemon.Plugin.Sdk` | `2.0.0-preview.4` |
| `MCServerLauncher.Daemon.API` | `2.0.0-preview.4` |
| `MCServerLauncher.Common` | `2.0.0-preview.4` |

Consumers MUST pin exact versions, without floating ranges, and should use a
lockfile.

### Dependency pins in packages

- `MCServerLauncher.Daemon.API` -> `MCServerLauncher.Common = [2.0.0-preview.4]`
- `MCServerLauncher.Daemon.Plugin.Sdk` -> `MCServerLauncher.Daemon.API = [2.0.0-preview.4]`
- `MCServerLauncher.Daemon.Plugin.Sdk` embeds both the generator and its
  `NuGet.Versioning.dll` analyzer dependency.
- `MCServerLauncher.Daemon.Plugin.Sdk` carries `buildTransitive` props and
  targets for `mcsl-plugin.json` and `MCSLPluginBundle`.

## Content fingerprints

Whole-nupkg SHA-256 is not the acceptance pin: NuGet embeds repository and
timestamp metadata that can change across repacks while payload code stays
identical. The fingerprints below cover every DLL that executes in the
consumer build or runtime path, plus every `buildTransitive` asset.

Built from branch `feat/sdk-preview-2` with .NET SDK `10.0.201`; reproducibility
requires that exact SDK. `global.json` pins it for both local and CI builds.

The `buildTransitive` assets are packed as verbatim byte copies, so their nupkg
entry hash is the hash of the checked-out file. `.gitattributes` declares those
paths `text eol=lf`, so derive their fingerprints from the committed blob rather
than from an ambient working tree — a tree left over from before that rule, or
one written by a client that ignores it, carries CRLF and hashes differently:

```bash
git show HEAD:src/MCServerLauncher.Daemon.Plugin.Sdk/buildTransitive/MCServerLauncher.Daemon.Plugin.Sdk.props | sha256sum
```

### `MCServerLauncher.Common.2.0.0-preview.4.nupkg`

| Entry | SHA-256 |
|---|---|
| `lib/net10.0/MCServerLauncher.Common.dll` | `fc2c69f6c1cf01bc3f8d891835e6fd196b0b2e3ae82c5ac62533f8812a3e2420` |

### `MCServerLauncher.Daemon.API.2.0.0-preview.4.nupkg`

| Entry | SHA-256 |
|---|---|
| `lib/net10.0/MCServerLauncher.Daemon.API.dll` | `9c3eff44e6a5b5b4620d6a46a92bb778cb415cfcf764ea0d0ac0439bd3c03c3d` |
| `buildTransitive/MCServerLauncher.Daemon.API.targets` | `81a79275e7ab2a10cf08ac950c27692db1e7455387944377b06047b0a340c17c` |

### `MCServerLauncher.Daemon.Plugin.Sdk.2.0.0-preview.4.nupkg`

| Entry | SHA-256 |
|---|---|
| `lib/net10.0/MCServerLauncher.Daemon.Plugin.Sdk.dll` | `7b675275b82cc6ecbd51793cd2c4cfd217dbf6b784bf5aeb21bda36cf51a850c` |
| `analyzers/dotnet/cs/MCServerLauncher.Daemon.Plugin.Generators.dll` | `dd0e1a4f4b7b49d994910ae0afd63347e1d6f342fadd5f04f0511f501da6fcbb` |
| `analyzers/dotnet/cs/NuGet.Versioning.dll` | `5ccab32f44a29834becbf640cfac4b119edce8496a02e94ef20e1b1d2e652b26` |
| `buildTransitive/MCServerLauncher.Daemon.Plugin.Sdk.props` | `c0dd9844c62950e9cf678c9bb067dd030876afa4d263eedd0d146ce52e5eb895` |
| `buildTransitive/MCServerLauncher.Daemon.Plugin.Sdk.targets` | `e383f4a71ef90a5ad1a25049291c6e877c980d6acac7095ba00778a53f544573` |

The SDK payload DLL fingerprint is unchanged from preview.2/preview.3: the SDK
assembly itself carries no Preview-2 surface, only the generator does.

Build the candidate packages with:

```powershell
$pinBuildRoot = Join-Path (Get-Location).Path 'artifacts/preview2-package-pin-build'
dotnet pack src/MCServerLauncher.Common/MCServerLauncher.Common.csproj -c Release -o artifacts/preview2-package-pin /m:1 -p:MCSL_PIN_PACKAGE_PAYLOAD=true "-p:MCSLPinBuildRoot=$pinBuildRoot"
dotnet pack src/MCServerLauncher.Daemon.API/MCServerLauncher.Daemon.API.csproj -c Release -o artifacts/preview2-package-pin /m:1 -p:MCSL_PIN_PACKAGE_PAYLOAD=true "-p:MCSLPinBuildRoot=$pinBuildRoot"
dotnet pack src/MCServerLauncher.Daemon.Plugin.Sdk/MCServerLauncher.Daemon.Plugin.Sdk.csproj -c Release -o artifacts/preview2-package-pin /m:1 -p:MCSL_PIN_PACKAGE_PAYLOAD=true "-p:MCSLPinBuildRoot=$pinBuildRoot"
```

## Preview-2 implemented FeatureCatalog freeze

Preview-2 completes the grantable feature *vocabulary* — every feature name a
manifest may declare is now implemented except `event.subscribe`. It does not
follow that every domain behind those names is feature-complete: monitoring and
automation ship a subset of their planned surface (see "Scope delivered" below).
Implemented and grantable for admission:

| Feature | Risk |
|---|---|
| `system.query` | None |
| `instance.query` | None |
| `operation.query` | None |
| `monitoring.query` | None |
| `audit.query` | None |
| `storage.private` | Low |
| `file.read` | Low |
| `instance.manage` | Medium |
| `operation.cancel` | Medium |
| `provisioning.manage` | Medium |
| `backup.manage` | Medium |
| `automation.manage` | Medium |
| `event-rule.manage` | Medium |
| `file.write` | Medium |
| `auth.verify` | Medium |
| `network.http.listen` | High |

Host infrastructure also implements `rpc.register` and `event.publish`.

`event.subscribe` is the **only** remaining unimplemented feature and stays
that way deliberately: the feature-application plan keeps it owned by the
separate Phase 7 Contracts/dependency-DAG plan. Declaring it in a manifest
still causes an atomic admission skip.
`FeatureCatalogPreview1Tests` asserts it is the only unimplemented entry, so a
future feature landing unimplemented fails loudly rather than silently
widening this freeze.

A `grant_level` of Medium now admits 17 features; Low admits 7.

## New in Preview-2

- Cold backup and confirmed restore (`mcsl.backup.*`, feature `backup.manage`).
- Bounded daemon metrics (`mcsl.monitoring.*`, feature `monitoring.query`).
- Typed automation policies with the `automation.intent` confirmation plan
  (`mcsl.automation.*`, feature `automation.manage`).
- Bounded structured audit history (`mcsl.audit.query`, feature `audit.query`).
- Remote parity: `IDaemonApplication` carries all ten domains and
  `DaemonClient` exposes them, so a remote caller composes the surface a local
  caller does.
- Remaining plugin features `file.read`, `file.write`, `event-rule.manage`.

## Scope delivered vs planned

The feature-application plan specifies more monitoring and automation surface
than Preview-2 ships. The gap is deliberate and bounded; it is recorded here
rather than left implicit so the delivered state is not mistaken for the planned
one.

| Area | Delivered | Planned but deferred |
|---|---|---|
| Monitoring metrics | system CPU, memory used/total, per-instance status | disk, responsiveness, significant events |
| Automation triggers | `crash-loop`, `unexpected-exit`, `sustained-metric`, `maintenance-window` | unresponsive, disk, duration |
| Automation actions | `restart-instance`, `stop-instance`, `notification`, `confirmation-plan` | maintenance-state, restart-suppression, diagnostics, explicit-audit |

Each deferred item widens a closed union, so adding one is not a drop-in: it
needs its `JsonTypeInfo`, converter discriminator, daemon/RPC/DaemonClient
parity, validation, and protocol tests. They are tracked as follow-up issues
with acceptance criteria rather than folded into this baseline.

## Consumer pin

```xml
<PackageReference Include="MCServerLauncher.Daemon.Plugin.Sdk" Version="2.0.0-preview.4" />
```

Local-feed restore requires the three nupkgs above and nuget.org for
transitive dependencies.

## Verification

```powershell
dotnet test tests/MCServerLauncher.Daemon.ApiTests/MCServerLauncher.Daemon.ApiTests.csproj -c Release --filter FullyQualifiedName~PackageContract /m:1
dotnet test tests/MCServerLauncher.Daemon.ApiTests/MCServerLauncher.Daemon.ApiTests.csproj -c Release /m:1
dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release /m:1
dotnet test tests/MCServerLauncher.Daemon.Plugin.Generators.Tests/MCServerLauncher.Daemon.Plugin.Generators.Tests.csproj -c Release /m:1
dotnet run --project tools/MCServerLauncher.ProtocolDocs/MCServerLauncher.ProtocolDocs.csproj -- --check
pwsh -File tools/VerifyNoV1Runtime.ps1
```

The published-host suite runs with `MCSL_PUBLISHED_DAEMON` pointing at a
published daemon and `MCSL_PLUGIN_PACKAGE_SOURCE` pointing at the three
nupkgs above.

### Known coverage gap carried into MCP-6

The published-host suite still exercises only plugin load, RPC/event serving,
and shutdown behaviour. It does not yet drive `mcsl.backup.*` or poll a real
long-running operation against a live daemon. Both need a daemon-side fake
operation fixture and are tracked as the remaining SDK-9b/MCP-6 item; they do
not block the internal pin because every one of those paths is covered by the
in-process protocol suite.

Distribution is internal-only. Public distribution requires explicitly
reopening the package gate.
