# Changelog

## Unreleased

- Completed the unified local extension-system preview across daemon/API/protocol, Web, and WPF: `.mpx` validation, signed-package trust, local dependency/update metadata, daemon bundle deployment, restart-required tracking, Extension Protocol dispatch, formal Extension Point runtime contracts, install risk review, installed-manifest drift review, daemon audit history, manifest schema support, and a copyable daemon plugin template.
- Completed the startup-only trusted plugin host with deterministic manifest admission, transactional lifecycle ownership, typed health/error/throwing fixtures, and published single-file integration.
- Completed current Extension Point runtime consumers for Java provider contributions, registry contributions, instance-create pipeline stages, instance lifecycle hooks, and process launcher overrides.
- Kept Web/WPF marketplace as shell-only in this preview; online registry/search/download/update resolution, remote dependency download, the full visual plugin designer, and service-manager restart orchestration remain future product tracks.
- Kept `/api/v2` as the sole daemon endpoint and TouchSocket at `4.2.17`.
- Added Daemon API package metadata, dependency-graph validation, supported-RID publish documentation, and a structured performance gate.

## 2026-07-15

- Completed the release-atomic V2 cutover and Phase 5 startup-plugin milestone. Acceptance evidence lived in a working-note plan that was never tracked and no longer exists; `EXECUTE_PLAN.md` carries what survived of it.
