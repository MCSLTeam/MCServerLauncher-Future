using System.Diagnostics.CodeAnalysis;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.State;
using RustyOptions;

namespace MCServerLauncher.Daemon.API.Application;

public sealed class AuthorizedInstanceCatalog(
    ICallerContext caller,
    IInstanceSnapshotSource inner) : IInstanceSnapshotSource
{
    private readonly ICallerContext _caller = caller ?? throw new ArgumentNullException(nameof(caller));
    private readonly IInstanceSnapshotSource _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public PublishedState<InstanceCatalogSnapshot> Current
    {
        get
        {
            EnsurePermission("mcsl.instance.catalog.get");
            return _inner.Current;
        }
    }

    public bool TryGet(Guid instanceId, [NotNullWhen(true)] out InstanceSnapshot? snapshot)
    {
        EnsurePermission("mcsl.instance.catalog.get");
        return _inner.TryGet(instanceId, out snapshot);
    }

    private void EnsurePermission(string method)
    {
        var permission = _caller.EnsurePermission(method);
        if (permission.IsErr(out var error))
            throw new UnauthorizedAccessException(error!.Message);
    }
}

public sealed class AuthorizedInstanceQueryApplication(
    ICallerContext caller,
    IInstanceQueryApplication inner) : IInstanceQueryApplication
{
    private readonly ICallerContext _caller = caller ?? throw new ArgumentNullException(nameof(caller));
    private readonly IInstanceQueryApplication _inner = inner ?? throw new ArgumentNullException(nameof(inner));

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

    private Task<Result<T, DaemonError>> Guard<T>(
        string method,
        Func<Task<Result<T, DaemonError>>> action)
        where T : notnull =>
        AuthorizedApplicationGuard.Invoke(_caller, method, action);
}

public sealed class AuthorizedInstanceManagementApplication(
    ICallerContext caller,
    IInstanceManagementApplication inner) : IInstanceManagementApplication
{
    private readonly ICallerContext _caller = caller ?? throw new ArgumentNullException(nameof(caller));
    private readonly IInstanceManagementApplication _inner = inner ?? throw new ArgumentNullException(nameof(inner));

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

    public Task<Result<UpdateInstanceSettingsResult, DaemonError>> UpdateInstanceSettingsAsync(
        UpdateInstanceSettingsRequest request,
        CancellationToken cancellationToken) =>
        Guard("mcsl.instance.settings.update", () => _inner.UpdateInstanceSettingsAsync(request, cancellationToken));

    private Task<Result<T, DaemonError>> Guard<T>(
        string method,
        Func<Task<Result<T, DaemonError>>> action)
        where T : notnull =>
        AuthorizedApplicationGuard.Invoke(_caller, method, action);
}

public sealed class AuthorizedSystemQueryApplication(
    ICallerContext caller,
    ISystemQueryApplication inner) : ISystemQueryApplication
{
    private readonly ICallerContext _caller = caller ?? throw new ArgumentNullException(nameof(caller));
    private readonly ISystemQueryApplication _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public Task<Result<SystemInfo, DaemonError>> GetSystemInfoAsync(
        CancellationToken cancellationToken) =>
        Guard("mcsl.system.info.get", () => _inner.GetSystemInfoAsync(cancellationToken));

    public Task<Result<JavaRuntimeList, DaemonError>> ListJavaRuntimesAsync(
        CancellationToken cancellationToken) =>
        Guard("mcsl.java.list", () => _inner.ListJavaRuntimesAsync(cancellationToken));

    private Task<Result<T, DaemonError>> Guard<T>(
        string method,
        Func<Task<Result<T, DaemonError>>> action)
        where T : notnull =>
        AuthorizedApplicationGuard.Invoke(_caller, method, action);
}

public sealed class AuthorizedOperationQueryApplication(
    ICallerContext caller,
    IOperationQueryApplication inner) : IOperationQueryApplication
{
    private readonly ICallerContext _caller = caller ?? throw new ArgumentNullException(nameof(caller));
    private readonly IOperationQueryApplication _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public Task<Result<OperationListResult, DaemonError>> ListOperationsAsync(
        OperationListQuery request,
        CancellationToken cancellationToken)
    {
        var permission = _caller.EnsurePermission("mcsl.operation.list");
        if (permission.IsErr(out var error))
            return Task.FromResult(Result.Err<OperationListResult, DaemonError>(error!));
        return _inner.ListOperationsAsync(
            request with { OwnerPrincipal = ResolveOwnerPrincipal() },
            cancellationToken);
    }

    public Task<Result<OperationSnapshot, DaemonError>> GetOperationAsync(
        OperationReference request,
        CancellationToken cancellationToken)
    {
        var permission = _caller.EnsurePermission("mcsl.operation.get");
        if (permission.IsErr(out var error))
            return Task.FromResult(Result.Err<OperationSnapshot, DaemonError>(error!));
        return _inner.GetOperationAsync(
            request with { OwnerPrincipal = ResolveOwnerPrincipal() },
            cancellationToken);
    }

    private string ResolveOwnerPrincipal() =>
        _caller.IsMainToken ? "*" : _caller.Subject;
}

public sealed class AuthorizedOperationControlApplication(
    ICallerContext caller,
    IOperationControlApplication inner) : IOperationControlApplication
{
    private readonly ICallerContext _caller = caller ?? throw new ArgumentNullException(nameof(caller));
    private readonly IOperationControlApplication _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public Task<Result<OperationCancelResult, DaemonError>> CancelOperationAsync(
        OperationCancelRequest request,
        CancellationToken cancellationToken)
    {
        var permission = _caller.EnsurePermission("mcsl.operation.cancel");
        if (permission.IsErr(out var error))
            return Task.FromResult(Result.Err<OperationCancelResult, DaemonError>(error!));
        return _inner.CancelOperationAsync(
            request with { OwnerPrincipal = _caller.IsMainToken ? "*" : _caller.Subject },
            cancellationToken);
    }
}

public sealed class AuthorizedProvisioningApplication(
    ICallerContext caller,
    IProvisioningApplication inner) : IProvisioningApplication
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
        return _inner.ResolveAsync(
            request with { CreatorPrincipal = _caller.Subject },
            cancellationToken);
    }

    public Task<Result<ProvisioningPlanSnapshot, DaemonError>> GetPlanAsync(
        ProvisioningPlanReference request,
        CancellationToken cancellationToken)
    {
        var permission = _caller.EnsurePermission("mcsl.provisioning.get");
        if (permission.IsErr(out var error))
            return Task.FromResult(Result.Err<ProvisioningPlanSnapshot, DaemonError>(error!));
        return _inner.GetPlanAsync(
            request with { OwnerPrincipal = _caller.Subject },
            cancellationToken);
    }

    public Task<Result<ProvisioningExecuteResult, DaemonError>> ExecuteAsync(
        ProvisioningExecuteRequest request,
        CancellationToken cancellationToken)
    {
        var permission = _caller.EnsurePermission("mcsl.provisioning.execute");
        if (permission.IsErr(out var error))
            return Task.FromResult(Result.Err<ProvisioningExecuteResult, DaemonError>(error!));
        return _inner.ExecuteAsync(
            request with { ExecutorPrincipal = _caller.Subject },
            cancellationToken);
    }
}

internal static class AuthorizedApplicationGuard
{
    internal static Task<Result<T, DaemonError>> Invoke<T>(
        ICallerContext caller,
        string method,
        Func<Task<Result<T, DaemonError>>> action)
        where T : notnull
    {
        var permission = caller.EnsurePermission(method);
        return permission.IsErr(out var error)
            ? Task.FromResult(Result.Err<T, DaemonError>(error!))
            : action();
    }
}
