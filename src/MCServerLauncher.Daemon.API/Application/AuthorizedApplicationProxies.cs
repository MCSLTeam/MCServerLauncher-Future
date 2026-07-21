using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

namespace MCServerLauncher.Daemon.API.Application;

/// <summary>
/// Permission-checking proxy for instance application methods.
/// Holds an <see cref="ICallerContext"/>; public app methods do not take the context as a parameter.
/// </summary>
public sealed class AuthorizedInstanceApplication(ICallerContext caller, IInstanceApplication inner) : IInstanceApplication
{
    private readonly ICallerContext _caller = caller ?? throw new ArgumentNullException(nameof(caller));
    private readonly IInstanceApplication _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public Task<Result<CreateInstanceResult, DaemonError>> CreateInstanceAsync(
        CreateInstanceRequest request,
        CancellationToken cancellationToken) =>
        Guard("mcsl.instance.create", () => _inner.CreateInstanceAsync(request, cancellationToken));

    public Task<Result<Unit, DaemonError>> RemoveInstanceAsync(
        InstanceReference request,
        CancellationToken cancellationToken) =>
        Guard("mcsl.instance.remove", () => _inner.RemoveInstanceAsync(request, cancellationToken));

    public Task<Result<Unit, DaemonError>> StartInstanceAsync(
        InstanceReference request,
        CancellationToken cancellationToken) =>
        Guard("mcsl.instance.start", () => _inner.StartInstanceAsync(request, cancellationToken));

    public Task<Result<Unit, DaemonError>> StopInstanceAsync(
        InstanceReference request,
        CancellationToken cancellationToken) =>
        Guard("mcsl.instance.stop", () => _inner.StopInstanceAsync(request, cancellationToken));

    public Task<Result<Unit, DaemonError>> HaltInstanceAsync(
        InstanceReference request,
        CancellationToken cancellationToken) =>
        Guard("mcsl.instance.halt", () => _inner.HaltInstanceAsync(request, cancellationToken));

    public Task<Result<Unit, DaemonError>> SendCommandAsync(
        InstanceCommandRequest request,
        CancellationToken cancellationToken) =>
        Guard("mcsl.instance.command.send", () => _inner.SendCommandAsync(request, cancellationToken));

    public Task<Result<InstanceReport, DaemonError>> GetInstanceReportAsync(
        InstanceReference request,
        CancellationToken cancellationToken) =>
        Guard("mcsl.instance.report.get", () => _inner.GetInstanceReportAsync(request, cancellationToken));

    public Task<Result<InstanceReportList, DaemonError>> ListInstanceReportsAsync(
        CancellationToken cancellationToken) =>
        Guard("mcsl.instance.report.list", () => _inner.ListInstanceReportsAsync(cancellationToken));

    public Task<Result<InstanceLogResult, DaemonError>> GetInstanceLogAsync(
        InstanceLogQuery request,
        CancellationToken cancellationToken) =>
        Guard("mcsl.instance.log.get", () => _inner.GetInstanceLogAsync(request, cancellationToken));

    public Task<Result<InstanceSettingsResult, DaemonError>> GetInstanceSettingsAsync(
        InstanceReference request,
        CancellationToken cancellationToken) =>
        Guard("mcsl.instance.settings.get", () => _inner.GetInstanceSettingsAsync(request, cancellationToken));

    public Task<Result<UpdateInstanceSettingsResult, DaemonError>> UpdateInstanceSettingsAsync(
        UpdateInstanceSettingsRequest request,
        CancellationToken cancellationToken) =>
        Guard("mcsl.instance.settings.update", () => _inner.UpdateInstanceSettingsAsync(request, cancellationToken));

    private Task<Result<T, DaemonError>> Guard<T>(string method, Func<Task<Result<T, DaemonError>>> action)
        where T : notnull
    {
        var permission = _caller.EnsurePermission(method);
        if (permission.IsErr(out var error))
            return Task.FromResult(Result.Err<T, DaemonError>(error!));
        return action();
    }
}

/// <summary>
/// Permission-checking proxy for operation query/control methods.
/// </summary>
public sealed class AuthorizedOperationApplication(ICallerContext caller, IOperationApplication inner) : IOperationApplication
{
    private readonly ICallerContext _caller = caller ?? throw new ArgumentNullException(nameof(caller));
    private readonly IOperationApplication _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public Task<Result<OperationListResult, DaemonError>> ListOperationsAsync(
        OperationListQuery request,
        CancellationToken cancellationToken)
    {
        var permission = _caller.EnsurePermission("mcsl.operation.list");
        if (permission.IsErr(out var error))
            return Task.FromResult(Result.Err<OperationListResult, DaemonError>(error!));
        var bound = BindOwner(request with { });
        return _inner.ListOperationsAsync(bound, cancellationToken);
    }

    public Task<Result<OperationSnapshot, DaemonError>> GetOperationAsync(
        OperationReference request,
        CancellationToken cancellationToken)
    {
        var permission = _caller.EnsurePermission("mcsl.operation.get");
        if (permission.IsErr(out var error))
            return Task.FromResult(Result.Err<OperationSnapshot, DaemonError>(error!));
        var bound = request with { OwnerPrincipal = ResolveOwnerPrincipal() };
        return _inner.GetOperationAsync(bound, cancellationToken);
    }

    public Task<Result<OperationCancelResult, DaemonError>> CancelOperationAsync(
        OperationCancelRequest request,
        CancellationToken cancellationToken)
    {
        var permission = _caller.EnsurePermission("mcsl.operation.cancel");
        if (permission.IsErr(out var error))
            return Task.FromResult(Result.Err<OperationCancelResult, DaemonError>(error!));
        var bound = request with { OwnerPrincipal = ResolveOwnerPrincipal() };
        return _inner.CancelOperationAsync(bound, cancellationToken);
    }

    private OperationListQuery BindOwner(OperationListQuery request) =>
        request with { OwnerPrincipal = ResolveOwnerPrincipal() };

    private string ResolveOwnerPrincipal() =>
        _caller.IsMainToken ? "*" : _caller.Subject;
}

/// <summary>
/// Permission-checking proxy for provisioning methods.
/// </summary>
public sealed class AuthorizedProvisioningApplication(ICallerContext caller, IProvisioningApplication inner)
    : IProvisioningApplication
{
    private readonly ICallerContext _caller = caller ?? throw new ArgumentNullException(nameof(caller));
    private readonly IProvisioningApplication _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public Task<Result<ProvisioningPlanSnapshot, DaemonError>> ResolveAsync(
        ProvisioningResolveRequest request,
        CancellationToken cancellationToken)
    {
        var permission = _caller.EnsurePermission("mcsl.provisioning.resolve");
        if (permission.IsErr(out var error))
            return Task.FromResult(Result.Err<ProvisioningPlanSnapshot, DaemonError>(error!));
        var bound = request with { CreatorPrincipal = _caller.Subject };
        return _inner.ResolveAsync(bound, cancellationToken);
    }

    public Task<Result<ProvisioningPlanSnapshot, DaemonError>> GetPlanAsync(
        ProvisioningPlanReference request,
        CancellationToken cancellationToken)
    {
        var permission = _caller.EnsurePermission("mcsl.provisioning.get");
        if (permission.IsErr(out var error))
            return Task.FromResult(Result.Err<ProvisioningPlanSnapshot, DaemonError>(error!));
        var bound = request with { OwnerPrincipal = _caller.Subject };
        return _inner.GetPlanAsync(bound, cancellationToken);
    }

    public Task<Result<ProvisioningExecuteResult, DaemonError>> ExecuteAsync(
        ProvisioningExecuteRequest request,
        CancellationToken cancellationToken)
    {
        var permission = _caller.EnsurePermission("mcsl.provisioning.execute");
        if (permission.IsErr(out var error))
            return Task.FromResult(Result.Err<ProvisioningExecuteResult, DaemonError>(error!));
        var bound = request with { ExecutorPrincipal = _caller.Subject };
        return _inner.ExecuteAsync(bound, cancellationToken);
    }
}
