using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Brigadier.NET;
using Brigadier.NET.Exceptions;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Console;
using MCServerLauncher.Daemon.Console.Commands;
using Microsoft.Extensions.DependencyInjection;
using RustyOptions;
using TouchSocket.Core.AspNetCore;
using TouchSocket.Http;
using ApplicationInstanceReport = MCServerLauncher.Common.Contracts.Instances.InstanceReport;

namespace MCServerLauncher.ProtocolTests;

public class InstanceCommandTests
{
    [Fact]
    public void ResolveTarget_ExistingUuidWinsOverEqualName()
    {
        var uuid = Guid.NewGuid();
        var matchingNameKey = Guid.NewGuid();
        var snapshot = new[]
        {
            new KeyValuePair<Guid, ApplicationInstanceReport>(uuid, CreateReport(uuid, "first")),
            new KeyValuePair<Guid, ApplicationInstanceReport>(matchingNameKey, CreateReport(matchingNameKey, uuid.ToString()))
        };

        var result = InstanceCommand.ResolveTarget(snapshot, uuid.ToString());

        Assert.Equal(uuid, result.InstanceId);
        Assert.Empty(result.AmbiguousInstanceIds);
    }

    [Fact]
    public void ResolveTarget_MissingUuidFallsBackToUuidShapedName()
    {
        var token = Guid.NewGuid().ToString();
        var id = Guid.NewGuid();

        var result = InstanceCommand.ResolveTarget(
            [new KeyValuePair<Guid, ApplicationInstanceReport>(id, CreateReport(id, token))],
            token);

        Assert.Equal(id, result.InstanceId);
        Assert.Empty(result.AmbiguousInstanceIds);
    }

    [Fact]
    public void ResolveTarget_CaseInsensitiveDuplicatesAreAmbiguousWithSortedKeys()
    {
        var first = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var second = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var result = InstanceCommand.ResolveTarget(
            [
                new KeyValuePair<Guid, ApplicationInstanceReport>(second, CreateReport(second, "Survival")),
                new KeyValuePair<Guid, ApplicationInstanceReport>(first, CreateReport(first, "survival"))
            ],
            "SURVIVAL");

        Assert.Null(result.InstanceId);
        Assert.Equal(new[] { first, second }, result.AmbiguousInstanceIds);
    }

    [Fact]
    public async Task Start_WaitsForApplicationAndPassesGracefulShutdownToken()
    {
        var id = Guid.NewGuid();
        var startGate = new TaskCompletionSource<Result<Unit, DaemonError>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = CreateHarness(
            [CreateReport(id, "alpha")],
            start: (_, _) => startGate.Task);

        var executeTask = Task.Run(() => harness.Dispatcher.Execute("inst start alpha", harness.Source));
        await harness.Spy.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.False(executeTask.IsCompleted);
        Assert.Equal(id, harness.Spy.StartedIds.Single());
        Assert.Equal(harness.Shutdown.CancellationToken, harness.Spy.StartCancellationToken);

        startGate.SetResult(Result.Ok<Unit, DaemonError>(Unit.Default));
        Assert.Equal(0, await executeTask);
    }

    [Fact]
    public void Start_ApplicationErrorReturnsBusinessFailure()
    {
        var id = Guid.NewGuid();
        using var harness = CreateHarness(
            [CreateReport(id, "alpha")],
            start: (_, _) => Task.FromResult(Result.Err<Unit, DaemonError>(
                new ConflictDaemonError("instance.running", "Already running."))));

        var result = harness.Dispatcher.Execute("inst start alpha", harness.Source);

        Assert.Equal(1, result);
        Assert.Equal(new[] { id }, harness.Spy.StartedIds);
        Assert.Empty(harness.Spy.StoppedIds);
        Assert.Empty(harness.Spy.HaltedIds);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public void Stop_UsesApplicationAndMapsItsResult(bool succeeds, int expectedExitCode)
    {
        var id = Guid.NewGuid();
        using var harness = CreateHarness(
            [CreateReport(id, "alpha")],
            stop: (_, _) => Task.FromResult(succeeds
                ? Result.Ok<Unit, DaemonError>(Unit.Default)
                : Result.Err<Unit, DaemonError>(new ConflictDaemonError("instance.stopped", "Not running."))));

        var result = harness.Dispatcher.Execute("inst stop alpha", harness.Source);

        Assert.Equal(expectedExitCode, result);
        Assert.Equal(new[] { id }, harness.Spy.StoppedIds);
        Assert.Empty(harness.Spy.StartedIds);
        Assert.Empty(harness.Spy.HaltedIds);
    }

    [Fact]
    public void Halt_UsesApplicationWithoutRunningInstancePrecheck()
    {
        var id = Guid.NewGuid();
        using var harness = CreateHarness([CreateReport(id, "alpha")]);

        var result = harness.Dispatcher.Execute("inst halt alpha", harness.Source);

        Assert.Equal(0, result);
        Assert.Equal(new[] { id }, harness.Spy.HaltedIds);
        Assert.Empty(harness.Spy.StartedIds);
        Assert.Empty(harness.Spy.StoppedIds);
    }

    [Fact]
    public void Halt_ApplicationExceptionPropagatesWithoutSuccess()
    {
        var id = Guid.NewGuid();
        var expected = new InvalidOperationException("Kill failed.");
        using var harness = CreateHarness(
            [CreateReport(id, "alpha")],
            halt: (_, _) => throw expected);

        var exception = Record.Exception(() => harness.Dispatcher.Execute("inst halt alpha", harness.Source));

        Assert.Same(expected, exception);
        Assert.Equal(new[] { id }, harness.Spy.HaltedIds);
    }

    [Fact]
    public void Dispatcher_QuotedNameAndAmbiguousTargetsPreserveTargetResolution()
    {
        var id = Guid.NewGuid();
        using var quoted = CreateHarness([CreateReport(id, "My Survival Server")]);
        Assert.Equal(0, quoted.Dispatcher.Execute("inst halt \"My Survival Server\"", quoted.Source));
        Assert.Equal(new[] { id }, quoted.Spy.HaltedIds);
        Assert.Throws<CommandSyntaxException>(() => quoted.Dispatcher.Execute("inst halt", quoted.Source));

        using var ambiguous = CreateHarness([CreateReport(Guid.NewGuid(), "alpha"), CreateReport(Guid.NewGuid(), "ALPHA")]);
        Assert.Equal(1, ambiguous.Dispatcher.Execute("inst halt alpha", ambiguous.Source));
        Assert.Empty(ambiguous.Spy.HaltedIds);
    }

    private static CommandHarness CreateHarness(
        IEnumerable<ApplicationInstanceReport>? reports = null,
        Func<Guid, CancellationToken, Task<Result<Unit, DaemonError>>>? start = null,
        Func<Guid, CancellationToken, Task<Result<Unit, DaemonError>>>? stop = null,
        Func<Guid, CancellationToken, Task<Result<Unit, DaemonError>>>? halt = null)
    {
        var reportMap = (reports ?? []).ToImmutableDictionary(report => report.Config.InstanceId);
        var spy = new LifecycleSpy();
        var instances = CreateProxy<IInstanceApplication>((method, args) => method.Name switch
        {
            nameof(IInstanceApplication.ListInstanceReportsAsync) => Task.FromResult(
                Result.Ok<InstanceReportList, DaemonError>(new InstanceReportList(reportMap))),
            nameof(IInstanceApplication.StartInstanceAsync) => InvokeLifecycle(args, spy.StartedIds, spy, start),
            nameof(IInstanceApplication.StopInstanceAsync) => InvokeLifecycle(args, spy.StoppedIds, spy, stop),
            nameof(IInstanceApplication.HaltInstanceAsync) => InvokeLifecycle(args, spy.HaltedIds, spy, halt),
            _ => GetDefaultReturnValue(method.ReturnType)
        });
        var shutdown = new GracefulShutdown();
        var services = new ServiceCollection();
        services.AddSingleton<IDaemonApplication>(new TestDaemonApplication(instances));
        services.AddSingleton(shutdown);
        var resolver = new AspNetCoreContainer(services).BuildResolver();
        var httpService = CreateProxy<IHttpService>((method, _) => method.Name == "get_Resolver"
            ? resolver
            : GetDefaultReturnValue(method.ReturnType));
        var dispatcher = new CommandDispatcher<ConsoleCommandSource>();
        InstanceCommand.Register(dispatcher);

        return new CommandHarness(dispatcher, new ConsoleCommandSource(httpService), shutdown, spy);
    }

    private static object InvokeLifecycle(
        object?[]? args,
        List<Guid> calledIds,
        LifecycleSpy spy,
        Func<Guid, CancellationToken, Task<Result<Unit, DaemonError>>>? implementation)
    {
        var request = Assert.IsType<InstanceReference>(args![0]);
        var cancellationToken = Assert.IsType<CancellationToken>(args[1]);
        calledIds.Add(request.InstanceId);
        if (ReferenceEquals(calledIds, spy.StartedIds))
        {
            spy.StartCancellationToken = cancellationToken;
            spy.StartEntered.TrySetResult();
        }

        return (implementation ?? ((_, _) => Task.FromResult(Result.Ok<Unit, DaemonError>(Unit.Default))))(
            request.InstanceId,
            cancellationToken);
    }

    private static ApplicationInstanceReport CreateReport(Guid instanceId, string name)
    {
        using var rulesDocument = JsonDocument.Parse("[]");
        var config = new InstanceConfiguration(
            instanceId,
            name,
            "server.jar",
            InstanceType.MCJava,
            TargetType.Jar,
            "",
            "utf-8",
            "utf-8",
            "java",
            ImmutableArray<string>.Empty,
            ImmutableDictionary<string, string>.Empty,
            rulesDocument.RootElement);
        return new ApplicationInstanceReport(
            InstanceStatus.Stopped,
            config,
            ImmutableDictionary<string, string>.Empty,
            ImmutableArray<InstancePlayer>.Empty,
            new InstancePerformance(0, 0),
            null);
    }

    private static T CreateProxy<T>(Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, InterfaceDispatchProxy>();
        ((InterfaceDispatchProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private static object? GetDefaultReturnValue(Type returnType)
    {
        return returnType == typeof(void)
            ? null
            : returnType.IsValueType
                ? Activator.CreateInstance(returnType)
                : null;
    }

    private sealed class TestDaemonApplication(IInstanceApplication instances) : IDaemonApplication
    {
        public IInstanceApplication Instances { get; } = instances;
        public IFileApplication Files => throw new NotSupportedException();
        public ISystemApplication System => throw new NotSupportedException();
        public IEventRuleApplication EventRules => throw new NotSupportedException();

        public IOperationApplication Operations { get; } = null!;
        public IProvisioningApplication Provisioning { get; } = null!;
    }

    private sealed class CommandHarness(
        CommandDispatcher<ConsoleCommandSource> dispatcher,
        ConsoleCommandSource source,
        GracefulShutdown shutdown,
        LifecycleSpy spy) : IDisposable
    {
        public CommandDispatcher<ConsoleCommandSource> Dispatcher { get; } = dispatcher;
        public ConsoleCommandSource Source { get; } = source;
        public GracefulShutdown Shutdown { get; } = shutdown;
        public LifecycleSpy Spy { get; } = spy;

        public void Dispose()
        {
            Shutdown.Dispose();
        }
    }

    private sealed class LifecycleSpy
    {
        public List<Guid> StartedIds { get; } = [];
        public List<Guid> StoppedIds { get; } = [];
        public List<Guid> HaltedIds { get; } = [];
        public CancellationToken StartCancellationToken { get; set; }
        public TaskCompletionSource StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private class InterfaceDispatchProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = (_, _) => null;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return Handler(targetMethod ?? throw new MissingMethodException("DispatchProxy target method was null."), args);
        }
    }
}
