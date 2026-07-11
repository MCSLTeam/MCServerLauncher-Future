using System.Reflection;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.DaemonClient.Connection;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MCServerLauncher.ProtocolTests;

[Collection(LegacyReconnectLogTestCollection.Name)]
public sealed class LegacyEventSubscriptionMigrationCharacterizationTests
{
    [Fact]
    [Trait("Category", "LegacyEventSubscriptionMigration")]
    public async Task SubscribeEvent_ConfirmsInitialRequestShapeAndSuccessfulAcknowledgement()
    {
        var tracker = new SubscribedEvents();
        var acknowledgement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SubscriptionRequest? captured = null;
        var daemon = CreateDaemonProxy(tracker, (method, args) =>
        {
            if (IsNonGenericRequest(method) && args is { Length: 4 })
            {
                captured = CaptureRequest(args);
                return acknowledgement.Task;
            }

            return GetDefaultReturnValue(method.ReturnType);
        });
        var meta = new InstanceLogEventMeta
        {
            InstanceId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")
        };
        const int timeout = 4321;
        using var cancellationSource = new CancellationTokenSource();

        var subscribeTask = daemon.SubscribeEvent(
            EventType.InstanceLog,
            meta,
            timeout,
            cancellationSource.Token);

        Assert.False(subscribeTask.IsCompleted);
        var request = AssertRequest(captured, ActionType.SubscribeEvent, timeout, cancellationSource.Token);
        var parameter = Assert.IsType<SubscribeEventParameter>(request.Parameter);
        Assert.Equal(EventType.InstanceLog, parameter.Type);
        Assert.True(parameter.Meta.HasValue);
        Assert.Equal(meta.InstanceId, parameter.Meta.Value.GetProperty("instance_id").GetGuid());

        acknowledgement.SetResult();
        await subscribeTask;

        Assert.True(subscribeTask.IsCompletedSuccessfully);
        Assert.Contains((EventType.InstanceLog, meta), tracker.Events);
    }

    [Fact]
    [Trait("Category", "LegacyEventSubscriptionMigration")]
    public async Task UnSubscribeEvent_ConfirmsInitialRequestShapeAndSuccessfulAcknowledgement()
    {
        var tracker = new SubscribedEvents();
        var acknowledgement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SubscriptionRequest? captured = null;
        var daemon = CreateDaemonProxy(tracker, (method, args) =>
        {
            if (IsNonGenericRequest(method) && args is { Length: 4 })
            {
                captured = CaptureRequest(args);
                return acknowledgement.Task;
            }

            return GetDefaultReturnValue(method.ReturnType);
        });
        var meta = new InstanceLogEventMeta
        {
            InstanceId = Guid.Parse("11111111-2222-3333-4444-555555555555")
        };
        tracker.EventSet.Add((EventType.InstanceLog, meta));
        const int timeout = 8765;
        using var cancellationSource = new CancellationTokenSource();

        var unsubscribeTask = daemon.UnSubscribeEvent(
            EventType.InstanceLog,
            meta,
            timeout,
            cancellationSource.Token);

        Assert.False(unsubscribeTask.IsCompleted);
        var request = AssertRequest(captured, ActionType.UnsubscribeEvent, timeout, cancellationSource.Token);
        var parameter = Assert.IsType<UnsubscribeEventParameter>(request.Parameter);
        Assert.Equal(EventType.InstanceLog, parameter.Type);
        Assert.True(parameter.Meta.HasValue);
        Assert.Equal(meta.InstanceId, parameter.Meta.Value.GetProperty("instance_id").GetGuid());

        acknowledgement.SetResult();
        await unsubscribeTask;

        Assert.True(unsubscribeTask.IsCompletedSuccessfully);
        Assert.DoesNotContain((EventType.InstanceLog, meta), tracker.Events);
    }

    [Fact]
    [Trait("Category", "LegacyEventSubscriptionMigration")]
    public async Task ReconnectReplay_InternalConnectionSeam_AttemptsEveryTrackedSubscription()
    {
        var connection = CreateOfflineConnection();
        var meta = new InstanceLogEventMeta
        {
            InstanceId = Guid.Parse("99999999-aaaa-bbbb-cccc-dddddddddddd")
        };

        var sink = new RecordingSink();
        var originalLogger = Log.Logger;
        using var testLogger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(sink)
            .CreateLogger();

        try
        {
            Log.Logger = testLogger;

            // Deliberate bug exclusion: seed ClientConnection's internal tracker directly. The legacy
            // concrete Daemon owns a separate tracker; asserting that split, public replay, or the
            // failure-time tracker mutation would freeze known V1 bugs. This only proves replay intent.
            connection.SubscribedEvents.EventSet.Add((EventType.InstanceLog, meta));
            connection.SubscribedEvents.EventSet.Add((EventType.DaemonReport, null));

            await InvokeReconnectRecoveryAsync(connection);

            Assert.Contains(sink.Events, logEvent =>
                logEvent.MessageTemplate.Text == "[ClientConnection] Try recovery {Count} subscribed events" &&
                logEvent.Properties.TryGetValue("Count", out var count) &&
                count is ScalarValue { Value: 2 });
            Assert.Equal(2, sink.Events.Count(logEvent =>
                logEvent.MessageTemplate.Text.StartsWith(
                    "[ClientConnection] Cannot recover subscribed event",
                    StringComparison.Ordinal)));
        }
        finally
        {
            Log.Logger = originalLogger;
            connection.Dispose();
        }
    }

    private static IDaemon CreateDaemonProxy(
        SubscribedEvents tracker,
        Func<MethodInfo, object?[]?, object?> handler)
    {
        return CreateProxy<IDaemon>((method, args) => method.Name switch
        {
            "get_SubscribedEvents" => tracker,
            _ => handler(method, args)
        });
    }

    private static bool IsNonGenericRequest(MethodInfo method)
    {
        return method.Name == nameof(IDaemon.RequestAsync) && !method.IsGenericMethod;
    }

    private static SubscriptionRequest CaptureRequest(object?[] args)
    {
        return new SubscriptionRequest(
            (ActionType)args[0]!,
            (IActionParameter?)args[1],
            (int)args[2]!,
            (CancellationToken)args[3]!);
    }

    private static SubscriptionRequest AssertRequest(
        SubscriptionRequest? captured,
        ActionType expectedAction,
        int expectedTimeout,
        CancellationToken expectedCancellationToken)
    {
        var request = Assert.IsType<SubscriptionRequest>(captured);
        Assert.Equal(expectedAction, request.ActionType);
        Assert.Equal(expectedTimeout, request.Timeout);
        Assert.Equal(expectedCancellationToken, request.CancellationToken);
        return request;
    }

    private static ClientConnection CreateOfflineConnection()
    {
        var constructor = typeof(ClientConnection).GetConstructor(
                              BindingFlags.Instance | BindingFlags.NonPublic,
                              binder: null,
                              [typeof(ClientConnectionConfig)],
                              modifiers: null)
                          ?? throw new MissingMethodException(typeof(ClientConnection).FullName, ".ctor");

        return (ClientConnection)constructor.Invoke([
            new ClientConnectionConfig
            {
                HeartBeat = false,
                HeartBeatTick = TimeSpan.FromMilliseconds(50),
                MaxFailCount = 1,
                PendingRequestCapacity = 4,
                PingTimeout = 100
            }
        ]);
    }

    private static async Task InvokeReconnectRecoveryAsync(ClientConnection connection)
    {
        var method = typeof(ClientConnection).GetMethod(
                         "OnReconnectedEventHandler",
                         BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(
                         typeof(ClientConnection).FullName,
                         "OnReconnectedEventHandler");
        var task = method.Invoke(connection, null) as Task
                   ?? throw new InvalidOperationException("Reconnect recovery handler did not return a task.");

        await task;
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
        if (returnType == typeof(void))
            return null;
        if (returnType == typeof(Task))
            return Task.CompletedTask;

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var resultValue = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
            return typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, [resultValue]);
        }

        return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
    }

    private sealed record SubscriptionRequest(
        ActionType ActionType,
        IActionParameter? Parameter,
        int Timeout,
        CancellationToken CancellationToken);

    private sealed class RecordingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            Events.Add(logEvent);
        }
    }

    private class InterfaceDispatchProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = (_, _) => null;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return Handler(
                targetMethod ?? throw new MissingMethodException("DispatchProxy target method was null."),
                args);
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LegacyReconnectLogTestCollection
{
    public const string Name = "Legacy reconnect log isolated tests";
}
