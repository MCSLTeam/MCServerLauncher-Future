# MCServerLauncher Future Release

This release packages the Windows, Linux, and macOS outputs produced by the GitHub release workflow.

## Package Rules

- Windows packages use `.zip` archives.
- Linux and macOS packages use `.tar.gz` archives.
- Each runtime is published twice: self-contained and framework-dependent.
- Windows packages contain both the WPF client and the daemon.
- Linux and macOS packages contain the daemon only.

## Notes

- This release includes the local extension-system preview: trusted local `.mpx` packages, daemon plugin bundles, signed-package trust review, Extension Protocol dispatch, formal Extension Point runtime contracts, Web/WPF extension-center shell, install risk review, manifest drift review, and extension audit history.
- The marketplace UI remains shell-only. Online registry/search/download/update resolution, remote dependency download, the full visual plugin designer, and service-manager restart orchestration are not part of this release.
- WPF tests require the WindowsDesktop runtime to execute; non-Windows release gates build the WPF test project but cannot run it.
- The GitHub Release body appends the exact package matrix and uploaded asset names for each run.