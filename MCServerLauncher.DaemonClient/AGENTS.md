# MCServerLauncher-Future/MCServerLauncher.DaemonClient Guide

Guide for agents working on the MCServerLauncher.DaemonClient project.

## Project Overview

**MCServerLauncher.DaemonClient** is the client library for connecting to the Daemon. It handles the WebSocket communication, request/response lifecycle, and event subscriptions.

- **Repository**: MCServerLauncher-Future
- **Language**: C# (.NET Standard 2.1)
- **Architecture**: Client Library

## Essential Commands

```bash
# Build the DaemonClient library
dotnet build MCServerLauncher.DaemonClient/MCServerLauncher.DaemonClient.csproj
```

## Project Structure

```
MCServerLauncher.DaemonClient/
├── Connection/      # WebSocket connection management and request handling
├── WebSocketPlugin/ # Plugins for handling specific WebSocket messages
└── Daemon.cs        # Main entry point for interacting with a Daemon
```

## Key Concepts

- **DaemonClient**: A library that abstracts the WebSocket communication with the Daemon. It provides a strongly-typed API for sending actions and receiving events.
- **Connection Management**: Handles connecting, disconnecting, and reconnecting to the Daemon.
- **Request/Response**: Uses a pending request system to map WebSocket responses back to the original asynchronous requests.

## Naming Conventions & Code Style

**C#**:
- Use PascalCase for classes, methods, properties, and events.
- Use camelCase for local variables and method parameters.
- Use `_camelCase` for private fields.

## Git & Workflow Notes

- Ensure all tests pass and code compiles before committing.
- Changes to the communication protocol must be synchronized with `MCServerLauncher.Daemon` and `MCServerLauncher.Common`.

## References

- [.NET Documentation](https://learn.microsoft.com/en-us/dotnet/)
