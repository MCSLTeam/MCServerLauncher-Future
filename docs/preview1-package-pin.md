# Preview-1 Package Pin

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

## Content fingerprints (deterministic)

Whole-nupkg SHA-256 is **not** used as the acceptance pin: NuGet embeds
`RepositoryCommit` and package metadata timestamps, so outer package hashes
change across commits/repacks even when package code is identical.

Repo build settings stabilize package payload assemblies:

- `Directory.Build.props`: `Deterministic=true`, `IncludeSourceRevisionInInformationalVersion=false`
- `Directory.Build.targets` when packing with `-p:MCSL_PIN_PACKAGE_PAYLOAD=true` (packable packages + Roslyn generator only):
  - `ContinuousIntegrationBuild=true` (normalized paths)
  - `DebugType=none` / `DebugSymbols=false` (no portable PDB / Source Link / CodeView path leakage into DLLs)
  - PDB excluded from package content (`AllowedOutputExtensionsInPackageBuildOutputFolder` + pack-item strip)
- `.gitattributes` forces `text eol=lf` for packed `buildTransitive/**` assets so text payload hashes are checkout-stable

Normal daemon/WPF builds keep referenced-project PDBs so release `include_pdb=true` archives stay complete. The pin property is pack-only.

Reproducibility requires .NET SDK `10.0.201` (`global.json` + release workflow
`setup-dotnet`). Payload fingerprints are only guaranteed under that SDK.

Acceptance fingerprints are SHA-256 of payload entries from:

```powershell
dotnet pack src/MCServerLauncher.Common/MCServerLauncher.Common.csproj -c Release -o artifacts/preview1-packages /m:1 -p:MCSL_PIN_PACKAGE_PAYLOAD=true
dotnet pack src/MCServerLauncher.Daemon.API/MCServerLauncher.Daemon.API.csproj -c Release -o artifacts/preview1-packages /m:1 -p:MCSL_PIN_PACKAGE_PAYLOAD=true
dotnet pack src/MCServerLauncher.Daemon.Plugin.Sdk/MCServerLauncher.Daemon.Plugin.Sdk.csproj -c Release -o artifacts/preview1-packages /m:1 -p:MCSL_PIN_PACKAGE_PAYLOAD=true
```

The GitHub release NuGet job, when the release tag is `2.0.0-preview.1` /
`v2.0.0-preview.1`, packs Common / Daemon.API / Plugin.Sdk with
`-p:MCSL_PIN_PACKAGE_PAYLOAD=true` and each project's declared PackageVersion
(`Common 1.0.0`, `Daemon.API` / `Plugin.Sdk 2.0.0-preview.1`). Other tags keep
the historical tag-derived version override for Common + Daemon.API only.

### `MCServerLauncher.Common.1.0.0.nupkg`

| Entry | SHA-256 |
|---|---|
| `lib/net10.0/MCServerLauncher.Common.dll` | `9bcf5e521551ca4f974abd1b1c0e2e534db359234fb53c6a01e51e9ff69c06cf` |

### `MCServerLauncher.Daemon.API.2.0.0-preview.1.nupkg`

| Entry | SHA-256 |
|---|---|
| `lib/net10.0/MCServerLauncher.Daemon.API.dll` | `0f566a306b909a27113a80d7f6602af59476f96d868a65b234890eb4e3cc6eb5` |
| `buildTransitive/MCServerLauncher.Daemon.API.targets` | `81a79275e7ab2a10cf08ac950c27692db1e7455387944377b06047b0a340c17c` |

Nuspec dependencies (exact):

- `MCServerLauncher.Common = [1.0.0]`
- `RustyOptions = [0.10.1]`
- `Microsoft.Extensions.Logging.Abstractions = [10.0.9]`

### `MCServerLauncher.Daemon.Plugin.Sdk.2.0.0-preview.1.nupkg`

| Entry | SHA-256 |
|---|---|
| `lib/net10.0/MCServerLauncher.Daemon.Plugin.Sdk.dll` | `c52be37810be8730f28f6577162b30c838c02c3ca5f2790f6eeea36035f3d0da` |
| `analyzers/dotnet/cs/MCServerLauncher.Daemon.Plugin.Generators.dll` | `f65dfdbe919bfbe3237c68d6729ce6063fe70be60414e85a80baf22aa562d5fe` |
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
