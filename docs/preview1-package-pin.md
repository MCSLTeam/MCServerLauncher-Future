# Preview-1 Package Pin

Status: package-affecting source candidate verified twice locally; GitHub Release asset verification is pending.
Branch: `feat/plugin-sdk-2-preview1`
Decision source: `docs/superpowers/specs/2026-07-20-plugin-sdk-mcp-decisions.md`, sections 1, 10, and 12.

## Gate status

This document records the reproducible local `2.0.0-preview.2` candidate from
package-affecting source commit `6148f426`. Two builds with independent pin
roots (`artifacts/_tmp_round4_pin_build_a` and
`artifacts/_tmp_round4_pin_build_b`) and package outputs
(`artifacts/_tmp_round4_pin_a` and `artifacts/_tmp_round4_pin_b`) produced
identical fingerprints for every listed payload. It does not mark the SDK
Preview-1 package gate accepted and does not unblock MCP implementation.
Acceptance requires attaching packages with these exact payloads to a GitHub
Release and verifying every listed payload from the downloaded release assets.

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
| `lib/net10.0/MCServerLauncher.Common.dll` | `968741466a4b74417dab808dc9aa3ab5c3c7e30624a473a781619afac543fd27` |

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
| `lib/net10.0/MCServerLauncher.Daemon.Plugin.Sdk.dll` | `c52be37810be8730f28f6577162b30c838c02c3ca5f2790f6eeea36035f3d0da` |
| `analyzers/dotnet/cs/MCServerLauncher.Daemon.Plugin.Generators.dll` | `c71a42a3bc044be9f2b20fb823d746205fda1680b1e0aaffdbbd3af6187168d2` |
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

Distribution remains GitHub Release assets for all three nupkgs. Public
nuget.org publication is not required for the first accepted pin.
