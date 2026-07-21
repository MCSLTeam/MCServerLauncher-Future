# Preview-1 Package Pin (SDK-9a)

Status: accepted local package gate for MCP-0..5 unblocking.  
Branch: `feat/plugin-sdk-2-preview1`  
Decision source: `docs/superpowers/specs/2026-07-20-plugin-sdk-mcp-decisions.md` §1 / §10 / §12.

## Exact versions

| Package id | Version |
|---|---|
| `MCServerLauncher.Daemon.Plugin.Sdk` | `2.0.0-preview.1` |
| `MCServerLauncher.Daemon.API` | `2.0.0-preview.1` |
| `MCServerLauncher.Common` | `1.0.0` (ABI dependency of Daemon API; exact pin) |

MCP and external consumers MUST pin exact versions (no floating ranges) and prefer a lockfile.

## Dependency pins in packages

- `MCServerLauncher.Daemon.API` → `MCServerLauncher.Common = [1.0.0]`
- `MCServerLauncher.Daemon.Plugin.Sdk` → `MCServerLauncher.Daemon.API = [2.0.0-preview.1]`
- `MCServerLauncher.Daemon.Plugin.Sdk` embeds `analyzers/dotnet/cs/MCServerLauncher.Daemon.Plugin.Generators.dll`
- `MCServerLauncher.Daemon.Plugin.Sdk` carries `buildTransitive` props/targets for `mcsl-plugin.json` + `MCSLPluginBundle`

## Content fingerprints (stable)

Whole-nupkg SHA-256 is **not** used as the acceptance pin: NuGet embeds
`RepositoryCommit` and package metadata timestamps, so outer package hashes
change across commits/repacks even when package code is identical.

Acceptance fingerprints are SHA-256 of payload entries:

### `MCServerLauncher.Common.1.0.0.nupkg`

| Entry | SHA-256 |
|---|---|
| `lib/net10.0/MCServerLauncher.Common.dll` | `ca21afac9868f48b47f4b845de4b114ede790816f9892dd63410678d4fb9ed2f` |

### `MCServerLauncher.Daemon.API.2.0.0-preview.1.nupkg`

| Entry | SHA-256 |
|---|---|
| `lib/net10.0/MCServerLauncher.Daemon.API.dll` | `31c0168d6d2bc742d60d15ed88ee78650573a73dea7de8673f76dd25de88200b` |
| `buildTransitive/MCServerLauncher.Daemon.API.targets` | `24aa062b14faccf3c2fbf74346716462bbbfe2f68a21bdb31d158a0831381d49` |

Nuspec dependencies (exact):

- `MCServerLauncher.Common = [1.0.0]`
- `RustyOptions = [0.10.1]`
- `Microsoft.Extensions.Logging.Abstractions = [10.0.9]`

### `MCServerLauncher.Daemon.Plugin.Sdk.2.0.0-preview.1.nupkg`

| Entry | SHA-256 |
|---|---|
| `lib/net10.0/MCServerLauncher.Daemon.Plugin.Sdk.dll` | `77bf5868c50eacec2a26fcac8ce41f1ffea5336c17430307b29e93d7df7b8f40` |
| `analyzers/dotnet/cs/MCServerLauncher.Daemon.Plugin.Generators.dll` | `62bae4cc4fa8038974fd21f78bea57db825ae7eb3cce75ad1c5e9f134f1423c1` |
| `buildTransitive/MCServerLauncher.Daemon.Plugin.Sdk.props` | `c0dd9844c62950e9cf678c9bb067dd030876afa4d263eedd0d146ce52e5eb895` |
| `buildTransitive/MCServerLauncher.Daemon.Plugin.Sdk.targets` | `e383f4a71ef90a5ad1a25049291c6e877c980d6acac7095ba00778a53f544573` |

Nuspec dependencies (exact):

- `MCServerLauncher.Daemon.API = [2.0.0-preview.1]`
- `Microsoft.Extensions.DependencyInjection = [10.0.9]`

## Preview-1 implemented FeatureCatalog freeze

Grantable/implemented for admission (decision §2):

- `system.query`
- `instance.query`
- `instance.manage`
- `operation.query`
- `operation.cancel`
- `provisioning.manage`
- `network.http.listen`
- `auth.verify`
- `storage.private`

Also implemented host infrastructure features:

- `rpc.register`
- `event.publish`

Unimplemented features remain catalog vocabulary only; declaring them causes atomic admission skip.

## MCP pin snippet

```xml
<PackageReference Include="MCServerLauncher.Daemon.Plugin.Sdk" Version="2.0.0-preview.1" />
```

Local feed restore requires the three nupkgs above plus nuget.org for transitive BCL packages.

## Verification

```powershell
dotnet test tests/MCServerLauncher.Daemon.ApiTests/MCServerLauncher.Daemon.ApiTests.csproj -c Release --filter FullyQualifiedName~PackageContract /m:1
dotnet test tests/MCServerLauncher.Daemon.ApiTests/MCServerLauncher.Daemon.ApiTests.csproj -c Release --filter FullyQualifiedName~FeatureCatalogPreview1 /m:1
dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release /m:1
dotnet run --project tools/MCServerLauncher.ProtocolDocs/MCServerLauncher.ProtocolDocs.csproj -- --check
```

Distribution: attach the three nupkgs as GitHub Release assets (decision §12). nuget.org public preview is not required for the first accepted pin.
