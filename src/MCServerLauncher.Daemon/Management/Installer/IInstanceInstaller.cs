using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;

namespace MCServerLauncher.Daemon.Management.Installer;

public interface IInstanceInstaller
{
    Task<Result<Unit, DaemonError>> Run(InstanceFactoryConfiguration setting, CancellationToken ct = default);
}
