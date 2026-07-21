# Preview-1 Package Pin (SDK-9a)

Status: accepted local package gate for MCP-0..5 unblocking.  
Branch / tip: `feat/plugin-sdk-2-preview1` @ `0b5877da`  
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

## Content fingerprints (stable within a clean Release pack of this tip)

Whole-nupkg SHA-256 is **not** used as the acceptance pin: NuGet embeds
`RepositoryCommit` and package metadata timestamps, so outer package hashes
change across commits/repacks even when package code is identical.

Acceptance fingerprints are SHA-256 of payload entries from a clean
`dotnet pack -c Release` of this branch tip:

### `MCServerLauncher.Common.1.0.0.nupkg`

| Entry | SHA-256 |
|---|---|
| `lib/net10.0/MCServerLauncher.Common.dll` | `e6fda3edfcaed2802cf6672a0997c54228e64ca8aea5485ec31bb177919b795e` |

### `MCServerLauncher.Daemon.API.2.0.0-preview.1.nupkg`

| Entry | SHA-256 |
|---|---|
| `lib/net10.0/MCServerLauncher.Daemon.API.dll` | `b94e043f6908ccf13c339b63d5d42cdfd31b4e96b00416389f43cc31bf753f2f` |
| `buildTransitive/MCServerLauncher.Daemon.API.targets` | `24aa062b14faccf3c2fbf74346716462bbbfe2f68a21bdb31d158a0831381d49` |

Nuspec dependencies (exact):

- `MCServerLauncher.Common = [1.0.0]`
- `RustyOptions = [0.10.1]`
- `Microsoft.Extensions.Logging.Abstractions = [10.0.9]`

### `MCServerLauncher.Daemon.Plugin.Sdk.2.0.0-preview.1.nupkg`

| Entry | SHA-256 |
|---|---|
| `lib/net10.0/MCServerLauncher.Daemon.Plugin.Sdk.dll` | `a533d03abb1b3aaff2d0f738483b5971077d5cb635d65c82e9341752fcdceeaa` |
| `analyzers/dotnet/cs/MCServerLauncher.Daemon.Plugin.Generators.dll` | `e52bbd03a2a3c289ffa18c03c848ce390d759d5abfc4bd127197cccf52bdae4a` |
| `buildTransitive/MCServerLauncher.Daemon.Plugin.Sdk.props` | `c0dd9844c62950e9cf678c9bb067dd030876afa4d263eedd0d146ce52e5eb895` |
| `buildTransitive/MCServerLauncher.Daemon.Plugin.Sdk.targets` | `e383f4a71ef90a5ad1a25049291c6e877c980d6acac7095ba00778a53f544573` |

Nuspec dependencies (exact):

- `MCServerLauncher.Daemon.API = [2.0.0-preview.1]`
- `Microsoft.Extensions.DependencyInjection = [10.0.9]`

> Recompute payload entry hashes after any intentional package content change. Within one clean Release pack session the entry hashes are stable; whole-nupkg hashes are not.

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
