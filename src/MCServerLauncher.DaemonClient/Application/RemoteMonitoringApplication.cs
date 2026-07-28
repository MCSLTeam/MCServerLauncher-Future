using System;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Contracts.Monitoring;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using RustyOptions;

namespace MCServerLauncher.DaemonClient.Application;

internal sealed class RemoteMonitoringApplication(IRemoteApplicationInvoker invoker) : IMonitoringApplication
{
    private readonly IRemoteApplicationInvoker _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));

    public Task<Result<MonitoringCurrentResult, DaemonError>> GetCurrentAsync(
        CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.GetMonitoringCurrent, new EmptyRequest(), cancellationToken);

    public Task<Result<MonitoringQueryResult, DaemonError>> QueryAsync(
        MonitoringQuery request,
        CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.QueryMonitoring, request, cancellationToken);
}
