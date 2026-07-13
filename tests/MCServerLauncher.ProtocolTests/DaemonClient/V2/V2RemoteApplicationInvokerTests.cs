using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.DaemonClient.Application;
using MCServerLauncher.DaemonClient.Connection.V2;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests.DaemonClient.V2;

public sealed class V2RemoteApplicationInvokerTests
{
    [Fact]
    public async Task CancellationPrecedesNotReadyAndPreservesCallerToken()
    {
        await using var owner = Owner();
        var invoker = new V2RemoteApplicationInvoker(owner);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            invoker.InvokeAsync(BuiltInProtocolDefinitions.GetSystemInfo, new EmptyRequest(), cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task NotReadyReturnsTypedTransportError()
    {
        await using var owner = Owner();
        var invoker = new V2RemoteApplicationInvoker(owner);

        var result = await invoker.InvokeUnitAsync(
            BuiltInProtocolDefinitions.StartInstance,
            new MCServerLauncher.Common.Contracts.Instances.InstanceReference(Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsErr(out var error));
        var transport = Assert.IsType<TransportDaemonError>(error);
        Assert.Equal("client.not_ready", transport.Code);
        Assert.Equal("The daemon client is not connected and ready.", transport.Message);
    }

    [Fact]
    public async Task ReadyDelegationUsesCurrentCoreAndEpochCloseCompletesTheAdmittedCall()
    {
        var factory = new ControlledSessionFactory();
        await using var owner = new V2ClientConnectionOwner(factory, TimeProvider.System, TimeSpan.Zero);
        var connecting = owner.ConnectAsync();
        var session = await factory.Created.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.RouteSuccess(await session.Transport.NextAsync());
        session.RouteSuccess(await session.Transport.NextAsync(), "{\"version\":0,\"items\":[]}");
        Assert.True((await connecting.WaitAsync(TimeSpan.FromSeconds(5))).IsOk(out _));

        var invoker = new V2RemoteApplicationInvoker(owner);
        var invoke = invoker.InvokeUnitAsync(
            BuiltInProtocolDefinitions.StartInstance,
            new MCServerLauncher.Common.Contracts.Instances.InstanceReference(Guid.NewGuid()),
            CancellationToken.None);
        _ = await session.Transport.NextAsync();
        session.Lose();

        var result = await invoke.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.IsErr(out var error));
        Assert.Equal("connection.closed", error!.Code);
    }

    private static V2ClientConnectionOwner Owner() => new(
        new NeverUsedSessionFactory(),
        TimeProvider.System,
        TimeSpan.Zero);

    private sealed class NeverUsedSessionFactory : IV2ClientConnectionSessionFactory
    {
        public IV2ClientConnectionSession Create(
            MCServerLauncher.DaemonClient.State.RemoteInstanceCatalogMirror mirror,
            Action<V2ClientConnectionCoordinator, JsonRpcRemoteEventNotification> routeEvent,
            Action<V2ClientDiagnostic>? diagnostic = null) =>
            throw new InvalidOperationException("The not-ready invoker must not create a physical session.");
    }

    private sealed class ControlledSessionFactory : IV2ClientConnectionSessionFactory
    {
        internal TaskCompletionSource<ControlledSession> Created { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IV2ClientConnectionSession Create(
            MCServerLauncher.DaemonClient.State.RemoteInstanceCatalogMirror mirror,
            Action<V2ClientConnectionCoordinator, JsonRpcRemoteEventNotification> routeEvent,
            Action<V2ClientDiagnostic>? diagnostic = null)
        {
            var session = new ControlledSession(mirror, routeEvent, diagnostic);
            Created.TrySetResult(session);
            return session;
        }
    }

    private sealed class ControlledSession : IV2ClientConnectionSession
    {
        private readonly TaskCompletionSource<DaemonError> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal ControlledSession(
            MCServerLauncher.DaemonClient.State.RemoteInstanceCatalogMirror mirror,
            Action<V2ClientConnectionCoordinator, JsonRpcRemoteEventNotification> routeEvent,
            Action<V2ClientDiagnostic>? diagnostic)
        {
            Transport = new RecordingWire();
            Coordinator = new V2ClientConnectionCoordinator(
                Transport,
                mirror,
                TimeProvider.System,
                TimeSpan.FromMinutes(1),
                diagnostic: diagnostic,
                nonCatalogEvent: routeEvent);
        }

        internal RecordingWire Transport { get; }
        public V2ClientConnectionCoordinator Coordinator { get; }
        public Task<DaemonError> Completion => _completion.Task;
        public Task<Result<Unit, DaemonError>> ConnectAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok<Unit, DaemonError>(Unit.Default));

        public Task CloseAsync()
        {
            Coordinator.Core.Close();
            _completion.TrySetResult(new TransportDaemonError("connection.closed", "closed"));
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal void Lose()
        {
            Coordinator.Core.Close();
            _completion.TrySetResult(new TransportDaemonError("connection.closed", "closed"));
        }

        internal void RouteSuccess(SentRequest request, string result = "{}") =>
            Coordinator.Core.RouteText(Encoding.UTF8.GetBytes(
                $"{{\"jsonrpc\":\"2.0\",\"id\":{request.IdJson},\"result\":{result}}}"));
    }

    private sealed class RecordingWire : IV2ClientWireTransport
    {
        private readonly Queue<SentRequest> _requests = [];
        private readonly SemaphoreSlim _available = new(0);

        public ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(utf8Json.AsMemory());
            var root = document.RootElement;
            lock (_requests)
            {
                _requests.Enqueue(new SentRequest(root.GetProperty("id").GetRawText()));
            }
            _available.Release();
            return ValueTask.CompletedTask;
        }

        public ValueTask SendBinaryAsync(ImmutableArray<byte> frame, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        internal async Task<SentRequest> NextAsync()
        {
            Assert.True(await _available.WaitAsync(TimeSpan.FromSeconds(5)));
            lock (_requests)
                return _requests.Dequeue();
        }
    }

    private sealed record SentRequest(string IdJson);
}
