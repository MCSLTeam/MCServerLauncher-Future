# MCServerLauncher-Future/MCServerLauncher.Daemon Guide

Guide for agents working on the MCServerLauncher.Daemon project.

## Project Overview

**MCServerLauncher.Daemon** is the background service that runs on the host machine and manages the actual server instances (Minecraft, Frpc, etc.).

- **Repository**: MCServerLauncher-Future
- **Language**: C# (.NET 10.0)
- **Architecture**: Server (Accepts WebSocket connections from Clients)

## Essential Commands

```bash
# Run the Daemon
dotnet run --project MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj

# Build the Daemon
dotnet build MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj
```

## Project Structure

```
MCServerLauncher.Daemon/
├── Storage/         # File management and storage logic (e.g., FileManager.cs)
├── Management/      # Instance management logic (e.g., InstanceManager.cs)
├── Remote/          # WebSocket and HTTP communication plugins
├── Console/         # Command-line interface logic for the Daemon itself
└── Utils/           # Utility classes and helpers
```

## Key Concepts

- **Daemon**: A background service that manages instances. It handles file operations, process execution, and status monitoring.
- **Path Handling**: Use `AppDomain.CurrentDomain.BaseDirectory` for absolute paths. When validating paths (e.g., in `FileManager`), ensure both relative and absolute paths are handled securely to prevent directory traversal attacks.
- **WebSocket Plugins**: Communication with the client is handled via various WebSocket plugins in the `Remote/` directory.

## Naming Conventions & Code Style

**C#**:
- Use PascalCase for classes, methods, properties, and events.
- Use camelCase for local variables and method parameters.
- Use `_camelCase` for private fields.
- Interfaces should start with `I` (e.g., `IInstance`).

## Git & Workflow Notes

- Ensure all tests pass and code compiles before committing.
- Pay special attention to file system security and path validation.

## References

- [.NET Documentation](https://learn.microsoft.com/en-us/dotnet/)
