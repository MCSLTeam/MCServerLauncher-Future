# GitHub Release Workflow Design

## Touched Areas

- `workflow`: new manual GitHub release pipeline for Windows builds and packaged assets.
- `docs`: repository-level release note source file.

## Goal

Add a manual Windows-hosted release workflow that builds and publishes the WPF client and daemon for Windows runtime identifiers, builds the daemon for Linux and macOS runtime identifiers, packages Windows outputs as `.zip` and Linux/macOS outputs as `.tar.gz`, and creates a GitHub Release whose body comes from `Release.md` plus run-specific metadata.

## Architecture

Use a single `workflow_dispatch` workflow with explicit inputs for release behavior. The workflow runs on `windows-latest`, builds the daemon for `win-x64`, `win-x86`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64`, and builds WPF only for the Windows runtime identifiers. Each runtime is published twice, once as self-contained and once as framework-dependent, and the Windows packages bundle both WPF and daemon outputs under the same archive while Linux/macOS packages contain daemon output only.

The publish step is parameterized by a single `include_pdb` boolean that controls whether symbol files are kept in the published payload. After publish, each matrix job creates one archive per runtime and package mode, using `.zip` for Windows and `.tar.gz` for Linux/macOS. A final release job downloads all artifacts, reads `Release.md`, appends a compact package matrix and asset list, and publishes either a normal release or a pre-release depending on the chosen input.

## Release Note Model

`Release.md` is the human-authored source of truth for release prose. It should describe what the release contains in neutral project language, not a single build run. The workflow uses that file as the top of the GitHub Release body and then appends a compact generated section with the exact build mode, runtime identifiers, and uploaded asset names so the release page remains accurate without hand-editing the workflow.

## Files

- Create: `Release.md`
- Create: `.github/workflows/windows-release.yml`

## Validation Strategy

- Keep the workflow limited to Windows-hosted actions and PowerShell so the publish path is easy to reason about.
- Validate YAML and release-note composition by inspecting the workflow diff and running a local PowerShell parse or dry-run against the new release-note file if needed.
- Preserve the existing `build-wpf.yml` workflow as a separate debug helper instead of folding unrelated behavior into the new release pipeline.
