# MCServerLauncher-Future/MCServerLauncher.Common Guide

Guide for agents working on the MCServerLauncher.Common project.

## Project Overview

**MCServerLauncher.Common** contains shared models, utilities, and network code used by both the WPF Client and the Daemon.

- **Repository**: MCServerLauncher-Future
- **Language**: C# (.NET Standard 2.1)
- **Architecture**: Shared Library

## Essential Commands

```bash
# Build the Common library
dotnet build MCServerLauncher.Common/MCServerLauncher.Common.csproj
```

## Project Structure

```
MCServerLauncher.Common/
├── Concurrent/      # Concurrency utilities (e.g., RwLock)
├── Helpers/         # Extension methods and helper classes
├── Network/         # Shared network utilities (e.g., SlpClient)
├── ProtoType/       # Shared data structures and enums for communication
└── Utils/           # General utilities
```

## Key Concepts

- **ProtoType**: This directory contains the core data structures (Actions, Events, Status) that define the communication protocol between the Client and the Daemon. Any changes here must be carefully considered as they affect both sides.
- **Shared Utilities**: Provides common functionality like path handling, date/time extensions, and concurrency primitives.

## Naming Conventions & Code Style

**C#**:
- Use PascalCase for classes, methods, properties, and events.
- Use camelCase for local variables and method parameters.
- Use `_camelCase` for private fields.

## Git & Workflow Notes

- Ensure all tests pass and code compiles before committing.
- Because this is a shared library, changes here can have widespread impacts across the entire solution.

## References

- [.NET Documentation](https://learn.microsoft.com/en-us/dotnet/)
