using MCServerLauncher.Common.Contracts.Monitoring;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

namespace MCServerLauncher.Daemon.ApplicationCore.Monitoring;

internal sealed class LocalMonitoringApplication(MonitoringSampler sampler) : IMonitoringApplication
{
    public Task<Result<MonitoringCurrentResult, DaemonError>> GetCurrentAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Ok<MonitoringCurrentResult, DaemonError>(
            new MonitoringCurrentResult(sampler.Latest, sampler.DroppedRecords)));
    }

    public Task<Result<MonitoringQueryResult, DaemonError>> QueryAsync(
        MonitoringQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Ok<MonitoringQueryResult, DaemonError>(sampler.Query(request)));
    }
}
