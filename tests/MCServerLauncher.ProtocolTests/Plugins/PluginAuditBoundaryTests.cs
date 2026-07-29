using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.ApplicationCore.Audit;
using MCServerLauncher.Daemon.ApplicationCore.Auth;
using MCServerLauncher.Daemon.Plugins;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests.Plugins;

/// <summary>
/// V2RpcDispatcher.RecordAudit never runs for plugin-originated calls (PluginApplicationAuthorizer
/// is never on the RPC dispatch path), so the audit decorators wired into
/// PluginApplicationAuthorizer.Create are the only place that records them. These tests exercise
/// that boundary directly, independent of PluginHost's IPC/lifecycle plumbing.
/// </summary>
public sealed class PluginAuditBoundaryTests
{
    private static readonly Guid InstanceId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task AuditedMutatingCall_ViaHost_RecordsPrincipalPluginMethodAndOutcomeExactlyOnce()
    {
        var sink = new RecordingAuditSink();
        var caller = new CallerContext("plugin:community.audit-test", [], isMainToken: true);
        var decorated = new AuditedInstanceManagementApplication(
            new FakeInstanceManagement(RemoveResult: Result.Ok<Unit, DaemonError>(Unit.Default)),
            caller,
            "community.audit-test",
            sink);

        var result = await decorated.RemoveInstanceAsync(new InstanceReference(InstanceId), CancellationToken.None);

        Assert.True(result.IsOk(out _));
        var recorded = Assert.Single(sink.Events);
        Assert.Equal("plugin:community.audit-test", recorded.Principal);
        Assert.Equal("community.audit-test", recorded.PluginId);
        Assert.Equal("mcsl.instance.remove", recorded.Method);
        Assert.Equal("mcsl.instance.remove", recorded.Permission);
        Assert.Equal(InstanceId.ToString("D"), recorded.Target);
        Assert.True(recorded.Succeeded);
        Assert.Null(recorded.ErrorCode);
    }

    [Fact]
    public async Task FailingAuditedCall_RecordsTheDaemonErrorCode()
    {
        var sink = new RecordingAuditSink();
        var caller = new CallerContext("plugin:community.audit-test", [], isMainToken: true);
        var decorated = new AuditedInstanceManagementApplication(
            new FakeInstanceManagement(RemoveResult: Result.Err<Unit, DaemonError>(
                new NotFoundDaemonError("instance.not_found", "The instance was not found."))),
            caller,
            "community.audit-test",
            sink);

        var result = await decorated.RemoveInstanceAsync(new InstanceReference(InstanceId), CancellationToken.None);

        Assert.True(result.IsErr(out _));
        var recorded = Assert.Single(sink.Events);
        Assert.False(recorded.Succeeded);
        Assert.Equal("instance.not_found", recorded.ErrorCode);
    }

    [Fact]
    public async Task ReadOnlyCall_RecordsNothing()
    {
        var sink = new RecordingAuditSink();
        var caller = new CallerContext("plugin:community.audit-test", [], isMainToken: true);
        var decorated = new AuditedSystemQueryApplication(
            new FakeSystemQueries(),
            caller,
            "community.audit-test",
            sink);

        // mcsl.java.list is in RpcAuditPolicy.ReadOnlyMethods, never AuditedMethods.
        var result = await decorated.ListJavaRuntimesAsync(CancellationToken.None);

        Assert.True(result.IsOk(out _));
        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task ThrowingSink_NeverDisturbsTheOutcomeTheCallerSees()
    {
        var caller = new CallerContext("plugin:community.audit-test", [], isMainToken: true);
        var decorated = new AuditedInstanceManagementApplication(
            new FakeInstanceManagement(RemoveResult: Result.Ok<Unit, DaemonError>(Unit.Default)),
            caller,
            "community.audit-test",
            new ThrowingAuditSink());

        var result = await decorated.RemoveInstanceAsync(new InstanceReference(InstanceId), CancellationToken.None);

        Assert.True(result.IsOk(out _));
    }

    [Fact]
    public async Task SingleAuditedCall_IsNeverRecordedMoreThanOnce()
    {
        var sink = new RecordingAuditSink();
        var caller = new CallerContext("plugin:community.audit-test", [], isMainToken: true);
        var decorated = new AuditedInstanceManagementApplication(
            new FakeInstanceManagement(RemoveResult: Result.Ok<Unit, DaemonError>(Unit.Default)),
            caller,
            "community.audit-test",
            sink);

        _ = await decorated.RemoveInstanceAsync(new InstanceReference(InstanceId), CancellationToken.None);

        // One decorated call must produce exactly one record - not zero, not two. Guards against
        // an accidental double-wrap of the authorized proxy inside PluginApplicationAuthorizer.Create.
        Assert.Single(sink.Events);
    }

    [Fact]
    public async Task EndToEnd_HostAndForPrincipal_BothRecordWithTheActingPrincipal()
    {
        var sink = new RecordingAuditSink();
        var verifiedPrincipals = new VerifiedPrincipalAuthority();
        var identity = new PluginIdentity("community.e2e-audit", "1.0.0");
        var inner = new FakeInstanceManagement(RemoveResult: Result.Ok<Unit, DaemonError>(Unit.Default));
        var authorizer = new PluginApplicationAuthorizer(
            identity,
            ["instance.manage"],
            new CallerContextFactory(verifiedPrincipals),
            instanceCatalog: null,
            instanceQueries: null,
            system: null,
            instanceManagement: inner,
            operationQueries: null,
            operationControl: null,
            provisioning: null,
            backups: null,
            audit: null,
            monitoring: null,
            automation: null,
            eventRules: null,
            files: null,
            auditSink: sink);

        _ = await authorizer.Host.InstanceManagement.RemoveInstanceAsync(
            new InstanceReference(InstanceId), CancellationToken.None);

        var hostRecord = Assert.Single(sink.Events);
        Assert.Equal("plugin:community.e2e-audit", hostRecord.Principal);
        Assert.Equal("community.e2e-audit", hostRecord.PluginId);

        var principal = new VerifiedPrincipal(
            "user-acting-through-plugin",
            "token-id",
            "issuer",
            "audience",
            DateTimeOffset.UtcNow.AddMinutes(5),
            ImmutableArray.Create("mcsl.instance.remove"),
            isMainToken: false);
        var principalApplications = authorizer.ForPrincipal(verifiedPrincipals.Register(identity, principal));

        _ = await principalApplications.InstanceManagement.RemoveInstanceAsync(
            new InstanceReference(InstanceId), CancellationToken.None);

        Assert.Equal(2, sink.Events.Count);
        var principalRecord = sink.Events[1];
        // The acting principal, not the plugin's own host subject, is what gets recorded.
        Assert.Equal("user-acting-through-plugin", principalRecord.Principal);
        Assert.Equal("community.e2e-audit", principalRecord.PluginId);
    }

    [Fact]
    public void AbsentAuditSink_LeavesTheAuthorizedSurfaceUnwrapped()
    {
        var verifiedPrincipals = new VerifiedPrincipalAuthority();
        var identity = new PluginIdentity("community.no-sink", "1.0.0");
        var authorizer = new PluginApplicationAuthorizer(
            identity,
            ["instance.manage"],
            new CallerContextFactory(verifiedPrincipals),
            instanceCatalog: null,
            instanceQueries: null,
            system: null,
            instanceManagement: new FakeInstanceManagement(RemoveResult: Result.Ok<Unit, DaemonError>(Unit.Default)),
            operationQueries: null,
            operationControl: null,
            provisioning: null,
            backups: null,
            audit: null,
            monitoring: null,
            automation: null,
            eventRules: null,
            files: null);

        // No auditSink argument at all: behavior must be exactly what it was before this boundary
        // existed, down to the concrete proxy type the host exposes.
        Assert.IsType<AuthorizedInstanceManagementApplication>(authorizer.Host.InstanceManagement);
    }

    private sealed class RecordingAuditSink : IAuditSink
    {
        internal List<AuditEvent> Events { get; } = [];

        public void Record(AuditEvent auditEvent) => Events.Add(auditEvent);
    }

    private sealed class ThrowingAuditSink : IAuditSink
    {
        public void Record(AuditEvent auditEvent) => throw new InvalidOperationException("sink defect");
    }

    private sealed class FakeInstanceManagement(Result<Unit, DaemonError> RemoveResult) : IInstanceManagementApplication
    {
        public Task<Result<CreateInstanceResult, DaemonError>> CreateInstanceAsync(
            CreateInstanceRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<Unit, DaemonError>> RemoveInstanceAsync(
            InstanceReference request,
            CancellationToken cancellationToken) =>
            Task.FromResult(RemoveResult);

        public Task<Result<Unit, DaemonError>> StartInstanceAsync(
            InstanceReference request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<Unit, DaemonError>> StopInstanceAsync(
            InstanceReference request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<Unit, DaemonError>> HaltInstanceAsync(
            InstanceReference request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<Unit, DaemonError>> SendCommandAsync(
            InstanceCommandRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<UpdateInstanceSettingsResult, DaemonError>> UpdateInstanceSettingsAsync(
            UpdateInstanceSettingsRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSystemQueries : ISystemQueryApplication
    {
        public Task<Result<SystemInfo, DaemonError>> GetSystemInfoAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<JavaRuntimeList, DaemonError>> ListJavaRuntimesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok<JavaRuntimeList, DaemonError>(
                new JavaRuntimeList(ImmutableArray<JavaRuntime>.Empty)));
    }
}
