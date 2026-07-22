using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using RustyOptions;

namespace MCServerLauncher.Daemon.Plugins;

internal sealed class PluginSystemQueryApplication(ISystemApplication system) : ISystemQueryApplication
{
    private readonly ISystemApplication _system = system ?? throw new ArgumentNullException(nameof(system));

    public Task<Result<SystemInfo, DaemonError>> GetSystemInfoAsync(
        CancellationToken cancellationToken) =>
        _system.GetSystemInfoAsync(cancellationToken);

    public Task<Result<JavaRuntimeList, DaemonError>> ListJavaRuntimesAsync(
        CancellationToken cancellationToken) =>
        _system.ListJavaRuntimesAsync(cancellationToken);
}
