using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Events;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.DaemonClient.Connection.V2;
using MCServerLauncher.DaemonClient.Protocol;
using MCServerLauncher.DaemonClient.State;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests.DaemonClient.V2;

public sealed class DaemonClientFacadeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void OptionsExposeFrozenDefaultsAndValues()
    {
        var endpoint = new Uri("wss://daemon.example/api/v2");
        var options = new DaemonClientOptions(endpoint, "token");

        Assert.Same(endpoint, options.Endpoint);
        Assert.Equal("token", options.Token);
        Assert.Equal(TimeSpan.FromSeconds(30), options.RequestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(3), options.ReconnectDelay);

        var explicitValues = new DaemonClientOptions(
            endpoint,
            "other",
            TimeSpan.FromSeconds(9),
            TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromSeconds(9), explicitValues.RequestTimeout);
        Assert.Equal(TimeSpan.Zero, explicitValues.ReconnectDelay);
    }

    [Fact]
    public void OptionsRejectCompleteInvalidMatrix()
    {
        Assert.Throws<ArgumentNullException>(() => new DaemonClientOptions(null!, "token"));
        Assert.Throws<ArgumentNullException>(() => new DaemonClientOptions(Endpoint(), null!));
        Assert.Throws<ArgumentException>(() => new DaemonClientOptions(Endpoint(), " "));

        var invalidEndpoints = new[]
        {
            new Uri("/api/v2", UriKind.Relative),
            new Uri("http://daemon.example/api/v2"),
            new Uri("ws://daemon.example/"),
            new Uri("ws://daemon.example/api/v2/"),
            new Uri("ws://daemon.example/api/v2?x=1"),
            new Uri("ws://daemon.example/api/v2#x"),
            new Uri("ws://user@daemon.example/api/v2")
        };
        foreach (var endpoint in invalidEndpoints)
            Assert.Throws<ArgumentException>(() => new DaemonClientOptions(endpoint, "token"));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DaemonClientOptions(Endpoint(), "token", TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DaemonClientOptions(Endpoint(), "token", TimeSpan.FromTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DaemonClientOptions(Endpoint(), "token", reconnectDelay: TimeSpan.FromTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DaemonClientOptions(
                Endpoint(),
                "token",
                reconnectDelay: TimeSpan.FromMilliseconds(uint.MaxValue)));
    }

    [Fact]
    public void PublicSurfaceIsExactAndTransportNeutral()
    {
        var type = typeof(MCServerLauncher.DaemonClient.DaemonClient);
        Assert.True(type.IsSealed);
        Assert.Contains(typeof(IDaemonApplication), type.GetInterfaces());
        Assert.Contains(typeof(IAsyncDisposable), type.GetInterfaces());
        Assert.Equal(
            [typeof(DaemonClientOptions)],
            type.GetConstructors().Single().GetParameters().Select(static parameter => parameter.ParameterType));

        Assert.Equal(
            ["ConnectionState", "EventRules", "Files", "InstanceCatalog", "Instances", "LastFailure", "Operations", "System"],
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Select(static property => property.Name)
                .Order());
        Assert.Equal(
            ["ConnectAsync", "DiscoverAsync", "DisposeAsync", "InvokeAsync", "PingAsync", "RestartInstanceAsync", "SubscribeAsync"],
            type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(static method => !method.IsSpecialName)
                .Select(static method => method.Name)
                .Order());
        var ping = type.GetMethod(nameof(MCServerLauncher.DaemonClient.DaemonClient.PingAsync));
        Assert.NotNull(ping);
        Assert.Equal(typeof(Task<Result<PingResult, DaemonError>>), ping.ReturnType);
        var pingCancellation = Assert.Single(ping.GetParameters());
        Assert.Equal(typeof(CancellationToken), pingCancellation.ParameterType);
        Assert.True(pingCancellation.IsOptional);
        var restart = type.GetMethod(nameof(MCServerLauncher.DaemonClient.DaemonClient.RestartInstanceAsync));
        Assert.NotNull(restart);
        Assert.Equal(typeof(Task<Result<Unit, DaemonError>>), restart.ReturnType);
        Assert.Equal(
            [typeof(Guid), typeof(CancellationToken)],
            restart.GetParameters().Select(static parameter => parameter.ParameterType));
        Assert.True(restart.GetParameters()[1].IsOptional);
        Assert.Equal("StateChanged", Assert.Single(type.GetEvents(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)).Name);

        var signatures = type.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(static member => member.ToString() ?? string.Empty);
        foreach (var signature in signatures)
        {
            Assert.DoesNotContain("TouchSocket", signature, StringComparison.Ordinal);
            Assert.DoesNotContain("Connection.V2", signature, StringComparison.Ordinal);
            Assert.DoesNotContain("ClientConnectionConfig", signature, StringComparison.Ordinal);
            Assert.DoesNotContain("SubscribedEvents", signature, StringComparison.Ordinal);
            Assert.DoesNotContain("IDaemon ", signature, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ConstructorIsColdAndDomainsCatalogAndDisconnectedErrorsAreStable()
    {
        var factory = new ControlledSessionFactory();
        await using var client = Client(factory);

        Assert.Equal(0, factory.CreatedCount);
        Assert.Same(client.Instances, client.Instances);
        Assert.Same(client.Files, client.Files);
        Assert.Same(client.System, client.System);
        Assert.Same(client.EventRules, client.EventRules);
        Assert.Same(client.InstanceCatalog, client.InstanceCatalog);
        Assert.Equal(DaemonConnectionState.Disconnected, client.ConnectionState);
        Assert.Null(client.LastFailure);

        await AssertNotReady(client.Instances.ListInstanceReportsAsync(default));
        await AssertNotReady(client.Files.GetDirectoryInfoAsync(default!, default));
        await AssertNotReady(client.System.GetSystemInfoAsync(default));
        await AssertNotReady(client.EventRules.GetEventRulesAsync(default!, default));
        var subscription = await client.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            _ => Task.CompletedTask);
        Assert.True(subscription.IsErr(out var error));
        Assert.Equal("client.not_ready", error!.Code);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            _ => Task.CompletedTask,
            cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task ControlledReadySessionForwardsStateAndTypedSubscription()
    {
        var factory = new ControlledSessionFactory();
        await using var client = Client(factory);
        var states = new ConcurrentQueue<DaemonConnectionState>();
        var readyObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.StateChanged += state =>
        {
            states.Enqueue(state);
            if (state == DaemonConnectionState.Ready)
                readyObserved.TrySetResult();
            return Task.CompletedTask;
        };

        var session = await MakeReadyAsync(client, factory);
        await readyObserved.Task.WaitAsync(Timeout);
        Assert.Equal(DaemonConnectionState.Ready, client.ConnectionState);
        Assert.Null(client.LastFailure);
        Assert.Contains(DaemonConnectionState.Connecting, states);
        Assert.Contains(DaemonConnectionState.Synchronizing, states);
        Assert.Contains(DaemonConnectionState.Ready, states);

        var received = new TaskCompletionSource<DaemonEvent<InstanceLogEventData, InstanceLogEventMeta>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var subscribing = client.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            value =>
            {
                received.TrySetResult(value);
                return Task.CompletedTask;
            });
        var subscribe = await session.Transport.NextAsync();
        Assert.Equal("mcsl.event.subscribe", subscribe.Method);
        session.RouteSuccess(subscribe);
        var result = await subscribing.WaitAsync(Timeout);
        var handle = result.Unwrap();

        session.RouteNotification(
            $"{{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.instance.log\",\"params\":{{\"sequence\":7,\"timestamp\":8,\"meta\":{{\"instance_id\":\"11111111-1111-1111-1111-111111111111\"}},\"data\":{{\"log\":\"started\"}}}}}}");
        var value = await received.Task.WaitAsync(Timeout);
        Assert.Equal(7, value.Sequence);
        Assert.Equal("started", value.Data.Value.Log);

        var first = handle.DisposeAsync().AsTask();
        var second = handle.DisposeAsync().AsTask();
        Assert.Same(first, second);
        var unsubscribe = await session.Transport.NextAsync();
        Assert.Equal("mcsl.event.unsubscribe", unsubscribe.Method);
        session.RouteSuccess(unsubscribe);
        await Task.WhenAll(first, second).WaitAsync(Timeout);
    }

    [Fact]
    public async Task ReadyPingUsesFrozenMethodReturnsTypedResultAndPreservesCancellation()
    {
        var factory = new ControlledSessionFactory();
        await using var client = Client(factory);
        var session = await MakeReadyAsync(client, factory);

        var pinging = client.PingAsync();
        var ping = await session.Transport.NextAsync();
        Assert.Equal(BuiltInProtocolDefinitions.PingDaemon.Method.Value, ping.Method);
        session.RouteSuccess(ping, "{\"time\":123456789}");

        var result = await pinging.WaitAsync(Timeout);
        Assert.True(result.IsOk(out var value));
        Assert.Equal(123456789, value!.Time);

        using var cancellation = new CancellationTokenSource();
        var canceledPing = client.PingAsync(cancellation.Token);
        Assert.Equal(
            BuiltInProtocolDefinitions.PingDaemon.Method.Value,
            (await session.Transport.NextAsync()).Method);
        cancellation.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledPing);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task RestartStopsWaitsOneSecondThenStarts()
    {
        var factory = new ControlledSessionFactory();
        var time = new ManualTimeProvider();
        await using var client = Client(factory, time);
        var session = await MakeReadyAsync(client, factory);
        var instanceId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var restarting = client.RestartInstanceAsync(instanceId);
        var stop = await session.Transport.NextAsync();
        Assert.Equal(BuiltInProtocolDefinitions.StopInstance.Method.Value, stop.Method);
        session.RouteSuccess(stop);

        await time.TimerCreated.Task.WaitAsync(Timeout);
        Assert.False(restarting.IsCompleted);
        Assert.Equal(TimeSpan.FromSeconds(1), time.LastDueTime);
        time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.False(restarting.IsCompleted);
        Assert.Equal(0, session.Transport.PendingCount);

        time.Advance(TimeSpan.FromMilliseconds(1));
        var start = await session.Transport.NextAsync();
        Assert.Equal(BuiltInProtocolDefinitions.StartInstance.Method.Value, start.Method);
        session.RouteSuccess(start);

        Assert.True((await restarting.WaitAsync(Timeout)).IsOk(out _));
    }

    [Fact]
    public async Task RestartStopAndStartErrorsRemainTyped()
    {
        var stopFactory = new ControlledSessionFactory();
        var stopTime = new ManualTimeProvider();
        await using var stopClient = Client(stopFactory, stopTime);
        var stopSession = await MakeReadyAsync(stopClient, stopFactory);

        var stopped = stopClient.RestartInstanceAsync(Guid.NewGuid());
        var stop = await stopSession.Transport.NextAsync();
        stopSession.RouteError(stop, "instance.stop_failed", "conflict");
        var stopResult = await stopped.WaitAsync(Timeout);
        Assert.True(stopResult.IsErr(out var stopError));
        Assert.Equal("instance.stop_failed", stopError!.Code);
        Assert.Equal(0, stopTime.TimerCount);
        Assert.Equal(0, stopSession.Transport.PendingCount);

        var startFactory = new ControlledSessionFactory();
        var startTime = new ManualTimeProvider();
        await using var startClient = Client(startFactory, startTime);
        var startSession = await MakeReadyAsync(startClient, startFactory);

        var started = startClient.RestartInstanceAsync(Guid.NewGuid());
        var successfulStop = await startSession.Transport.NextAsync();
        startSession.RouteSuccess(successfulStop);
        await startTime.TimerCreated.Task.WaitAsync(Timeout);
        startTime.Advance(TimeSpan.FromSeconds(1));
        var start = await startSession.Transport.NextAsync();
        startSession.RouteError(start, "instance.start_failed", "conflict");
        var startResult = await started.WaitAsync(Timeout);
        Assert.True(startResult.IsErr(out var startError));
        Assert.Equal("instance.start_failed", startError!.Code);
    }

    [Fact]
    public async Task RestartDelayCancellationPreservesTokenAndDoesNotStart()
    {
        var factory = new ControlledSessionFactory();
        var time = new ManualTimeProvider();
        await using var client = Client(factory, time);
        var session = await MakeReadyAsync(client, factory);
        using var cancellation = new CancellationTokenSource();

        var restarting = client.RestartInstanceAsync(Guid.NewGuid(), cancellation.Token);
        var stop = await session.Transport.NextAsync();
        session.RouteSuccess(stop);
        await time.TimerCreated.Task.WaitAsync(Timeout);

        cancellation.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => restarting);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(0, session.Transport.PendingCount);
    }

    [Fact]
    public async Task SubscriptionAndFacadeDisposeShareOneCleanupTask()
    {
        var inner = new BlockingAsyncDisposable();
        var subscription = new DaemonEventSubscription(inner);
        var subscriptionFirst = subscription.DisposeAsync().AsTask();
        var subscriptionSecond = subscription.DisposeAsync().AsTask();
        Assert.Same(subscriptionFirst, subscriptionSecond);
        Assert.Equal(1, inner.DisposeCount);
        Assert.False(subscriptionFirst.IsCompleted);
        inner.Release.TrySetResult();
        await Task.WhenAll(subscriptionFirst, subscriptionSecond).WaitAsync(Timeout);

        var factory = new ControlledSessionFactory();
        var client = Client(factory);
        var session = await MakeReadyAsync(client, factory);
        session.BlockClose = true;
        var first = client.DisposeAsync().AsTask();
        var second = client.DisposeAsync().AsTask();
        Assert.Same(first, second);
        await session.CloseStarted.Task.WaitAsync(Timeout);
        Assert.False(first.IsCompleted);
        session.ReleaseClose.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(Timeout);
        Assert.Equal(1, session.CloseCount);
        Assert.Equal(1, session.DisposeCount);
    }

    private static Uri Endpoint() => new("ws://daemon.example/api/v2");

    private static MCServerLauncher.DaemonClient.DaemonClient Client(
        ControlledSessionFactory factory,
        TimeProvider? timeProvider = null) =>
        new(
            new V2ClientConnectionOwner(factory, TimeProvider.System, TimeSpan.FromSeconds(3)),
            timeProvider ?? TimeProvider.System);

    private static async Task<ControlledSession> MakeReadyAsync(
        MCServerLauncher.DaemonClient.DaemonClient client,
        ControlledSessionFactory factory)
    {
        var connecting = client.ConnectAsync();
        var session = await factory.Created.Task.WaitAsync(Timeout);
        session.CompleteConnect();
        var subscribe = await session.Transport.NextAsync();
        Assert.Equal("mcsl.event.subscribe", subscribe.Method);
        session.RouteSuccess(subscribe);
        var catalog = await session.Transport.NextAsync();
        Assert.Equal("mcsl.instance.catalog.get", catalog.Method);
        session.RouteSuccess(catalog, "{\"version\":0,\"items\":[]}");
        Assert.True((await connecting.WaitAsync(Timeout)).IsOk(out _));
        return session;
    }

    private static async Task AssertNotReady<T>(Task<Result<T, DaemonError>> operation)
        where T : notnull
    {
        var result = await operation;
        Assert.True(result.IsErr(out var error));
        Assert.Equal("client.not_ready", error!.Code);
    }

    private sealed class BlockingAsyncDisposable : IAsyncDisposable
    {
        private int _disposeCount;
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int DisposeCount => Volatile.Read(ref _disposeCount);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return new ValueTask(Release.Task);
        }
    }

    private sealed class ControlledSessionFactory : IV2ClientConnectionSessionFactory
    {
        private int _createdCount;
        internal TaskCompletionSource<ControlledSession> Created { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int CreatedCount => Volatile.Read(ref _createdCount);

        public IV2ClientConnectionSession Create(
            RemoteInstanceCatalogMirror mirror,
            Action<V2ClientConnectionCoordinator, JsonRpcRemoteEventNotification> routeEvent,
            Action<V2ClientDiagnostic>? diagnostic = null)
        {
            Interlocked.Increment(ref _createdCount);
            var session = new ControlledSession(mirror, routeEvent, diagnostic);
            Created.TrySetResult(session);
            return session;
        }
    }

    private sealed class ControlledSession : IV2ClientConnectionSession
    {
        private readonly TaskCompletionSource<Result<Unit, DaemonError>> _connect =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<DaemonError> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _closeCount;
        private int _disposeCount;

        internal ControlledSession(
            RemoteInstanceCatalogMirror mirror,
            Action<V2ClientConnectionCoordinator, JsonRpcRemoteEventNotification> routeEvent,
            Action<V2ClientDiagnostic>? diagnostic)
        {
            Transport = new ScriptedTransport();
            var next = 0;
            Coordinator = new V2ClientConnectionCoordinator(
                Transport,
                mirror,
                TimeProvider.System,
                TimeSpan.FromMinutes(1),
                () => JsonRpcRequestId.FromString($"facade-{Interlocked.Increment(ref next)}"),
                diagnostic,
                routeEvent);
        }

        public V2ClientConnectionCoordinator Coordinator { get; }
        public Task<DaemonError> Completion => _completion.Task;
        internal ScriptedTransport Transport { get; }
        internal bool BlockClose { get; set; }
        internal TaskCompletionSource CloseStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseClose { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int CloseCount => Volatile.Read(ref _closeCount);
        internal int DisposeCount => Volatile.Read(ref _disposeCount);

        public Task<Result<Unit, DaemonError>> ConnectAsync(CancellationToken cancellationToken) =>
            _connect.Task.WaitAsync(cancellationToken);

        public Task CloseAsync()
        {
            Interlocked.Increment(ref _closeCount);
            _connect.TrySetResult(Result.Err<Unit, DaemonError>(new TransportDaemonError("connection.closed", "closed")));
            _completion.TrySetResult(new TransportDaemonError("connection.closed", "closed"));
            CloseStarted.TrySetResult();
            return BlockClose ? ReleaseClose.Task : Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }

        internal void CompleteConnect() =>
            _connect.TrySetResult(Result.Ok<Unit, DaemonError>(Unit.Default));

        internal void RouteSuccess(SentRequest request, string result = "{}") =>
            Coordinator.Core.RouteText(Encoding.UTF8.GetBytes(
                $"{{\"jsonrpc\":\"2.0\",\"id\":{request.IdJson},\"result\":{result}}}"));

        internal void RouteError(SentRequest request, string code, string kind) =>
            Coordinator.Core.RouteText(Encoding.UTF8.GetBytes(
                $"{{\"jsonrpc\":\"2.0\",\"id\":{request.IdJson},\"error\":{{\"code\":-32000,\"message\":\"Rejected\",\"data\":{{\"daemon_error_code\":\"{code}\",\"daemon_error_kind\":\"{kind}\",\"correlation_id\":\"facade-test\"}}}}}}"));

        internal void RouteNotification(string notification) =>
            Coordinator.Core.RouteText(Encoding.UTF8.GetBytes(notification));
    }

    private sealed class ScriptedTransport : IV2ClientWireTransport
    {
        private readonly ConcurrentQueue<SentRequest> _requests = new();
        private readonly SemaphoreSlim _available = new(0);

        internal int PendingCount => _requests.Count;

        public ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken)
        {
            var json = Encoding.UTF8.GetString(utf8Json.AsSpan());
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            _requests.Enqueue(new SentRequest(
                root.GetProperty("method").GetString()!,
                root.GetProperty("id").GetRawText()));
            _available.Release();
            return ValueTask.CompletedTask;
        }

        public ValueTask SendBinaryAsync(ImmutableArray<byte> frame, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        internal async Task<SentRequest> NextAsync()
        {
            Assert.True(await _available.WaitAsync(Timeout));
            Assert.True(_requests.TryDequeue(out var request));
            return request!;
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private int _timerCount;

        internal TaskCompletionSource TimerCreated { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int TimerCount => Volatile.Read(ref _timerCount);

        internal TimeSpan LastDueTime { get; private set; }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            LastDueTime = dueTime;
            var timer = new ManualTimer(callback, state, dueTime);
            lock (_timers)
                _timers.Add(timer);
            Interlocked.Increment(ref _timerCount);
            TimerCreated.TrySetResult();
            return timer;
        }

        internal void Advance(TimeSpan elapsed)
        {
            ManualTimer[] timers;
            lock (_timers)
                timers = _timers.ToArray();
            foreach (var timer in timers)
                timer.Advance(elapsed);
        }

        private sealed class ManualTimer(
            TimerCallback callback,
            object? state,
            TimeSpan remaining) : ITimer
        {
            private int _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                remaining = dueTime;
                return Volatile.Read(ref _disposed) == 0;
            }

            public void Dispose() => Interlocked.Exchange(ref _disposed, 1);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            internal void Advance(TimeSpan elapsed)
            {
                remaining -= elapsed;
                if (remaining <= TimeSpan.Zero && Interlocked.Exchange(ref _disposed, 1) == 0)
                    callback(state);
            }
        }
    }

    private sealed record SentRequest(string Method, string IdJson);
}
