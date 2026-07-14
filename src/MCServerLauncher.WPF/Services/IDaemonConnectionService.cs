using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.WPF.Modules;
using RustyOptions;
using TypedDaemonClient = MCServerLauncher.DaemonClient.DaemonClient;

namespace MCServerLauncher.WPF.Services;

public interface IDaemonConnectionService
{
    Task<Result<TypedDaemonClient, DaemonError>> GetAsync(
        Constants.DaemonConfigModel config,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, DaemonError>> RemoveAsync(
        Constants.DaemonConfigModel config,
        CancellationToken cancellationToken = default);
}
