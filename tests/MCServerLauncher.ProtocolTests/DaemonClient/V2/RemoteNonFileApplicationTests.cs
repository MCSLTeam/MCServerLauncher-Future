using System.Collections.Immutable;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.DaemonClient.Application;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests.DaemonClient.V2;

public sealed class RemoteNonFileApplicationTests
{
    [Fact]
    public async Task ProxiesMapEveryNonFileApplicationCallToItsFrozenDescriptor()
    {
        var invoker = new RecordingInvoker();
        var instances = new RemoteInstanceApplication(invoker);
        var system = new RemoteSystemApplication(invoker);
        var eventRules = new RemoteEventRuleApplication(invoker);
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var createRequest = new CreateInstanceRequest(null!);
        var removeRequest = new InstanceReference(Guid.NewGuid());
        var startRequest = new InstanceReference(Guid.NewGuid());
        var stopRequest = new InstanceReference(Guid.NewGuid());
        var haltRequest = new InstanceReference(Guid.NewGuid());
        var commandRequest = new InstanceCommandRequest(Guid.NewGuid(), "say test");
        var reportRequest = new InstanceReference(Guid.NewGuid());
        var logRequest = new InstanceLogQuery(Guid.NewGuid());
        var settingsRequest = new InstanceReference(Guid.NewGuid());
        var updateRequest = new UpdateInstanceSettingsRequest(
            Guid.NewGuid(), "name", default, null, [], null, null, false);
        var eventRuleQuery = new EventRuleQuery(Guid.NewGuid());
        using var document = JsonDocument.Parse("[]");
        var eventRuleUpdate = new EventRuleUpdateRequest(Guid.NewGuid(), document.RootElement);

        DaemonError[] errors =
        [
            (await instances.CreateInstanceAsync(createRequest, token)).UnwrapErr(),
            (await instances.RemoveInstanceAsync(removeRequest, token)).UnwrapErr(),
            (await instances.StartInstanceAsync(startRequest, token)).UnwrapErr(),
            (await instances.StopInstanceAsync(stopRequest, token)).UnwrapErr(),
            (await instances.HaltInstanceAsync(haltRequest, token)).UnwrapErr(),
            (await instances.SendCommandAsync(commandRequest, token)).UnwrapErr(),
            (await instances.GetInstanceReportAsync(reportRequest, token)).UnwrapErr(),
            (await instances.ListInstanceReportsAsync(token)).UnwrapErr(),
            (await instances.GetInstanceLogAsync(logRequest, token)).UnwrapErr(),
            (await instances.GetInstanceSettingsAsync(settingsRequest, token)).UnwrapErr(),
            (await instances.UpdateInstanceSettingsAsync(updateRequest, token)).UnwrapErr(),
            (await system.GetSystemInfoAsync(token)).UnwrapErr(),
            (await system.ListJavaRuntimesAsync(token)).UnwrapErr(),
            (await eventRules.GetEventRulesAsync(eventRuleQuery, token)).UnwrapErr(),
            (await eventRules.UpdateEventRulesAsync(eventRuleUpdate, token)).UnwrapErr()
        ];

        var expected = BuiltInProtocolDefinitions.Rpcs
            .Where(IsNonFileApplicationDescriptor)
            .ToHashSet(ReferenceEqualityComparer.Instance);
        var observed = invoker.Calls.Select(call => call.Descriptor)
            .ToHashSet(ReferenceEqualityComparer.Instance);
        Assert.Equal(expected, observed);
        Assert.All(invoker.Calls, call => Assert.Equal(token, call.CancellationToken));
        Assert.Equal(
            [
                BuiltInProtocolDefinitions.CreateInstance, BuiltInProtocolDefinitions.RemoveInstance,
                BuiltInProtocolDefinitions.StartInstance, BuiltInProtocolDefinitions.StopInstance,
                BuiltInProtocolDefinitions.HaltInstance, BuiltInProtocolDefinitions.SendInstanceCommand,
                BuiltInProtocolDefinitions.GetInstanceReport, BuiltInProtocolDefinitions.ListInstanceReports,
                BuiltInProtocolDefinitions.GetInstanceLog, BuiltInProtocolDefinitions.GetInstanceSettings,
                BuiltInProtocolDefinitions.UpdateInstanceSettings, BuiltInProtocolDefinitions.GetSystemInfo,
                BuiltInProtocolDefinitions.ListJavaRuntimes, BuiltInProtocolDefinitions.GetInstanceEventRules,
                BuiltInProtocolDefinitions.UpdateInstanceEventRules
            ],
            invoker.Calls.Select(call => call.Descriptor));
        Assert.Same(createRequest, invoker.Calls[0].Request);
        Assert.Same(removeRequest, invoker.Calls[1].Request);
        Assert.Same(startRequest, invoker.Calls[2].Request);
        Assert.Same(stopRequest, invoker.Calls[3].Request);
        Assert.Same(haltRequest, invoker.Calls[4].Request);
        Assert.Same(commandRequest, invoker.Calls[5].Request);
        Assert.Same(reportRequest, invoker.Calls[6].Request);
        Assert.IsType<EmptyRequest>(invoker.Calls[7].Request);
        Assert.Same(logRequest, invoker.Calls[8].Request);
        Assert.Same(settingsRequest, invoker.Calls[9].Request);
        Assert.Same(updateRequest, invoker.Calls[10].Request);
        Assert.IsType<EmptyRequest>(invoker.Calls[11].Request);
        Assert.IsType<EmptyRequest>(invoker.Calls[12].Request);
        Assert.Same(eventRuleQuery, invoker.Calls[13].Request);
        Assert.Same(eventRuleUpdate, invoker.Calls[14].Request);
        Assert.All(errors, error => Assert.Same(invoker.Sentinel, error));
        Assert.All(invoker.Calls.Where(call => call.Descriptor.ResultTypeInfo.Type == typeof(UnitResult)), call => Assert.True(call.IsUnit));
        Assert.All(invoker.Calls.Where(call => call.Descriptor.RequestTypeInfo.Type == typeof(EmptyRequest)), call =>
            Assert.IsType<EmptyRequest>(call.Request));
    }

    private static bool IsNonFileApplicationDescriptor(RpcDescriptor descriptor) =>
        (IsApplicationContract(descriptor.RequestTypeInfo.Type) || IsApplicationContract(descriptor.ResultTypeInfo.Type)) &&
        !descriptor.Method.Value.StartsWith("mcsl.instance.console.", StringComparison.Ordinal);

    private static bool IsApplicationContract(Type type) => type.Namespace is string ns &&
        (ns.StartsWith("MCServerLauncher.Common.Contracts.Instances", StringComparison.Ordinal) ||
         ns.StartsWith("MCServerLauncher.Common.Contracts.System", StringComparison.Ordinal) ||
         ns.StartsWith("MCServerLauncher.Common.Contracts.EventRules", StringComparison.Ordinal));

    private sealed class RecordingInvoker : IRemoteApplicationInvoker
    {
        internal List<Call> Calls { get; } = [];
        internal DaemonError Sentinel { get; } =
            new InternalDaemonError("test.result", "The recording invoker has no result.");

        public Task<Result<TResult, DaemonError>> InvokeAsync<TRequest, TResult>(RpcDescriptor<TRequest, TResult> descriptor, TRequest request, CancellationToken cancellationToken)
            where TResult : notnull
        {
            Calls.Add(new(descriptor, request!, cancellationToken, false));
            return Task.FromResult(Result.Err<TResult, DaemonError>(Sentinel));
        }

        public Task<Result<Unit, DaemonError>> InvokeUnitAsync<TRequest>(RpcDescriptor<TRequest, UnitResult> descriptor, TRequest request, CancellationToken cancellationToken)
        {
            Calls.Add(new(descriptor, request!, cancellationToken, true));
            return Task.FromResult(Result.Err<Unit, DaemonError>(Sentinel));
        }
    }

    private sealed record Call(RpcDescriptor Descriptor, object Request, CancellationToken CancellationToken, bool IsUnit);
}
