using MCServerLauncher.Common.Contracts.Monitoring;
using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

namespace MCServerLauncher.Daemon.API.Application;

/// <summary>
/// Bounded daemon metrics: the newest sample plus deterministic downsampled range queries over
/// the retained history. Metrics are system-wide observations, not owner-scoped resources.
/// </summary>
public interface IMonitoringApplication
{
    Task<Result<MonitoringCurrentResult, DaemonError>> GetCurrentAsync(
        CancellationToken cancellationToken);

    Task<Result<MonitoringQueryResult, DaemonError>> QueryAsync(
        MonitoringQuery request,
        CancellationToken cancellationToken);
}
