using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Events;
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
            ["ConnectionState", "EventRules", "Files", "InstanceCatalog", "Instances", "LastFailure", "System"],
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Select(static property => property.Name)
                .Order());
        Assert.Equal(
            ["ConnectAsync", "DisposeAsync", "SubscribeAsync"],
            type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(static method => !method.IsSpecialName)
                .Select(static method => method.Name)
                .Order());
        Assert.Equal("StateChanged", Assert.Single(type.GetEvents(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)).Name);

        var signatures = type.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(static member => member.ToString() ?? string.Empty);
        foreach (var signature in signatures)
        {
            Assert.DoesNotContain("TouchSocket", signature, StringComparison.Ordinal);
            Assert.DoesNotContain("Connection.V2", signature, StringComparison.Ordinal);
            Assert.DoesNotContain("ActionType", signature, StringComparison.Ordinal);
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
        client.StateChanged += state =>
        {
            states.Enqueue(state);
            return Task.CompletedTask;
        };

        var session = await MakeReadyAsync(client, factory);
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

    private static MCServerLauncher.DaemonClient.DaemonClient Client(ControlledSessionFactory factory) =>
        new(new V2ClientConnectionOwner(factory, TimeProvider.System, TimeSpan.FromSeconds(3)));

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

        internal void RouteNotification(string notification) =>
            Coordinator.Core.RouteText(Encoding.UTF8.GetBytes(notification));
    }

    private sealed class ScriptedTransport : IV2ClientWireTransport
    {
        private readonly ConcurrentQueue<SentRequest> _requests = new();
        private readonly SemaphoreSlim _available = new(0);

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

    private sealed record SentRequest(string Method, string IdJson);
}
