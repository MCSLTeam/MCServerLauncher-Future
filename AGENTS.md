# MCServerLauncher-Future Guide

Guide for agents working on the MCServerLauncher-Future project.

## Project Overview

**MCServerLauncher-Future** is a C# .NET application suite for managing Minecraft servers. It consists of a WPF client, a Daemon service, and a Daemon client library.

- **Repository**: MCServerLauncher-Future
- **Language**: C# (.NET 10.0)
- **UI Framework**: WPF (Windows Presentation Foundation)
- **Architecture**: Client-Server (WPF Client connects to Daemon via WebSocket)

## Essential Commands

```bash
# Build the entire solution
dotnet build MCServerLauncher.sln

# Run the WPF Client
dotnet run --project MCServerLauncher.WPF/MCServerLauncher.WPF.csproj

# Run the Daemon
dotnet run --project MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj
```

## Project Structure

```
MCServerLauncher-Future/
├── MCServerLauncher.Common/       # Shared models, utilities, and network code
├── MCServerLauncher.Daemon/       # The background service that manages instances
├── MCServerLauncher.DaemonClient/ # Client library for connecting to the Daemon
├── MCServerLauncher.WPF/          # The graphical user interface (Windows only)
└── Sign/                          # Code signing utilities
```

## Code Organization

### MCServerLauncher.WPF
- **Translations/**: Contains `.resx` files for internationalization (i18n). Use `Lang.Tr("Key")` to get localized strings.
- **Modules/**: Core logic modules (e.g., `Initializer.cs`, `Language.cs`, `Settings.cs`).
- **View/**: WPF XAML pages and components.

### MCServerLauncher.Daemon
- **Storage/**: File management and storage logic (e.g., `FileManager.cs`).
- **Management/**: Instance management logic.
- **Remote/**: WebSocket and HTTP communication plugins.

### MCServerLauncher.Common
- **ProtoType/**: Shared data structures and enums used for communication between Client and Daemon.

## Naming Conventions & Code Style

**C#**:
- Use PascalCase for classes, methods, properties, and events.
- Use camelCase for local variables and method parameters.
- Use `_camelCase` for private fields.
- Interfaces should start with `I` (e.g., `IInstance`).

## Key Concepts

- **Daemon**: A background service that runs on the host machine and manages the actual server instances (Minecraft, Frpc, etc.).
- **WPF Client**: A graphical interface that connects to one or more Daemons to manage instances remotely.
- **i18n**: The WPF client supports multiple languages. Always use `Lang.Tr("Key")` for UI text and ensure keys are present in the `.resx` files (especially `Lang.zh-CN.resx` and `Lang.en-US.resx`).
- **Path Handling**: Use `AppDomain.CurrentDomain.BaseDirectory` for absolute paths to ensure consistency regardless of how the application is launched.

## Git & Workflow Notes

- Main branch is `master`.
- Ensure all tests pass and code compiles before committing.

## References

- [.NET Documentation](https://learn.microsoft.com/en-us/dotnet/)
- [WPF Documentation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
