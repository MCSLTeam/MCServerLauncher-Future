using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;

namespace MCServerLauncher.Daemon.Management.Installer;

public interface IInstanceInstaller
{
    Task<Result<Unit, Error>> Run(InstanceFactorySetting setting, CancellationToken ct = default);
}