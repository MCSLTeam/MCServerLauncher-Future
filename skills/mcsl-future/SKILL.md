---
name: mcsl-future
description: Guides work that changes or reviews MCServerLauncher Future domain behavior. Use for daemon protocol, instance lifecycle, file/path handling, installers, serialization, WPF instance submission, or event semantics, not ordinary docs-only edits.
---

# MCServerLauncher Future

## Quick Start

1. Read `PROJECT_PLAN.md`.
2. Read `RULES.md`.
3. Declare touched areas.
4. Preserve the invariants below.
5. Run the smallest relevant verification.

## Invariants

- Client-daemon communication uses action/event protocol over WebSocket.
- Shared wire contracts live in `src/MCServerLauncher.Common`.
- Daemon serialization must stay `System.Text.Json`, source-generation friendly, and AOT/trim compatible.
- Instance creation remains factory-driven through `IInstanceFactory` and `[InstanceFactory]`.
- WPF user-facing text uses `Lang.Tr[...]`.
- Daemon path handling validates trust boundaries before file access.

## Task Recipes

**Protocol or serialization.** Start in `src/MCServerLauncher.Common`, then update daemon, daemon client, WPF, protocol tests, and benchmarks as needed.

**Instance lifecycle.** Preserve cancellation support, `Result<T, Error>` failure paths, and daemon-side authority for state changes.

**Installer changes.** Keep mirror behavior explicit, preserve Forge-family format differences, and validate cached/downloaded libraries when checksums exist.

**WPF create-instance flows.** Validate form values before submission, but keep daemon-side validation authoritative.

**Events.** Preserve meta/data semantics, subscription recovery, and event fan-out behavior.

## Red Flags

- Frontend-only validation becomes the only security or path-safety check.
- Reflection is added to daemon hot paths or AOT-sensitive paths without a trim boundary.
- A protocol field changes without protocol tests.
- Domain vocabulary changes without updating `PROJECT_PLAN.md` and `RULES.md`.

## Verification

- Docs: inspect Markdown and run terminology searches.
- WPF: `dotnet build src/MCServerLauncher.WPF/MCServerLauncher.WPF.csproj /m:1`
- Daemon: `dotnet build src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj /m:1`
- Protocol: `dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj /m:1`
- Final: `git diff --check`

## Commits

Use `type(scope): subject`.
