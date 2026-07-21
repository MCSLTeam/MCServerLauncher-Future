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

## SHA-256 (Release pack)

Produced by:

```powershell
dotnet pack src/MCServerLauncher.Common/MCServerLauncher.Common.csproj -c Release -o artifacts/preview1-packages /m:1
dotnet pack src/MCServerLauncher.Daemon.API/MCServerLauncher.Daemon.API.csproj -c Release -o artifacts/preview1-packages /m:1
dotnet pack src/MCServerLauncher.Daemon.Plugin.Sdk/MCServerLauncher.Daemon.Plugin.Sdk.csproj -c Release -o artifacts/preview1-packages /m:1
```

| File | SHA-256 | Bytes |
|---|---|---|
| `MCServerLauncher.Common.1.0.0.nupkg` | `bbb6613c0fcd7e2c85ee5f188b67140515832477ddb12b498d87a184c23e6d5f` | 268977 |
| `MCServerLauncher.Daemon.API.2.0.0-preview.1.nupkg` | `a271f5e3289f54c974c2c4ceb60fe4ca8f8b1de9c2e99052b0a9d0a8f37313fb` | 43414 |
| `MCServerLauncher.Daemon.Plugin.Sdk.2.0.0-preview.1.nupkg` | `91b55d3ceebb344ab4b4ea0c839177310ba74d2216b2c844de677ce9fb2992a1` | 19077 |

Hashes above were computed from a Release pack after exact Daemon.API dependency pinning. Re-pack and replace hashes only when package contents or package metadata intentionally change.

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

Also implemented host infrastructure features (not business MCP tools, but generator/host surfaces):

- `rpc.register`
- `event.publish`

Unimplemented features remain catalog vocabulary only; declaring them causes atomic admission skip.

## MCP pin snippet

```xml
<PackageReference Include="MCServerLauncher.Daemon.Plugin.Sdk" Version="2.0.0-preview.1" />
```

Local feed restore requires the three nupkgs above plus nuget.org for transitive BCL packages (`RustyOptions`, `Microsoft.Extensions.*`).

## Verification

```powershell
dotnet test tests/MCServerLauncher.Daemon.ApiTests/MCServerLauncher.Daemon.ApiTests.csproj -c Release --filter FullyQualifiedName~PackageContract /m:1
dotnet test tests/MCServerLauncher.Daemon.ApiTests/MCServerLauncher.Daemon.ApiTests.csproj -c Release --filter FullyQualifiedName~FeatureCatalogPreview1 /m:1
dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release /m:1
dotnet run --project tools/MCServerLauncher.ProtocolDocs/MCServerLauncher.ProtocolDocs.csproj -- --check
```

Distribution: attach the three nupkgs as GitHub Release assets (decision §12). nuget.org public preview is not required for the first accepted pin.
