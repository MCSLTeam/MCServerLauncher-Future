# MCServerLauncher-Future Guide

Guide for agents working on the MCServerLauncher-Future project.

## Project Overview

**MCServerLauncher-Future** is a C# .NET application suite for managing Minecraft servers and console applications. It consists of a WPF client, a Daemon service, a Daemon client library, and supporting projects.

- **Repository**: MCServerLauncher-Future
- **Language**: C# 14 (.NET 10.0)
- **UI Framework**: WPF (Windows Presentation Foundation) with iNKORE.UI.WPF.Modern
- **Architecture**: Client-Server (WPF Client connects to Daemon via WebSocket using TouchSocket)
- **License**: GPLv3
- **Team**: MCSLTeam
- **Copyright**: © 2022-2026 MCSLTeam

## Essential Commands

```bash
# Build the entire solution
dotnet build MCServerLauncher.sln

# Run tests
dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj

# Run benchmarks
dotnet run --project MCServerLauncher.Benchmarks/MCServerLauncher.Benchmarks.csproj -c Release

# Run the WPF Client
dotnet run --project MCServerLauncher.WPF/MCServerLauncher.WPF.csproj

# Run the Daemon
dotnet run --project MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj

# Publish Daemon (with AOT/trimming)
dotnet publish MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj -c Release -r win-x64 --self-contained

# Publish WPF Client
dotnet publish MCServerLauncher.WPF/MCServerLauncher.WPF.csproj -c Release -r win-x64 --self-contained
```

## Project Structure

```
MCServerLauncher-Future/
├── MCServerLauncher.Common/            # Shared models, utilities, and network code (netstandard2.1)
├── MCServerLauncher.Daemon/            # Background service managing instances (net10.0, AOT-ready)
├── MCServerLauncher.Daemon.Generators/ # Source generators for RPC and serialization (netstandard2.0)
├── MCServerLauncher.DaemonClient/      # Client library for connecting to Daemon (net10.0)
├── MCServerLauncher.WPF/               # Graphical user interface (net10.0-windows)
├── MCServerLauncher.ProtocolTests/     # Protocol and integration tests
├── MCServerLauncher.Benchmarks/        # Performance benchmarks (BenchmarkDotNet)
└── Sign/                               # Code signing utilities
```

## Code Organization

### MCServerLauncher.WPF (net10.0-windows)

- **Translations/**: `.resx` files for internationalization (6 languages supported). Use `Lang.Tr("Key")` for localized strings.
- **Modules/**: Core logic (Initializer, Language, Settings).
- **View/**: WPF XAML pages and components.
- **InstanceConsole/**: Console management for instances.
- **Resources/**: Icons, fonts (Montserrat, SegoeIcons), syntax highlighting definitions.
- **Key Dependencies**: iNKORE.UI.WPF.Modern, AvalonEdit, Serilog, Downloader

### MCServerLauncher.Daemon (net10.0, AOT-ready)

- **Storage/**: File management (`FileManager.cs`, `JavaScanner.cs`, `ContainedFiles.cs`).
- **Management/**: Instance lifecycle and factories.
  - **Factory/**: Instance factories (MCUniversalFactory, MCForgeFactory) with `[InstanceFactory]` attribute.
  - **Installer/**: Installation logic for Minecraft Forge (V1/V2 installers).
  - **Minecraft/**: Minecraft-specific instance handling (`MinecraftInstance.cs`, `PropertiesHandler.cs`).
  - **Communicate/**: Process communication (`InstanceProcess.cs`).
- **Remote/**: WebSocket/HTTP communication.
  - **Action/**: RPC action handlers and executors (source-generated registry).
  - **Event/**: Event triggering and distribution (`EventTriggerService.cs`).
- **Console/**: CLI commands for daemon management (Brigadier.NET).
- **Utils/**: Status helpers, memory info.
- **Contained/**: Embedded resources (server.jar).
- **Key Dependencies**: TouchSocket, Serilog, Brigadier.NET, RustyOptions, System.IdentityModel.Tokens.Jwt

### MCServerLauncher.DaemonClient (net10.0)

- Client library for connecting to Daemon via WebSocket.
- **WebSocketPlugin/**: WebSocket event handling and plugins (`WsReceivedPlugin.cs`).
- Provides `Daemon` class for RPC communication.
- Supports file upload/download with chunking.

### MCServerLauncher.Common (netstandard2.1)

- **ProtoType/**: Shared data structures and enums for Client-Daemon communication.
- Cross-platform compatibility layer.
- Uses both Newtonsoft.Json and System.Text.Json.

### MCServerLauncher.Daemon.Generators (netstandard2.0)

- Roslyn source generators for RPC action registry (`DaemonActionRegistryGenerator.cs`).
- Enables AOT compilation and reduces reflection.
- Generates serialization code for System.Text.Json.

## Naming Conventions & Code Style

**C# 14 (.NET 10.0)**:

- Use PascalCase for classes, methods, properties, and events.
- Use camelCase for local variables and method parameters.
- Use `_camelCase` for private fields.
- Interfaces start with `I` (e.g., `IInstance`, `IInstanceManager`, `IInstanceFactory`).
- Nullable reference types are enabled (`<Nullable>enable</Nullable>`).
- Use `required` modifier for non-nullable properties without constructors (addresses CS8618 warnings).
- Prefer `System.Text.Json` with source generators over `Newtonsoft.Json` for new code.
- Use implicit usings where enabled (`<ImplicitUsings>enable</ImplicitUsings>`).
- Attributes use PascalCase without "Attribute" suffix in usage (e.g., `[InstanceFactory]`).

## Key Concepts

- **Daemon**: Background service managing server instances (Minecraft, Frpc, etc.) with WebSocket/HTTP API.
- **WPF Client**: Graphical interface connecting to Daemons for remote instance management.
- **RPC Communication**: Client-Daemon communication via WebSocket using TouchSocket with action/event pattern.
- **Source Generation**: Uses Roslyn source generators for RPC registry and JSON serialization (AOT-compatible).
- **AOT Compilation**: Daemon supports Native AOT with trimming (`PublishTrimmed`, `JsonSerializerIsReflectionEnabledByDefault=false`).
- **i18n**: WPF client supports 6 languages. Use `Lang.Tr("Key")` for UI text. Keys in `.resx` files (zh-CN, en-US, etc.).
- **Path Handling**: Use `AppDomain.CurrentDomain.BaseDirectory` for absolute paths.
- **Instance Types**: Supports multiple instance types via factory pattern (`IInstanceFactory`, `[InstanceFactory]` attribute).
- **Event System**: Daemon broadcasts events to connected clients via WebSocket (`EventTriggerService`).
- **Forge Installer**: Supports both V1 and V2 Forge installer formats with library dependency resolution.

## Performance Considerations

- Recent work focuses on transport layer optimization (byte-based vs. string-based).
- Serialization uses source-generated System.Text.Json for AOT compatibility.
- Event fan-out serialization optimized to reduce duplication.
- Benchmark baselines tracked in `MCServerLauncher.Benchmarks/` with CI measurement.
- Performance tests use `[PERF]` logging for measurement tracking.
- Object pooling used via `Microsoft.Extensions.ObjectPool`.

## Common Issues

### Config File Structure Changes

If Daemon fails to start with JSON parsing errors, delete outdated config files:

- `./config.json`
- `./daemon/instances/<uuid>/daemon_instance.json`

### Source Generator Warnings

RS2008 warnings about analyzer release tracking can be ignored (non-critical). These appear in `MCServerLauncher.Daemon.Generators`.

### Nullable Reference Type Warnings

Address CS8618 (uninitialized non-nullable properties) by:

- Adding `required` modifier to properties
- Making properties nullable with `?`
- Initializing in constructor

Common locations:

- `MCServerLauncher.Daemon/Management/Installer/MinecraftForge/` JSON models
- `MCServerLauncher.Daemon/Remote/Action/ActionExecutor.cs`

### Switch Expression Exhaustiveness

CS8524 warnings indicate missing enum cases in switch expressions. Add cases for all enum values or use a default case.

## Git & Workflow Notes

- Main branch is `master`.
- Ensure all tests pass and code compiles before committing.
- Run tests: `dotnet test MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj`
- Run benchmarks: `dotnet run --project MCServerLauncher.Benchmarks/MCServerLauncher.Benchmarks.csproj -c Release`
- Check for build warnings, especially nullable reference type warnings.
- Recent commits focus on event metadata handling, serialization optimization, and test quality.

## Testing

- **Protocol Tests**: `MCServerLauncher.ProtocolTests/` - Integration and protocol tests.
- **Benchmarks**: `MCServerLauncher.Benchmarks/` - Performance benchmarks using BenchmarkDotNet.
- Performance baselines are measured in CI and tracked in the repository.

## Documentation

- **Daemon Docs**: `MCServerLauncher.Daemon/.Resources/Docs/`
  - `ws-api.md`: WebSocket API documentation
  - `manual.daemon.md`: Daemon manual
  - `serializer-migration-policy.md`: Serialization migration policy
- **WPF Docs**: `MCServerLauncher.WPF/.Resources/Docs/`
- **README**: Available in English (`README.md`) and Chinese (`README_ZH.md`)

## Related Projects

- **Rust Daemon**: [mcsl-daemon-rs](https://github.com/MCSLTeam/mcsl-daemon-rs/) - Experimental Rust implementation
- **Tauri Launcher**: Cross-platform interface
- **Web Panel**: Browser-accessible dashboard at [MCServerLauncher-Future-Web](https://github.com/MCSLTeam/MCServerLauncher-Future-Web)

## Contact & Contribution

- **Email**: <services@mcsl.com.cn>
- **QQ Group 1**: 733951376
- **QQ Group 2**: 819067131
- **Issues**: [GitHub Issues](https://github.com/MCSLTeam/MCServerLauncher-Future/issues/new/choose)
- **Pull Requests**: [GitHub PRs](https://github.com/MCSLTeam/MCServerLauncher-Future/compare)
- **Internationalization**: [Weblate](https://translate.mcsl.com.cn/engage/mcsl-future/)

## References

- [.NET 10.0 Documentation](https://learn.microsoft.com/en-us/dotnet/)
- [WPF Documentation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
- [TouchSocket Documentation](https://touchsocket.net/)
- [System.Text.Json Documentation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/)
