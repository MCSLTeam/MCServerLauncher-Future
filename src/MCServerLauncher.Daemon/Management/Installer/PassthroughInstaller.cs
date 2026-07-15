using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;

namespace MCServerLauncher.Daemon.Management.Installer;

public sealed class PassthroughInstaller : IInstanceInstaller
{
    public static PassthroughInstaller Instance { get; } = new();

    private PassthroughInstaller()
    {
    }

    public Task<Result<Unit, DaemonError>> Run(InstanceFactoryConfiguration setting, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ResultExt.Ok());
    }
}
