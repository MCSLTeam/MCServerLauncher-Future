# MCServerLauncher Daemon

The daemon is the local implementation of the transport-neutral application contracts. It serves the authenticated `/api/v2` WebSocket endpoint and owns instance, file, system, event-rule, and startup-plugin behavior.

For configuration, authentication, publish profiles, and operational shutdown, see [the daemon manual](../../docs/daemon-manual.md).

The daemon package is published as an untrimmed JIT single-file application. Trusted plugin bundles live in the `plugins/` sidecar directory next to the published executable.
