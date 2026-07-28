using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Audit;
using MCServerLauncher.Common.Contracts.Automation;
using MCServerLauncher.Common.Contracts.Backup;
using MCServerLauncher.Common.Contracts.Monitoring;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.DaemonClient.Application;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests.DaemonClient.V2;

/// <summary>
/// SDK-4b remote parity for the Preview-2 domains: every facade method must reach the frozen
/// descriptor of the matching method, forward the caller's request untouched, and honour the
/// caller's cancellation token.
/// </summary>
public sealed class RemotePreview2ApplicationTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task BackupFacadeMapsEveryFrozenDescriptor()
    {
        var invoker = new RecordingInvoker();
        var application = new RemoteBackupApplication(invoker);
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;

        var list = new BackupListQuery(Id, "owner-a");
        var create = new BackupCreateRequest(Id, true, "owner-a");
        var prune = new BackupPruneRequest("owner-a");
        var plan = new BackupRestorePlanRequest(Id, Id, "owner-a");
        var confirm = new BackupRestoreConfirmRequest(Id, "hash", "owner-a");
        var execute = new BackupRestoreExecuteRequest(Id, "owner-a");

        await application.ListAsync(list, token);
        await application.CreateAsync(create, token);
        await application.PruneAsync(prune, token);
        await application.PlanRestoreAsync(plan, token);
        await application.ConfirmRestoreAsync(confirm, token);
        await application.ExecuteRestoreAsync(execute, token);

        invoker.AssertCalls(
            token,
            ("mcsl.backup.list", list),
            ("mcsl.backup.create", create),
            ("mcsl.backup.prune", prune),
            ("mcsl.backup.restore.plan", plan),
            ("mcsl.backup.restore.confirm", confirm),
            ("mcsl.backup.restore.execute", execute));
    }

    [Fact]
    public async Task MonitoringFacadeMapsEveryFrozenDescriptor()
    {
        var invoker = new RecordingInvoker();
        var application = new RemoteMonitoringApplication(invoker);
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var query = new MonitoringQuery(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), 100);

        await application.GetCurrentAsync(token);
        await application.QueryAsync(query, token);

        Assert.Equal(
            ["mcsl.monitoring.current.get", "mcsl.monitoring.query"],
            invoker.Calls.Select(call => call.Descriptor.Method.Value));
        Assert.IsType<EmptyRequest>(invoker.Calls[0].Request);
        Assert.Same(query, invoker.Calls[1].Request);
        Assert.All(invoker.Calls, call => Assert.Equal(token, call.CancellationToken));
    }

    [Fact]
    public async Task AuditFacadeMapsTheFrozenDescriptor()
    {
        var invoker = new RecordingInvoker();
        var application = new RemoteAuditApplication(invoker);
        using var cancellation = new CancellationTokenSource();
        var query = new AuditQuery(100, null, null, "owner-a");

        await application.QueryAsync(query, cancellation.Token);

        invoker.AssertCalls(cancellation.Token, ("mcsl.audit.query", query));
    }

    [Fact]
    public async Task AutomationFacadeMapsEveryFrozenDescriptor()
    {
        var invoker = new RecordingInvoker();
        var application = new RemoteAutomationApplication(invoker);
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;

        var document = new AutomationPolicySet { Version = 3 };
        var validate = new AutomationValidateRequest(document);
        var apply = new AutomationApplyRequest(document);
        var enable = new AutomationEnableRequest(Id, false, 3);
        var confirm = new AutomationIntentConfirmRequest(Id, "hash", "owner-a");
        var execute = new AutomationIntentExecuteRequest(Id, "owner-a");

        await application.GetAsync(token);
        await application.ValidateAsync(validate, token);
        await application.TestAsync(token);
        await application.ApplyAsync(apply, token);
        await application.EnableAsync(enable, token);
        await application.ConfirmIntentAsync(confirm, token);
        await application.ExecuteIntentAsync(execute, token);

        Assert.Equal(
            [
                "mcsl.automation.get",
                "mcsl.automation.validate",
                "mcsl.automation.test",
                "mcsl.automation.apply",
                "mcsl.automation.enable",
                "mcsl.automation.intent.confirm",
                "mcsl.automation.intent.execute"
            ],
            invoker.Calls.Select(call => call.Descriptor.Method.Value));
        Assert.IsType<EmptyRequest>(invoker.Calls[0].Request);
        Assert.Same(validate, invoker.Calls[1].Request);
        Assert.IsType<EmptyRequest>(invoker.Calls[2].Request);
        Assert.Same(apply, invoker.Calls[3].Request);
        Assert.Same(enable, invoker.Calls[4].Request);
        Assert.Same(confirm, invoker.Calls[5].Request);
        Assert.Same(execute, invoker.Calls[6].Request);
        Assert.All(invoker.Calls, call => Assert.Equal(token, call.CancellationToken));
    }

    [Fact]
    public void EveryPreview2FacadeDescriptorIsTheFrozenCatalogInstance()
    {
        // The facades must reference the frozen descriptors themselves; a look-alike would drift
        // from the daemon's registered catalog without any test noticing.
        var frozen = BuiltInProtocolDefinitions.Rpcs.ToDictionary(
            descriptor => descriptor.Method.Value,
            StringComparer.Ordinal);
        string[] preview2Methods =
        [
            "mcsl.backup.list", "mcsl.backup.create", "mcsl.backup.prune",
            "mcsl.backup.restore.plan", "mcsl.backup.restore.confirm", "mcsl.backup.restore.execute",
            "mcsl.monitoring.current.get", "mcsl.monitoring.query",
            "mcsl.automation.get", "mcsl.automation.validate", "mcsl.automation.test",
            "mcsl.automation.apply", "mcsl.automation.enable",
            "mcsl.automation.intent.confirm", "mcsl.automation.intent.execute",
            "mcsl.audit.query"
        ];

        Assert.All(preview2Methods, method => Assert.True(frozen.ContainsKey(method), method));
        Assert.All(preview2Methods, method => Assert.Equal(method, frozen[method].Permission.Value));
    }

    private sealed record Call(RpcDescriptor Descriptor, object Request, CancellationToken CancellationToken);

    private sealed class RecordingInvoker : IRemoteApplicationInvoker
    {
        internal List<Call> Calls { get; } = [];

        private DaemonError Sentinel { get; } = new InternalDaemonError("test.result", "recorded");

        internal void AssertCalls(CancellationToken expectedToken, params (string Method, object Request)[] expected)
        {
            Assert.Equal(expected.Select(entry => entry.Method), Calls.Select(call => call.Descriptor.Method.Value));
            Assert.Equal(expected.Select(entry => entry.Request), Calls.Select(call => call.Request));
            Assert.All(Calls, call => Assert.Equal(expectedToken, call.CancellationToken));
        }

        public Task<Result<TResult, DaemonError>> InvokeAsync<TRequest, TResult>(
            RpcDescriptor<TRequest, TResult> descriptor,
            TRequest request,
            CancellationToken cancellationToken)
            where TResult : notnull
        {
            Calls.Add(new Call(descriptor, request!, cancellationToken));
            return Task.FromResult(Result.Err<TResult, DaemonError>(Sentinel));
        }

        public Task<Result<Unit, DaemonError>> InvokeUnitAsync<TRequest>(
            RpcDescriptor<TRequest, UnitResult> descriptor,
            TRequest request,
            CancellationToken cancellationToken)
        {
            Calls.Add(new Call(descriptor, request!, cancellationToken));
            return Task.FromResult(Result.Err<Unit, DaemonError>(Sentinel));
        }
    }
}
