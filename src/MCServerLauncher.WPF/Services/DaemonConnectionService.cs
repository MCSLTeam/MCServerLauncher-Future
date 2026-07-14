using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.WPF.Modules;
using RustyOptions;
using TypedDaemonClient = MCServerLauncher.DaemonClient.DaemonClient;

namespace MCServerLauncher.WPF.Services;

public class DaemonConnectionService : IDaemonConnectionService
{
    public Task<Result<TypedDaemonClient, DaemonError>> GetAsync(
        Constants.DaemonConfigModel config,
        CancellationToken cancellationToken = default)
    {
        return DaemonsWsManager.Get(config, cancellationToken);
    }

    public Task<Result<Unit, DaemonError>> RemoveAsync(
        Constants.DaemonConfigModel config,
        CancellationToken cancellationToken = default)
    {
        return DaemonsWsManager.Remove(config, cancellationToken);
    }
}
