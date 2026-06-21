# MCServerLauncher Future Release

This release packages the Windows, Linux, and macOS outputs produced by the GitHub release workflow.

## Package Rules

- Windows packages use `.zip` archives.
- Linux and macOS packages use `.tar.gz` archives.
- Each runtime is published twice: self-contained and framework-dependent.
- Windows packages contain both the WPF client and the daemon.
- Linux and macOS packages contain the daemon only.

## Notes

- The GitHub Release body appends the exact package matrix and uploaded asset names for each run.