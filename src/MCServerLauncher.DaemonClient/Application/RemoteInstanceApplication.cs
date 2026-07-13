using System;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using RustyOptions;

namespace MCServerLauncher.DaemonClient.Application;

internal sealed class RemoteInstanceApplication(IRemoteApplicationInvoker invoker) : IInstanceApplication
{
    private readonly IRemoteApplicationInvoker _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));

    public Task<Result<CreateInstanceResult, DaemonError>> CreateInstanceAsync(CreateInstanceRequest request, CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.CreateInstance, request, cancellationToken);

    public Task<Result<Unit, DaemonError>> RemoveInstanceAsync(InstanceReference request, CancellationToken cancellationToken) =>
        _invoker.InvokeUnitAsync(BuiltInProtocolDefinitions.RemoveInstance, request, cancellationToken);

    public Task<Result<Unit, DaemonError>> StartInstanceAsync(InstanceReference request, CancellationToken cancellationToken) =>
        _invoker.InvokeUnitAsync(BuiltInProtocolDefinitions.StartInstance, request, cancellationToken);

    public Task<Result<Unit, DaemonError>> StopInstanceAsync(InstanceReference request, CancellationToken cancellationToken) =>
        _invoker.InvokeUnitAsync(BuiltInProtocolDefinitions.StopInstance, request, cancellationToken);

    public Task<Result<Unit, DaemonError>> HaltInstanceAsync(InstanceReference request, CancellationToken cancellationToken) =>
        _invoker.InvokeUnitAsync(BuiltInProtocolDefinitions.HaltInstance, request, cancellationToken);

    public Task<Result<Unit, DaemonError>> SendCommandAsync(InstanceCommandRequest request, CancellationToken cancellationToken) =>
        _invoker.InvokeUnitAsync(BuiltInProtocolDefinitions.SendInstanceCommand, request, cancellationToken);

    public Task<Result<InstanceReport, DaemonError>> GetInstanceReportAsync(InstanceReference request, CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.GetInstanceReport, request, cancellationToken);

    public Task<Result<InstanceReportList, DaemonError>> ListInstanceReportsAsync(CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.ListInstanceReports, new EmptyRequest(), cancellationToken);

    public Task<Result<InstanceLogResult, DaemonError>> GetInstanceLogAsync(InstanceLogQuery request, CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.GetInstanceLog, request, cancellationToken);

    public Task<Result<InstanceSettingsResult, DaemonError>> GetInstanceSettingsAsync(InstanceReference request, CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.GetInstanceSettings, request, cancellationToken);

    public Task<Result<UpdateInstanceSettingsResult, DaemonError>> UpdateInstanceSettingsAsync(UpdateInstanceSettingsRequest request, CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.UpdateInstanceSettings, request, cancellationToken);
}
