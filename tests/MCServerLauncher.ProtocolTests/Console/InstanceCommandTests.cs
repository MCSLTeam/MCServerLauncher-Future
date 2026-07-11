using System.Collections.Concurrent;
using System.Reflection;
using Brigadier.NET;
using Brigadier.NET.Exceptions;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon;
using MCServerLauncher.Daemon.Console;
using MCServerLauncher.Daemon.Console.Commands;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Management.Communicate;
using Microsoft.Extensions.DependencyInjection;
using RustyOptions;
using TouchSocket.Core;
using TouchSocket.Core.AspNetCore;
using TouchSocket.Http;

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
            new KeyValuePair<Guid, IInstance>(uuid, CreateInstance("first")),
            new KeyValuePair<Guid, IInstance>(matchingNameKey, CreateInstance(uuid.ToString()))
        };

        var result = InstanceCommand.ResolveTarget(snapshot, uuid.ToString());

        Assert.Equal(uuid, result.InstanceId);
        Assert.Empty(result.AmbiguousInstanceIds);
    }

    [Fact]
    public void ResolveTarget_MissingUuidFallsBackToUuidShapedName()
    {
        var token = Guid.NewGuid().ToString();
        var key = Guid.NewGuid();
        var snapshot = new[]
        {
            new KeyValuePair<Guid, IInstance>(key, CreateInstance(token))
        };

        var result = InstanceCommand.ResolveTarget(snapshot, token);

        Assert.Equal(key, result.InstanceId);
        Assert.Empty(result.AmbiguousInstanceIds);
    }

    [Fact]
    public void ResolveTarget_NameMatchesOrdinalIgnoreCaseUsingSnapshotKey()
    {
        var key = Guid.NewGuid();
        var instance = CreateInstance("My Survival Server");
        instance.Config.Uuid = Guid.NewGuid();
        var snapshot = new[]
        {
            new KeyValuePair<Guid, IInstance>(key, instance)
        };

        var result = InstanceCommand.ResolveTarget(snapshot, "my survival server");

        Assert.Equal(key, result.InstanceId);
        Assert.NotEqual(instance.Config.Uuid, result.InstanceId);
    }

    [Fact]
    public void ResolveTarget_CaseInsensitiveDuplicatesAreAmbiguousWithSortedKeys()
    {
        var first = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var second = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var snapshot = new[]
        {
            new KeyValuePair<Guid, IInstance>(second, CreateInstance("Survival")),
            new KeyValuePair<Guid, IInstance>(first, CreateInstance("survival"))
        };

        var result = InstanceCommand.ResolveTarget(snapshot, "SURVIVAL");

        Assert.Null(result.InstanceId);
        Assert.Equal(new[] { first, second }, result.AmbiguousInstanceIds);
    }

    [Fact]
    public async Task Start_WaitsForManagerAndPassesGracefulShutdownToken()
    {
        var key = Guid.NewGuid();
        var instance = CreateInstance("alpha");
        var startGate = new TaskCompletionSource<IInstance?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = CreateHarness(
            [new KeyValuePair<Guid, IInstance>(key, instance)],
            start: (_, _) => startGate.Task);

        var executeTask = Task.Run(() => harness.Dispatcher.Execute("inst start alpha", harness.Source));
        await harness.Spy.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.False(executeTask.IsCompleted);
        Assert.Equal(key, harness.Spy.StartedIds.Single());
        Assert.Equal(harness.Shutdown.CancellationToken, harness.Spy.StartCancellationToken);

        startGate.SetResult(instance);

        Assert.Equal(0, await executeTask);
    }

    [Fact]
    public void Start_NullManagerResult_ReturnsBusinessFailure()
    {
        var key = Guid.NewGuid();
        using var harness = CreateHarness(
            [new KeyValuePair<Guid, IInstance>(key, CreateInstance("alpha"))],
            start: (_, _) => Task.FromResult<IInstance?>(null));

        var result = harness.Dispatcher.Execute("inst start alpha", harness.Source);

        Assert.Equal(1, result);
        Assert.Equal(new[] { key }, harness.Spy.StartedIds);
        Assert.Empty(harness.Spy.StoppedIds);
        Assert.Empty(harness.Spy.HaltedIds);
    }

    [Fact]
    public void Start_CanceledManagerTask_PropagatesCancellation()
    {
        var key = Guid.NewGuid();
        using var harness = CreateHarness(
            [new KeyValuePair<Guid, IInstance>(key, CreateInstance("alpha"))],
            start: (_, _) => Task.FromCanceled<IInstance?>(new CancellationToken(canceled: true)));

        Assert.ThrowsAny<OperationCanceledException>(() => harness.Dispatcher.Execute("inst start alpha", harness.Source));
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public void Stop_UsesTryStopInstanceAndMapsItsResult(bool stopped, int expectedExitCode)
    {
        var key = Guid.NewGuid();
        using var harness = CreateHarness(
            [new KeyValuePair<Guid, IInstance>(key, CreateInstance("alpha"))],
            stop: _ => stopped);

        var result = harness.Dispatcher.Execute("inst stop alpha", harness.Source);

        Assert.Equal(expectedExitCode, result);
        Assert.Equal(new[] { key }, harness.Spy.StoppedIds);
        Assert.Empty(harness.Spy.StartedIds);
        Assert.Empty(harness.Spy.HaltedIds);
    }

    [Fact]
    public void Halt_UsesKillInstanceWithoutRunningInstancePrecheck()
    {
        var key = Guid.NewGuid();
        using var harness = CreateHarness(
            [new KeyValuePair<Guid, IInstance>(key, CreateInstance("alpha"))],
            halt: _ => { });

        var result = harness.Dispatcher.Execute("inst halt alpha", harness.Source);

        Assert.Equal(0, result);
        Assert.Equal(new[] { key }, harness.Spy.HaltedIds);
        Assert.Empty(harness.Spy.StartedIds);
        Assert.Empty(harness.Spy.StoppedIds);
        Assert.Empty(harness.RunningInstances);
    }

    [Fact]
    public void Halt_ManagerException_PropagatesWithoutSuccess()
    {
        var key = Guid.NewGuid();
        var expected = new InvalidOperationException("Kill failed.");
        using var harness = CreateHarness(
            [new KeyValuePair<Guid, IInstance>(key, CreateInstance("alpha"))],
            halt: _ => throw expected);

        var exception = Record.Exception(() => harness.Dispatcher.Execute("inst halt alpha", harness.Source));

        Assert.Same(expected, exception);
        Assert.Equal(new[] { key }, harness.Spy.HaltedIds);
    }

    [Fact]
    public void Dispatcher_QuotedNameResolvesAndTrailingOrMissingTargetsAreSyntaxErrors()
    {
        var key = Guid.NewGuid();
        using var harness = CreateHarness(
            [new KeyValuePair<Guid, IInstance>(key, CreateInstance("My Survival Server"))],
            stop: _ => true);

        Assert.Equal(0, harness.Dispatcher.Execute("inst stop \"My Survival Server\"", harness.Source));
        Assert.Equal(new[] { key }, harness.Spy.StoppedIds);
        Assert.Throws<CommandSyntaxException>(() => harness.Dispatcher.Execute("inst stop", harness.Source));
        Assert.Throws<CommandSyntaxException>(() => harness.Dispatcher.Execute("inst stop alpha extra", harness.Source));
    }

    [Fact]
    public void Dispatcher_AmbiguousNameDoesNotInvokeLifecycleOperation()
    {
        using var harness = CreateHarness(
            [
                new KeyValuePair<Guid, IInstance>(Guid.NewGuid(), CreateInstance("alpha")),
                new KeyValuePair<Guid, IInstance>(Guid.NewGuid(), CreateInstance("ALPHA"))
            ],
            halt: _ => { });

        var result = harness.Dispatcher.Execute("inst halt alpha", harness.Source);

        Assert.Equal(1, result);
        Assert.Empty(harness.Spy.StartedIds);
        Assert.Empty(harness.Spy.StoppedIds);
        Assert.Empty(harness.Spy.HaltedIds);
    }

    [Fact]
    public void Dispatcher_UnmatchedTargetReturnsBusinessFailureWithoutLifecycleOperation()
    {
        using var harness = CreateHarness(
            [new KeyValuePair<Guid, IInstance>(Guid.NewGuid(), CreateInstance("alpha"))],
            halt: _ => { });

        var result = harness.Dispatcher.Execute("inst halt missing", harness.Source);

        Assert.Equal(1, result);
        Assert.Empty(harness.Spy.StartedIds);
        Assert.Empty(harness.Spy.StoppedIds);
        Assert.Empty(harness.Spy.HaltedIds);
    }

    [Fact]
    public void Dispatcher_UsageIncludesAllInstanceLifecycleBranches()
    {
        using var harness = CreateHarness();
        var instanceNode = harness.Dispatcher.GetRoot().GetChild("inst");

        Assert.NotNull(instanceNode);
        var usage = string.Join(" | ", harness.Dispatcher.GetAllUsage(instanceNode!, harness.Source, false));

        Assert.Contains("list", usage, StringComparison.Ordinal);
        Assert.Contains("start", usage, StringComparison.Ordinal);
        Assert.Contains("stop", usage, StringComparison.Ordinal);
        Assert.Contains("halt", usage, StringComparison.Ordinal);
    }

    private static CommandHarness CreateHarness(
        IEnumerable<KeyValuePair<Guid, IInstance>>? instances = null,
        Func<Guid, CancellationToken, Task<IInstance?>>? start = null,
        Func<Guid, bool>? stop = null,
        Action<Guid>? halt = null)
    {
        var instanceMap = new ConcurrentDictionary<Guid, IInstance>(instances ?? []);
        var runningInstances = new ConcurrentDictionary<Guid, IInstance>();
        var spy = new LifecycleSpy();
        var manager = CreateProxy<IInstanceManager>((method, args) => method.Name switch
        {
            "get_Instances" => instanceMap,
            "get_RunningInstances" => runningInstances,
            nameof(IInstanceManager.TryStartInstance) => StartInstance(args, spy, start),
            nameof(IInstanceManager.TryStopInstance) => StopInstance(args, spy, stop),
            nameof(IInstanceManager.KillInstance) => HaltInstance(args, spy, halt),
            _ => GetDefaultReturnValue(method.ReturnType)
        });
        var shutdown = new GracefulShutdown();
        var services = new ServiceCollection();
        services.AddSingleton<IInstanceManager>(manager);
        services.AddSingleton(shutdown);
        var resolver = new AspNetCoreContainer(services).BuildResolver();
        var httpService = CreateProxy<IHttpService>((method, _) => method.Name == "get_Resolver"
            ? resolver
            : GetDefaultReturnValue(method.ReturnType));
        var dispatcher = new CommandDispatcher<ConsoleCommandSource>();
        InstanceCommand.Register(dispatcher);

        return new CommandHarness(dispatcher, new ConsoleCommandSource(httpService), shutdown, spy, runningInstances);
    }

    private static object StartInstance(
        object?[]? args,
        LifecycleSpy spy,
        Func<Guid, CancellationToken, Task<IInstance?>>? start)
    {
        var id = GetInstanceId(args);
        var cancellationToken = GetCancellationToken(args);
        spy.StartedIds.Add(id);
        spy.StartCancellationToken = cancellationToken;
        spy.StartEntered.TrySetResult();
        return (start ?? ((_, _) => Task.FromResult<IInstance?>(null)))(id, cancellationToken);
    }

    private static object StopInstance(object?[]? args, LifecycleSpy spy, Func<Guid, bool>? stop)
    {
        var id = GetInstanceId(args);
        spy.StoppedIds.Add(id);
        return (stop ?? (_ => false))(id);
    }

    private static object? HaltInstance(object?[]? args, LifecycleSpy spy, Action<Guid>? halt)
    {
        var id = GetInstanceId(args);
        spy.HaltedIds.Add(id);
        halt?.Invoke(id);
        return null;
    }

    private static Guid GetInstanceId(object?[]? args)
    {
        return Assert.IsType<Guid>(args![0]);
    }

    private static CancellationToken GetCancellationToken(object?[]? args)
    {
        return Assert.IsType<CancellationToken>(args![1]);
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

    private static TestInstance CreateInstance(string name)
    {
        return new TestInstance(new InstanceConfig
        {
            Name = name,
            Target = "server.jar",
            TargetType = TargetType.Jar,
            InstanceType = InstanceType.MCJava,
            JavaPath = "java",
            Arguments = ["nogui"]
        });
    }

    private sealed class CommandHarness(
        CommandDispatcher<ConsoleCommandSource> dispatcher,
        ConsoleCommandSource source,
        GracefulShutdown shutdown,
        LifecycleSpy spy,
        ConcurrentDictionary<Guid, IInstance> runningInstances) : IDisposable
    {
        public CommandDispatcher<ConsoleCommandSource> Dispatcher { get; } = dispatcher;
        public ConsoleCommandSource Source { get; } = source;
        public GracefulShutdown Shutdown { get; } = shutdown;
        public LifecycleSpy Spy { get; } = spy;
        public ConcurrentDictionary<Guid, IInstance> RunningInstances { get; } = runningInstances;

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

    private sealed class TestInstance(InstanceConfig config) : IInstance
    {
        public InstanceConfig Config { get; } = config;
        public InstanceProcess? Process => null;
        public InstanceStatus Status => InstanceStatus.Stopped;
        public int ServerProcessId => -1;

        public event Action<Guid, string>? OnLog
        {
            add
            {
            }
            remove
            {
            }
        }

        public event Action<Guid, InstanceStatus>? OnStatusChanged
        {
            add
            {
            }
            remove
            {
            }
        }

        public Task<InstanceReport> GetReportAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new InstanceReport(Status, Config, new Dictionary<string, string>(), [], default));
        }

        public Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default)
        {
            return Task.FromResult(false);
        }

        public void Stop()
        {
        }

        public IReadOnlyList<string> GetLogHistory()
        {
            return Array.Empty<string>();
        }

        public void Dispose()
        {
        }
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
