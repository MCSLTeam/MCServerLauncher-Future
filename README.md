# MCServerLauncher Future

MCServerLauncher Future manages Minecraft servers and other console applications through a daemon and client applications. The repository contains the .NET daemon, the WPF connection-layer client, shared wire contracts, the packable Daemon API, and protocol tests.

[![GPLv3](https://img.shields.io/badge/License-GPLv3-blue)](LICENSE)

## Architecture

- The daemon exposes one authenticated `/api/v2` WebSocket endpoint using typed JSON-RPC and versioned binary transfer frames.
- `src/MCServerLauncher.Daemon.API` is the transport-neutral NuGet boundary for application, protocol, state, error, and startup-plugin contracts.
- `src/MCServerLauncher.DaemonClient` implements the remote application and typed event APIs.
- `src/MCServerLauncher.WPF` is the Windows desktop client and uses the daemon client connection layer.
- Startup plugins are trusted, startup-only sidecars. They may register typed RPCs, publish typed events, and read immutable instance snapshots. They do not receive TouchSocket, MessagePipe, Serilog, or daemon implementation types.

The plugin-enabled daemon is an untrimmed JIT single-file host with sidecar plugin bundles. Native AOT and `PublishTrimmed=true` are not supported product configurations.

## Build And Test

The repository targets .NET 10.

```powershell
dotnet build MCServerLauncher.slnx /m:1
dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release /m:1
dotnet test tests/MCServerLauncher.Daemon.ApiTests/MCServerLauncher.Daemon.ApiTests.csproj -c Release /m:1
```

Run the daemon or WPF client from their project paths:

```powershell
dotnet run --project src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj
dotnet run --project src/MCServerLauncher.WPF/MCServerLauncher.WPF.csproj
```

## Plugin SDK

See [the plugin developer guide](docs/plugin-developer-guide.md) for the manifest, capability declarations, lifecycle rules, and sidecar publish layout. The public API package can be packed with:

```powershell
dotnet pack src/MCServerLauncher.Daemon.API/MCServerLauncher.Daemon.API.csproj -c Release -o artifacts/packages
```

## Release Documentation

- [Daemon manual](docs/daemon-manual.md)
- [Third-party notices and license inventory](docs/THIRD-PARTY-NOTICES.md)
- [Release workflow notes](Release.md)
- [Changelog](CHANGELOG.md)

The WPF client requires the .NET Desktop Runtime 10.x. Framework-dependent daemon packages require the .NET Runtime 10.x; self-contained packages include the runtime.

## Related Clients

- [Web frontend](https://github.com/MCSLTeam/MCServerLauncher-Future-Web)
- [Rust daemon experiment](https://github.com/MCSLTeam/mcsl-daemon-rs/)

## Contributing

Open an issue or pull request for bugs and improvements. Domain and protocol changes must preserve the rules in [PROJECT_PLAN.md](PROJECT_PLAN.md), [RULES.md](RULES.md), and [AGENTS.md](AGENTS.md).

## License

MCServerLauncher Future is distributed under the [GNU General Public License v3.0](LICENSE). Third-party package notices are recorded in [docs/THIRD-PARTY-NOTICES.md](docs/THIRD-PARTY-NOTICES.md).
