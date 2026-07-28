using System.Collections.Immutable;
using System.Text;
using MCServerLauncher.Common.Contracts.Audit;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.ApplicationCore.Audit;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Dispatch;

namespace MCServerLauncher.ProtocolTests.Rpc.Dispatch;

public sealed class V2RpcDispatcherAuditTests
{
    private static readonly Guid InstanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task AuditedMethodSuccess_RecordsPrincipalMethodAndTarget()
    {
        var sink = new RecordingAuditSink();
        var dispatcher = CreateDispatcher(sink, Ok());

        var outcome = await dispatcher.DispatchAsync(StartRequest(), Connection("user-a"));

        Assert.True(outcome.HasResponse);
        var recorded = Assert.Single(sink.Events);
        Assert.Equal("user-a", recorded.Principal);
        Assert.Equal("mcsl.instance.start", recorded.Method);
        Assert.Equal("mcsl.instance.start", recorded.Permission);
        Assert.Equal(InstanceId.ToString("D"), recorded.Target);
        Assert.True(recorded.Succeeded);
        Assert.Null(recorded.ErrorCode);
        Assert.Null(recorded.PluginId);
    }

    [Fact]
    public async Task AuditedMethodDaemonError_RecordsFailureWithTheErrorCode()
    {
        var sink = new RecordingAuditSink();
        var dispatcher = CreateDispatcher(sink, (_, _, _) => Task.FromResult(
            ProtocolRpcExecution<UnitResult>.Err(
                new NotFoundDaemonError("instance.not_found", "The instance was not found."))));

        _ = await dispatcher.DispatchAsync(StartRequest(), Connection("user-a"));

        var recorded = Assert.Single(sink.Events);
        Assert.False(recorded.Succeeded);
        Assert.Equal("instance.not_found", recorded.ErrorCode);
        Assert.Equal(InstanceId.ToString("D"), recorded.Target);
    }

    [Fact]
    public async Task AuditedMethodUnexpectedException_RecordsFailureAndKeepsTheInternalError()
    {
        var sink = new RecordingAuditSink();
        var dispatcher = CreateDispatcher(sink, (_, _, _) => throw new InvalidOperationException("boom"));

        var outcome = await dispatcher.DispatchAsync(StartRequest(), Connection("user-a"));

        var error = JsonRpcWireParser.ParseErrorResponse(outcome.ResponseUtf8.AsSpan());
        Assert.Equal(-32603, error.Error.Code);
        var recorded = Assert.Single(sink.Events);
        Assert.False(recorded.Succeeded);
        Assert.Equal("internal.unexpected", recorded.ErrorCode);
    }

    [Fact]
    public async Task ReadOnlyMethodAndDeniedCall_RecordNothing()
    {
        var sink = new RecordingAuditSink();
        var pingDescriptor = (RpcDescriptor<EmptyRequest, PingResult>)BuiltInProtocolDefinitions.Rpcs.Single(
            candidate => candidate.Method.Value == "mcsl.daemon.ping");
        var builder = new ProtocolCatalogBuilder(new OpenRpcInfo("Audit dispatcher tests", "1.0.0"));
        builder.RegisterBuiltInRpc(
            pingDescriptor,
            new RpcBinding<EmptyRequest, PingResult>(
                ProtocolExecutionOwner.BuiltIn,
                static (_, _, _) => Task.FromResult(ProtocolRpcExecution<PingResult>.Ok(new PingResult(1)))));
        RegisterStart(builder, Ok());
        var dispatcher = new V2RpcDispatcher(builder.Freeze(), new NoOpDiagnosticSink(), sink);

        var ping = await dispatcher.DispatchAsync(
            Utf8("{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.daemon.ping\",\"id\":1}"),
            Connection("user-a"));
        Assert.True(ping.HasResponse);
        Assert.Empty(sink.Events);

        // Permission denied: not an authorized mutation, so nothing is recorded.
        var denied = await dispatcher.DispatchAsync(
            StartRequest(),
            new V2RpcConnectionContext(
                new TestPermissionView(ImmutableArray.Create("mcsl.daemon.ping"), "user-a"),
                null,
                CancellationToken.None));
        var error = JsonRpcWireParser.ParseErrorResponse(denied.ResponseUtf8.AsSpan());
        Assert.Equal(-32001, error.Error.Code);
        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task ThrowingSink_NeverRewritesTheMutationOutcome()
    {
        var diagnostics = new RecordingDiagnosticSink();
        var dispatcher = CreateDispatcher(new ThrowingAuditSink(), Ok(), diagnostics);

        var outcome = await dispatcher.DispatchAsync(StartRequest(), Connection("user-a"));

        var response = JsonRpcWireParser.ParseSuccessResponse(outcome.ResponseUtf8.AsSpan());
        Assert.NotNull(response.Result);
        var diagnostic = Assert.Single(diagnostics.Unexpected);
        Assert.Equal("mcsl.instance.start", diagnostic.Method);
    }

    [Fact]
    public async Task SuccessResultIdentifiers_AreExtractedIntoTheRecord()
    {
        var planId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var operationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var sink = new RecordingAuditSink();
        var descriptor = (RpcDescriptor<ProvisioningExecuteRequest, ProvisioningExecuteResult>)BuiltInProtocolDefinitions.Rpcs.Single(
            candidate => candidate.Method.Value == "mcsl.provisioning.execute");
        var builder = new ProtocolCatalogBuilder(new OpenRpcInfo("Audit dispatcher tests", "1.0.0"));
        builder.RegisterBuiltInRpc(
            descriptor,
            new RpcBinding<ProvisioningExecuteRequest, ProvisioningExecuteResult>(
                ProtocolExecutionOwner.BuiltIn,
                (_, _, _) => Task.FromResult(ProtocolRpcExecution<ProvisioningExecuteResult>.Ok(
                    new ProvisioningExecuteResult(planId, operationId)))));
        var dispatcher = new V2RpcDispatcher(builder.Freeze(), new NoOpDiagnosticSink(), sink);

        _ = await dispatcher.DispatchAsync(
            Utf8($"{{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.provisioning.execute\",\"id\":1,\"params\":{{\"plan_id\":\"{planId}\",\"executor_principal\":\"user-a\"}}}}"),
            Connection("user-a"));

        var recorded = Assert.Single(sink.Events);
        Assert.Equal(planId, recorded.PlanId);
        Assert.Equal(operationId, recorded.OperationId);
    }

    [Fact]
    public async Task AuditQueryRpc_ScopesRecordsByTheConnectionSubject()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-audit-e2e-").FullName;
        var auditLog = new AuditLog(new DaemonAuditConfig(), rootDirectory: root);
        var builder = new ProtocolCatalogBuilder(new OpenRpcInfo("Audit dispatcher tests", "1.0.0"));
        RegisterStart(builder, Ok());
        BuiltInAuditRpcRegistrar.Register(builder, new LocalAuditApplication(auditLog));
        var dispatcher = new V2RpcDispatcher(builder.Freeze(), new NoOpDiagnosticSink(), auditLog);

        _ = await dispatcher.DispatchAsync(StartRequest(), Connection("user-a"));
        _ = await dispatcher.DispatchAsync(StartRequest(), Connection("user-b"));

        var scoped = await QueryAsync(dispatcher, Connection("user-a"));
        var scopedRecord = Assert.Single(scoped.Records);
        Assert.Equal("user-a", scopedRecord.Principal);

        var admin = await QueryAsync(dispatcher, new V2RpcConnectionContext(
            new TestPermissionView(ImmutableArray.Create("*"), "daemon-main", IsMainToken: true),
            null,
            CancellationToken.None));
        Assert.Equal(2, admin.Records.Length);

        // The query itself is a read and must not append to the history it reads.
        var again = await QueryAsync(dispatcher, new V2RpcConnectionContext(
            new TestPermissionView(ImmutableArray.Create("*"), "daemon-main", IsMainToken: true),
            null,
            CancellationToken.None));
        Assert.Equal(2, again.Records.Length);
    }

    private static async Task<AuditQueryResult> QueryAsync(V2RpcDispatcher dispatcher, V2RpcConnectionContext connection)
    {
        var outcome = await dispatcher.DispatchAsync(
            Utf8("{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.audit.query\",\"id\":9,\"params\":{\"maximum_records\":100}}"),
            connection);
        var response = JsonRpcWireParser.ParseSuccessResponse(outcome.ResponseUtf8.AsSpan());
        return Assert.IsType<AuditQueryResult>(response.Result.Deserialize(
            MCServerLauncher.Common.Contracts.Serialization.ApplicationContractJsonContext.Default.AuditQueryResult));
    }

    private static ProtocolRpcHandler<InstanceReference, UnitResult> Ok() =>
        static (_, _, _) => Task.FromResult(ProtocolRpcExecution<UnitResult>.Ok(new UnitResult()));

    private static void RegisterStart(ProtocolCatalogBuilder builder, ProtocolRpcHandler<InstanceReference, UnitResult> handler)
    {
        var descriptor = (RpcDescriptor<InstanceReference, UnitResult>)BuiltInProtocolDefinitions.Rpcs.Single(
            candidate => candidate.Method.Value == "mcsl.instance.start");
        builder.RegisterBuiltInRpc(
            descriptor,
            new RpcBinding<InstanceReference, UnitResult>(ProtocolExecutionOwner.BuiltIn, handler));
    }

    private static V2RpcDispatcher CreateDispatcher(
        IAuditSink sink,
        ProtocolRpcHandler<InstanceReference, UnitResult> handler,
        IV2RpcDiagnosticSink? diagnostics = null)
    {
        var builder = new ProtocolCatalogBuilder(new OpenRpcInfo("Audit dispatcher tests", "1.0.0"));
        RegisterStart(builder, handler);
        return new V2RpcDispatcher(builder.Freeze(), diagnostics ?? new NoOpDiagnosticSink(), sink);
    }

    private static V2RpcConnectionContext Connection(string subject, params string[] extraPermissions) =>
        new(
            new TestPermissionView(ImmutableArray.Create("*").AddRange(extraPermissions), subject),
            null,
            CancellationToken.None);

    private static ReadOnlyMemory<byte> StartRequest() =>
        Utf8($"{{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.instance.start\",\"id\":1,\"params\":{{\"instance_id\":\"{InstanceId}\"}}}}");

    private static ReadOnlyMemory<byte> Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private sealed record TestPermissionView(
        ImmutableArray<string> Permissions,
        string Subject = "test-user",
        bool IsMainToken = false) : IProtocolPermissionView;

    private sealed class RecordingAuditSink : IAuditSink
    {
        internal List<AuditEvent> Events { get; } = [];

        public void Record(AuditEvent auditEvent) => Events.Add(auditEvent);
    }

    private sealed class ThrowingAuditSink : IAuditSink
    {
        public void Record(AuditEvent auditEvent) => throw new InvalidOperationException("sink defect");
    }

    private sealed class NoOpDiagnosticSink : IV2RpcDiagnosticSink
    {
        public void RecordUnexpected(V2RpcUnexpectedDiagnostic diagnostic)
        {
        }

        public void RecordNotificationSuppressed(V2RpcNotificationSuppressionDiagnostic diagnostic)
        {
        }
    }

    private sealed class RecordingDiagnosticSink : IV2RpcDiagnosticSink
    {
        internal List<V2RpcUnexpectedDiagnostic> Unexpected { get; } = [];

        public void RecordUnexpected(V2RpcUnexpectedDiagnostic diagnostic) => Unexpected.Add(diagnostic);

        public void RecordNotificationSuppressed(V2RpcNotificationSuppressionDiagnostic diagnostic)
        {
        }
    }
}
