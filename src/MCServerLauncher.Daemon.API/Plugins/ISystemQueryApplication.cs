using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

namespace MCServerLauncher.Daemon.API.Plugins;

/// <summary>
/// Narrow system query surface for plugins that declare <c>system.query</c>.
/// </summary>
public interface ISystemQueryApplication
{
    Task<Result<SystemInfo, DaemonError>> GetSystemInfoAsync(
        CancellationToken cancellationToken);

    Task<Result<JavaRuntimeList, DaemonError>> ListJavaRuntimesAsync(
        CancellationToken cancellationToken);
}
