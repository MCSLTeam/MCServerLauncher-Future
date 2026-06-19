using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;

namespace MCServerLauncher.Daemon.Management.Installer;

public sealed class PassthroughInstaller : IInstanceInstaller
{
    public static PassthroughInstaller Instance { get; } = new();

    private PassthroughInstaller()
    {
    }

    public Task<Result<Unit, Error>> Run(InstanceFactorySetting setting, CancellationToken ct = default)
    {
        return Task.FromResult(ResultExt.Ok());
    }
}
