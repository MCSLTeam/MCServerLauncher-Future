using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.State;
using MCServerLauncher.Daemon.ApplicationCore.Auth;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests;

public sealed class AuthorizedApplicationProxyTests
{
    [Fact]
    public async Task QueryProxyRequiresCallerPermissionBeforeInvokingInnerApplication()
    {
        var inner = new RecordingInstanceQueries();
        var denied = new AuthorizedInstanceQueryApplication(
            new CallerContext("user-a", [], isMainToken: false),
            inner);

        var deniedResult = await denied.ListInstanceReportsAsync(CancellationToken.None);

        Assert.True(deniedResult.IsErr(out var deniedError));
        Assert.Equal("auth.permission_denied", deniedError!.Code);
        Assert.Equal(0, inner.CallCount);

        var allowed = new AuthorizedInstanceQueryApplication(
            new CallerContext(
                "user-a",
                ImmutableArray.Create("mcsl.instance.report.list"),
                isMainToken: false),
            inner);

        var innerResult = await allowed.ListInstanceReportsAsync(CancellationToken.None);

        Assert.True(innerResult.IsErr(out var innerError));
        Assert.Equal("test.inner", innerError!.Code);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public void CatalogProxyChecksPermissionForCurrentAndTryGet()
    {
        var source = new SnapshotSource();
        var denied = new AuthorizedInstanceCatalog(
            new CallerContext("user-a", [], isMainToken: false),
            source);

        Assert.Throws<UnauthorizedAccessException>(() => denied.Current);
        Assert.Throws<UnauthorizedAccessException>(() => denied.TryGet(Guid.NewGuid(), out _));

        var allowed = new AuthorizedInstanceCatalog(
            new CallerContext(
                "user-a",
                ImmutableArray.Create("mcsl.instance.catalog.get"),
                isMainToken: false),
            source);
        Assert.Same(source.Current, allowed.Current);
        Assert.False(allowed.TryGet(Guid.NewGuid(), out _));
    }

    [Fact]
    public async Task OperationFacadesBindDistinctPluginOwnersAndCapabilities()
    {
        var factory = new CallerContextFactory();
        var queryHost = factory.CreateHost(
            new PluginIdentity("community.plugin-a", "1.0.0"),
            ["operation.query"]);
        var controlHost = factory.CreateHost(
            new PluginIdentity("community.plugin-b", "1.0.0"),
            ["operation.cancel"]);
        var inner = new RecordingOperations();

        var query = new AuthorizedOperationQueryApplication(queryHost, inner);
        var control = new AuthorizedOperationControlApplication(controlHost, inner);
        var operationId = Guid.NewGuid();

        _ = await query.ListOperationsAsync(
            new OperationListQuery("plugin:community.plugin-b"),
            CancellationToken.None);
        _ = await query.GetOperationAsync(
            new OperationReference(operationId, "plugin:community.plugin-b"),
            CancellationToken.None);
        _ = await control.CancelOperationAsync(
            new OperationCancelRequest(operationId, "plugin:community.plugin-a"),
            CancellationToken.None);

        Assert.Equal("plugin:community.plugin-a", inner.ListOwner);
        Assert.Equal("plugin:community.plugin-a", inner.GetOwner);
        Assert.Equal("plugin:community.plugin-b", inner.CancelOwner);
        Assert.False(queryHost.HasPermission("mcsl.operation.cancel"));
        Assert.False(controlHost.HasPermission("mcsl.operation.get"));
    }

    [Fact]
    public async Task ProvisioningFacadeOverwritesSpoofedPluginPrincipals()
    {
        var factory = new CallerContextFactory();
        var host = factory.CreateHost(
            new PluginIdentity("community.plugin-a", "1.0.0"),
            ["provisioning.manage"]);
        var inner = new RecordingProvisioning();
        var proxy = new AuthorizedProvisioningApplication(host, inner);
        var planId = Guid.NewGuid();

        _ = await proxy.GetPlanAsync(
            new ProvisioningPlanReference(planId, "plugin:community.plugin-b"),
            CancellationToken.None);
        _ = await proxy.ExecuteAsync(
            new ProvisioningExecuteRequest(planId, "plugin:community.plugin-b"),
            CancellationToken.None);

        Assert.Equal("plugin:community.plugin-a", inner.GetOwner);
        Assert.Equal("plugin:community.plugin-a", inner.Executor);
    }

    private static ValidationDaemonError InnerError() =>
        new("test.inner", "The inner test application was invoked.");

    private sealed class SnapshotSource : IInstanceSnapshotSource
    {
        private readonly StatePublisher<InstanceCatalogSnapshot> _publisher =
            new(InstanceCatalogSnapshot.Empty);

        public PublishedState<InstanceCatalogSnapshot> Current => _publisher.Current;

        public bool TryGet(Guid instanceId, [NotNullWhen(true)] out InstanceSnapshot? snapshot)
        {
            snapshot = null;
            return false;
        }
    }

    private sealed class RecordingInstanceQueries : IInstanceQueryApplication
    {
        public int CallCount { get; private set; }

        public Task<Result<InstanceReport, DaemonError>> GetInstanceReportAsync(
            InstanceReference request,
            CancellationToken cancellationToken) =>
            Invoked<InstanceReport>();

        public Task<Result<InstanceReportList, DaemonError>> ListInstanceReportsAsync(
            CancellationToken cancellationToken) =>
            Invoked<InstanceReportList>();

        public Task<Result<InstanceLogResult, DaemonError>> GetInstanceLogAsync(
            InstanceLogQuery request,
            CancellationToken cancellationToken) =>
            Invoked<InstanceLogResult>();

        public Task<Result<InstanceSettingsResult, DaemonError>> GetInstanceSettingsAsync(
            InstanceReference request,
            CancellationToken cancellationToken) =>
            Invoked<InstanceSettingsResult>();

        private Task<Result<T, DaemonError>> Invoked<T>() where T : notnull
        {
            CallCount++;
            return Task.FromResult(Result.Err<T, DaemonError>(InnerError()));
        }
    }

    private sealed class RecordingOperations : IOperationApplication
    {
        public string? ListOwner { get; private set; }
        public string? GetOwner { get; private set; }
        public string? CancelOwner { get; private set; }

        public Task<Result<OperationListResult, DaemonError>> ListOperationsAsync(
            OperationListQuery request,
            CancellationToken cancellationToken)
        {
            ListOwner = request.OwnerPrincipal;
            return Task.FromResult(Result.Err<OperationListResult, DaemonError>(InnerError()));
        }

        public Task<Result<OperationSnapshot, DaemonError>> GetOperationAsync(
            OperationReference request,
            CancellationToken cancellationToken)
        {
            GetOwner = request.OwnerPrincipal;
            return Task.FromResult(Result.Err<OperationSnapshot, DaemonError>(InnerError()));
        }

        public Task<Result<OperationCancelResult, DaemonError>> CancelOperationAsync(
            OperationCancelRequest request,
            CancellationToken cancellationToken)
        {
            CancelOwner = request.OwnerPrincipal;
            return Task.FromResult(Result.Err<OperationCancelResult, DaemonError>(InnerError()));
        }
    }

    private sealed class RecordingProvisioning : IProvisioningApplication
    {
        public string? GetOwner { get; private set; }
        public string? Executor { get; private set; }

        public Task<Result<ProvisioningPlanSnapshot, DaemonError>> ResolveAsync(
            ProvisioningResolveRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result.Err<ProvisioningPlanSnapshot, DaemonError>(InnerError()));

        public Task<Result<ProvisioningPlanSnapshot, DaemonError>> GetPlanAsync(
            ProvisioningPlanReference request,
            CancellationToken cancellationToken)
        {
            GetOwner = request.OwnerPrincipal;
            return Task.FromResult(Result.Err<ProvisioningPlanSnapshot, DaemonError>(InnerError()));
        }

        public Task<Result<ProvisioningExecuteResult, DaemonError>> ExecuteAsync(
            ProvisioningExecuteRequest request,
            CancellationToken cancellationToken)
        {
            Executor = request.ExecutorPrincipal;
            return Task.FromResult(Result.Err<ProvisioningExecuteResult, DaemonError>(InnerError()));
        }
    }
}
