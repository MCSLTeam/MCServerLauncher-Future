# Preview-1 Package Pin

Status: accepted for MCP-0..5.
Branch: `feat/plugin-sdk-2-preview1`
Release: `2.0.0-preview.2` ([GitHub Release](https://github.com/MCSLTeam/MCServerLauncher-Future/releases/tag/2.0.0-preview.2))
Source commit: `a5510a6089a88692ff7ff0cde7fd0b6249a3b965`
Decision source: `docs/superpowers/specs/2026-07-20-plugin-sdk-mcp-decisions.md`, sections 1, 10, and 12.

## Gate status

The accepted packages were downloaded from the public prerelease above after
Release workflow run `30101725253` completed successfully. A second independent
Ubuntu `nuget` job (attempt 2, artifact `8600806535`) rebuilt the exact tag; all
eight executable/build payload fingerprints matched the downloaded Release
assets. Exact nuspec dependencies, package-only restore, bundle de-duplication,
and the published Release daemon fixture also passed. The latter ran all three
PluginIntegrationTests with `MCSL_PLUGIN_PACKAGE_SOURCE` bound to the downloaded
Release packages.

## Exact versions

| Package id | Version |
|---|---|
| `MCServerLauncher.Daemon.Plugin.Sdk` | `2.0.0-preview.2` |
| `MCServerLauncher.Daemon.API` | `2.0.0-preview.2` |
| `MCServerLauncher.Common` | `2.0.0-preview.2` |

MCP and external consumers MUST pin exact versions, without floating ranges,
and should use a lockfile.

## Dependency pins in packages

- `MCServerLauncher.Daemon.API` -> `MCServerLauncher.Common = [2.0.0-preview.2]`
- `MCServerLauncher.Daemon.Plugin.Sdk` -> `MCServerLauncher.Daemon.API = [2.0.0-preview.2]`
- `MCServerLauncher.Daemon.Plugin.Sdk` embeds both the generator and its
  `NuGet.Versioning.dll` analyzer dependency.
- `MCServerLauncher.Daemon.Plugin.Sdk` carries `buildTransitive` props and
  targets for `mcsl-plugin.json` and `MCSLPluginBundle`.

## Content fingerprints

Whole-nupkg SHA-256 is not the acceptance pin: NuGet embeds repository and
timestamp metadata that can change across repacks while payload code remains
identical. The acceptance fingerprints below cover every DLL that executes in
the consumer build or runtime path, plus every `buildTransitive` asset.

Published Release asset SHA-256 values:

| Package | SHA-256 |
|---|---|
| `MCServerLauncher.Common.2.0.0-preview.2.nupkg` | `1d7ded61ebf209d034c0465c13c86651771772a35ef6843482cc64ee09c2a430` |
| `MCServerLauncher.Daemon.API.2.0.0-preview.2.nupkg` | `b8b2e5c6d76f7b39692b4a0cf14ff53954ee97848349c48580d5d4fdb3442d21` |
| `MCServerLauncher.Daemon.Plugin.Sdk.2.0.0-preview.2.nupkg` | `68081605db1cb5ccf9c78f882b9a54126183ad54da78be523f6a3748beb3214c` |

Repository build settings make payloads reproducible when packing with
`-p:MCSL_PIN_PACKAGE_PAYLOAD=true`:

- `Directory.Build.props` isolates pin output and intermediate roots, maps the
  caller-selected pin root to a stable compiler path, enables deterministic CI
  compilation, omits source revision metadata, and disables PDB/source output.
- `Directory.Build.targets` excludes PDBs from package content for packable
  projects.
- `.gitattributes` forces LF for packed `buildTransitive` assets.

Normal daemon and WPF builds keep referenced-project PDBs. The pin property is
pack-only. Reproducibility requires .NET SDK `10.0.201` from `global.json`.

Build the candidate packages with:

```powershell
$pinBuildRoot = Join-Path (Get-Location).Path 'artifacts/preview1-package-pin-build-preview2'
dotnet pack src/MCServerLauncher.Common/MCServerLauncher.Common.csproj -c Release -o artifacts/preview1-package-pin-preview2 /m:1 -p:MCSL_PIN_PACKAGE_PAYLOAD=true "-p:MCSLPinBuildRoot=$pinBuildRoot"
dotnet pack src/MCServerLauncher.Daemon.API/MCServerLauncher.Daemon.API.csproj -c Release -o artifacts/preview1-package-pin-preview2 /m:1 -p:MCSL_PIN_PACKAGE_PAYLOAD=true "-p:MCSLPinBuildRoot=$pinBuildRoot"
dotnet pack src/MCServerLauncher.Daemon.Plugin.Sdk/MCServerLauncher.Daemon.Plugin.Sdk.csproj -c Release -o artifacts/preview1-package-pin-preview2 /m:1 -p:MCSL_PIN_PACKAGE_PAYLOAD=true "-p:MCSLPinBuildRoot=$pinBuildRoot"
```

The release workflow recognizes the `2.0.0-preview.2` tag, packs all three
declared versions with `MCSL_PIN_PACKAGE_PAYLOAD=true`, and attaches each nupkg.

### `MCServerLauncher.Common.2.0.0-preview.2.nupkg`

| Entry | SHA-256 |
|---|---|
| `lib/net10.0/MCServerLauncher.Common.dll` | `4d44f9993d9def979db6e80c7004c4d2ddf8fa92cdaf5511dc69842ad6dae38c` |

### `MCServerLauncher.Daemon.API.2.0.0-preview.2.nupkg`

| Entry | SHA-256 |
|---|---|
| `lib/net10.0/MCServerLauncher.Daemon.API.dll` | `9896e863ad0eaf394e1379d429d1cbb4e63143ce53577124bfbce8ab40c6d83e` |
| `buildTransitive/MCServerLauncher.Daemon.API.targets` | `81a79275e7ab2a10cf08ac950c27692db1e7455387944377b06047b0a340c17c` |

Nuspec dependencies are exact:

- `MCServerLauncher.Common = [2.0.0-preview.2]`
- `RustyOptions = [0.10.1]`
- `Microsoft.Extensions.Logging.Abstractions = [10.0.9]`

### `MCServerLauncher.Daemon.Plugin.Sdk.2.0.0-preview.2.nupkg`

| Entry | SHA-256 |
|---|---|
| `lib/net10.0/MCServerLauncher.Daemon.Plugin.Sdk.dll` | `7b675275b82cc6ecbd51793cd2c4cfd217dbf6b784bf5aeb21bda36cf51a850c` |
| `analyzers/dotnet/cs/MCServerLauncher.Daemon.Plugin.Generators.dll` | `704a9051e922c93623884d28cc1c1cca684e389e5b8645cc141d49b017ebab2f` |
| `analyzers/dotnet/cs/NuGet.Versioning.dll` | `5ccab32f44a29834becbf640cfac4b119edce8496a02e94ef20e1b1d2e652b26` |
| `buildTransitive/MCServerLauncher.Daemon.Plugin.Sdk.props` | `c0dd9844c62950e9cf678c9bb067dd030876afa4d263eedd0d146ce52e5eb895` |
| `buildTransitive/MCServerLauncher.Daemon.Plugin.Sdk.targets` | `e383f4a71ef90a5ad1a25049291c6e877c980d6acac7095ba00778a53f544573` |

Nuspec dependencies are exact:

- `MCServerLauncher.Daemon.API = [2.0.0-preview.2]`
- `Microsoft.Extensions.DependencyInjection = [10.0.9]`

## Preview-1 implemented FeatureCatalog freeze

Grantable and implemented for admission:

- `system.query`
- `instance.query`
- `instance.manage`
- `operation.query`
- `operation.cancel`
- `provisioning.manage`
- `network.http.listen`
- `auth.verify`
- `storage.private`

Host infrastructure also implements `rpc.register` and `event.publish`.
Unimplemented catalog vocabulary remains non-grantable; declaring it causes an
atomic admission skip.

## Consumer pin

```xml
<PackageReference Include="MCServerLauncher.Daemon.Plugin.Sdk" Version="2.0.0-preview.2" />
```

Local-feed restore requires the three nupkgs above and nuget.org for transitive
dependencies.

## Verification

```powershell
dotnet test tests/MCServerLauncher.Daemon.ApiTests/MCServerLauncher.Daemon.ApiTests.csproj -c Release --filter FullyQualifiedName~PackageContract /m:1
dotnet test tests/MCServerLauncher.Daemon.ApiTests/MCServerLauncher.Daemon.ApiTests.csproj -c Release --filter FullyQualifiedName~FeatureCatalogPreview1 /m:1
dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release /m:1
dotnet run --project tools/MCServerLauncher.ProtocolDocs/MCServerLauncher.ProtocolDocs.csproj -- --check
```

Release acceptance additionally runs `.github/workflows/verify-preview1-package-pin.yml`
and the published-host suite with `MCSL_PUBLISHED_DAEMON` pointing to the
downloaded daemon asset and `MCSL_PLUGIN_PACKAGE_SOURCE` pointing to the three
downloaded nupkgs.

Distribution remains GitHub Release assets for all three nupkgs. Public
nuget.org publication is not required for the first accepted pin.
