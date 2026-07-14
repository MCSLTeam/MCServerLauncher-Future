using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;

namespace MCServerLauncher.Daemon.Management.Installer;

public interface IInstanceInstaller
{
    Task<Result<Unit, Error>> Run(InstanceFactoryConfiguration setting, CancellationToken ct = default);
}
