using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.DaemonClient.State;
using RustyOptions;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Http.WebSockets;
using TouchSocket.Sockets;

namespace MCServerLauncher.DaemonClient.Connection.V2;

internal sealed class TouchSocketV2ClientConnectionSession : IV2ClientConnectionSession, IV2ClientWireTransport
{
    private const string ClosedCode = "connection.closed";
    private const long InboundTerminalMask = long.MinValue;
    private readonly object _gate = new();
    private readonly Uri _endpoint;
    private readonly string _token;
    private readonly WebSocketClient _client = new();
    private readonly TouchSocketV2MessageAssembler _assembler = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly TaskCompletionSource<DaemonError> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<V2ClientDiagnostic>? _diagnostic;
    private readonly Func<Task> _beforeConnectFailureArbitration;
    private Task<RustyOptions.Result<Unit, DaemonError>>? _connectTask;
    private Task? _closeTask;
    private SessionPhase _phase;
    private bool _peerClosedDuringConnect;
    private bool _setupCompleted;
    private bool _disposed;
    private long _inboundState;

    internal TouchSocketV2ClientConnectionSession(
        Uri endpoint,
        string token,
        RemoteInstanceCatalogMirror mirror,
        Action<V2ClientConnectionCoordinator, JsonRpcRemoteEventNotification> routeEvent,
        TimeProvider timeProvider,
        TimeSpan requestTimeout,
        Action<V2ClientDiagnostic>? diagnostic = null,
        Func<Task>? beforeConnectFailureArbitration = null)
    {
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        _token = token;
        _diagnostic = diagnostic;
        _beforeConnectFailureArbitration = beforeConnectFailureArbitration ?? (static () => Task.CompletedTask);
        Coordinator = new V2ClientConnectionCoordinator(
            this,
            mirror ?? throw new ArgumentNullException(nameof(mirror)),
            timeProvider ?? throw new ArgumentNullException(nameof(timeProvider)),
            requestTimeout,
            diagnostic: HandleDiagnostic,
            nonCatalogEvent: routeEvent ?? throw new ArgumentNullException(nameof(routeEvent)));
    }

    public V2ClientConnectionCoordinator Coordinator { get; }

    public Task<DaemonError> Completion => _completion.Task;

    public Task<RustyOptions.Result<Unit, DaemonError>> ConnectAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_closeTask is not null)
                return Task.FromResult(RustyOptions.Result.Err<Unit, DaemonError>(ClosedError()));
            if (_connectTask is not null)
                return _connectTask;
            _phase = SessionPhase.Connecting;
            return _connectTask = ConnectCoreAsync(cancellationToken);
        }
    }

    public ValueTask SendTextAsync(
        ImmutableArray<byte> utf8Json,
        CancellationToken cancellationToken)
    {
        if (utf8Json.IsDefault)
            throw new ArgumentException("The V2 text payload must be initialized.", nameof(utf8Json));
        return new ValueTask(SendTextCoreAsync(utf8Json, cancellationToken));
    }

    public ValueTask SendBinaryAsync(
        ImmutableArray<byte> frame,
        CancellationToken cancellationToken)
    {
        if (frame.IsDefault)
            throw new ArgumentException("The V2 binary frame must be initialized.", nameof(frame));
        return new ValueTask(SendBinaryCoreAsync(frame, cancellationToken));
    }

    public Task CloseAsync()
    {
        lock (_gate)
        {
            if (_closeTask is not null)
                return _closeTask;
            _phase = SessionPhase.Closing;
            return _closeTask = CloseCoreAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        _lifetimeCancellation.Dispose();
    }

    internal async Task HandleFrameAsync(WSDataFrame frame, Func<Task>? invokeNext = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        invokeNext ??= static () => Task.CompletedTask;
        try
        {
            switch (frame.Opcode)
            {
                case WSDataType.Ping:
                case WSDataType.Pong:
                    ValidateControlFrame(frame);
                    break;
                case WSDataType.Close:
                    ValidateControlFrame(frame);
                    HandlePeerClosed();
                    break;
                default:
                    if (_assembler.TryAssemble(frame, out var message))
                    {
                        using (message)
                        {
                            var payload = message.PayloadData.ToArray();
                            RouteInbound(message.Opcode, payload);
                        }
                    }
                    break;
            }
            await invokeNext().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Complete(exception is InvalidDataException
                ? new TransportDaemonError(
                    "protocol.websocket_message_invalid",
                    "The daemon sent an invalid V2 WebSocket message.")
                : new TransportDaemonError(
                    "transport.receive_failed",
                    $"The V2 WebSocket receive callback failed: {exception.GetType().Name}."));
        }
    }

    internal Task HandleClosedAsync(Func<Task>? invokeNext = null)
    {
        HandlePeerClosed();
        return (invokeNext ?? (static () => Task.CompletedTask))();
    }

    private async Task<RustyOptions.Result<Unit, DaemonError>> ConnectCoreAsync(CancellationToken callerToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            callerToken,
            _lifetimeCancellation.Token);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            var config = new TouchSocketConfig()
                .SetRemoteIPHost(new IPHost(BuildAuthenticatedEndpoint(_endpoint, _token)))
                .ConfigurePlugins(plugins => plugins.Add(new SessionPlugin(this)));
            await _client.SetupAsync(config).ConfigureAwait(false);
            lock (_gate)
                _setupCompleted = true;
            await _client.ConnectAsync(linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            if (!FinishConnectSuccess())
                return RustyOptions.Result.Err<Unit, DaemonError>(ClosedError());
            return RustyOptions.Result.Ok<Unit, DaemonError>(Unit.Default);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            FinishConnectWithoutCompletion();
            await CleanupFailedConnectAsync().ConfigureAwait(false);
            callerToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            await CleanupFailedConnectAsync().ConfigureAwait(false);
            return RustyOptions.Result.Err<Unit, DaemonError>(ClosedError());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await _beforeConnectFailureArbitration().ConfigureAwait(false);
            var error = new TransportDaemonError(
                "transport.connect_failed",
                $"The V2 WebSocket connection could not be established: {exception.GetType().Name}.");
            var resolution = ResolveConnectFailure(error, callerToken.IsCancellationRequested);
            await CleanupFailedConnectAsync().ConfigureAwait(false);
            if (resolution.CallerCanceled)
            {
                callerToken.ThrowIfCancellationRequested();
                throw new OperationCanceledException(callerToken);
            }
            return RustyOptions.Result.Err<Unit, DaemonError>(resolution.Error!);
        }
    }

    private async Task SendTextCoreAsync(
        ImmutableArray<byte> utf8Json,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _client.SendAsync(
                utf8Json.AsMemory(),
                WSDataType.Text,
                true,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Complete(new TransportDaemonError(
                "transport.send_failed",
                $"The V2 WebSocket send failed: {exception.GetType().Name}."));
            throw;
        }
    }

    private async Task SendBinaryCoreAsync(
        ImmutableArray<byte> frame,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _client.SendAsync(
                frame.AsMemory(),
                WSDataType.Binary,
                true,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Complete(new TransportDaemonError(
                "transport.send_failed",
                $"The V2 WebSocket send failed: {exception.GetType().Name}."));
            throw;
        }
    }

    private async Task CloseCoreAsync()
    {
        Complete(ClosedError());
        try
        {
            _lifetimeCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        Task<RustyOptions.Result<Unit, DaemonError>>? connectTask;
        bool setupCompleted;
        lock (_gate)
        {
            connectTask = _connectTask;
            setupCompleted = _setupCompleted;
        }
        if (connectTask is not null)
        {
            try
            {
                await connectTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _assembler.Clear();
        if (setupCompleted)
        {
            try
            {
                await _client.CloseAsync("V2 client session closing", CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
            }
        }
        _client.SafeDispose();
        lock (_gate)
            _phase = SessionPhase.Closed;
    }

    private Task CleanupFailedConnectAsync()
    {
        try
        {
            _client.SafeDispose();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
        return Task.CompletedTask;
    }

    private void HandleDiagnostic(V2ClientDiagnostic diagnostic)
    {
        if (diagnostic.Kind == V2ClientDiagnosticKind.ProtocolFault)
        {
            FailProtocol(
                "protocol.envelope_invalid",
                "The daemon sent a text message that violates the V2 JSON-RPC profile.");
        }
        _diagnostic?.Invoke(diagnostic);
    }

    private void FailProtocol(string code, string message) =>
        Complete(new TransportDaemonError(code, message));

    private static void ValidateControlFrame(WSDataFrame frame)
    {
        if (!frame.FIN || frame.PayloadData.Length > 125 ||
            (frame.Opcode == WSDataType.Close && frame.PayloadData.Length == 1))
            throw new InvalidDataException("A WebSocket control frame is invalid.");
    }

    private void Complete(DaemonError error)
    {
        if (!TryEnterTerminal())
            return;

        // The terminal bit blocks callbacks arriving after this point before Core can observe them.
        Coordinator.Core.Close();
        _assembler.Clear();
        _completion.TrySetResult(error);
    }

    private bool FinishConnectSuccess()
    {
        var peerClosed = false;
        lock (_gate)
        {
            if (_phase != SessionPhase.Connecting)
                return false;
            _phase = SessionPhase.Connected;
            if (_peerClosedDuringConnect)
            {
                _peerClosedDuringConnect = false;
                peerClosed = true;
            }
        }
        if (peerClosed)
            Complete(PeerClosedError());
        return true;
    }

    private ConnectFailureResolution ResolveConnectFailure(
        DaemonError error,
        bool callerCanceled)
    {
        DaemonError? terminal = null;
        ConnectFailureResolution resolution;
        lock (_gate)
        {
            if (_phase is SessionPhase.Closing or SessionPhase.Closed)
            {
                resolution = new(false, ClosedError());
            }
            else if (_phase == SessionPhase.Connecting && callerCanceled)
            {
                _phase = SessionPhase.ConnectTerminated;
                _peerClosedDuringConnect = false;
                resolution = new(true, null);
            }
            else if (_phase == SessionPhase.Connecting)
            {
                _phase = SessionPhase.ConnectTerminated;
                _peerClosedDuringConnect = false;
                terminal = error;
                resolution = new(false, error);
            }
            else
            {
                resolution = callerCanceled
                    ? new(true, null)
                    : new(false, _completion.Task.IsCompletedSuccessfully
                        ? _completion.Task.Result
                        : ClosedError());
            }
        }
        if (terminal is not null)
            Complete(terminal);
        return resolution;
    }

    private void FinishConnectWithoutCompletion()
    {
        lock (_gate)
        {
            if (_phase != SessionPhase.Connecting)
                return;
            _phase = SessionPhase.ConnectTerminated;
            _peerClosedDuringConnect = false;
        }
    }

    private void HandlePeerClosed()
    {
        var peerClosed = false;
        lock (_gate)
        {
            if (_phase == SessionPhase.Connecting)
            {
                _peerClosedDuringConnect = true;
                return;
            }
            if (_phase != SessionPhase.Connected)
                return;
            peerClosed = true;
        }
        if (peerClosed)
            Complete(PeerClosedError());
    }

    private void RouteInbound(WSDataType opcode, ReadOnlySpan<byte> payload)
    {
        if (!TryEnterInboundRoute())
            return;

        try
        {
            if (opcode == WSDataType.Text)
                Coordinator.Core.RouteText(payload);
            else
                Coordinator.Core.RouteBinary(payload);
        }
        finally
        {
            ExitInboundRoute();
        }
    }

    private bool TryEnterInboundRoute()
    {
        while (true)
        {
            var current = Volatile.Read(ref _inboundState);
            if ((current & InboundTerminalMask) != 0)
                return false;
            if (Interlocked.CompareExchange(ref _inboundState, current + 1, current) == current)
                return true;
        }
    }

    private void ExitInboundRoute() => Interlocked.Decrement(ref _inboundState);

    private bool TryEnterTerminal()
    {
        while (true)
        {
            var current = Volatile.Read(ref _inboundState);
            if ((current & InboundTerminalMask) != 0)
                return false;
            if (Interlocked.CompareExchange(ref _inboundState, current | InboundTerminalMask, current) == current)
                return true;
        }
    }

    internal static string BuildAuthenticatedEndpoint(Uri endpoint, string token)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return $"{endpoint.GetLeftPart(UriPartial.Path)}?token={Uri.EscapeDataString(token)}";
    }

    private static TransportDaemonError ClosedError() =>
        new(ClosedCode, "The V2 WebSocket session is closed.");

    private static TransportDaemonError PeerClosedError() =>
        new("transport.peer_closed", "The daemon closed the V2 WebSocket session.");

    private enum SessionPhase
    {
        Created,
        Connecting,
        Connected,
        ConnectTerminated,
        Closing,
        Closed
    }

    private readonly record struct ConnectFailureResolution(
        bool CallerCanceled,
        DaemonError? Error);

    private sealed class SessionPlugin(TouchSocketV2ClientConnectionSession session) : PluginBase,
        IWebSocketReceivedPlugin,
        IWebSocketClosedPlugin
    {
        public Task OnWebSocketReceived(IWebSocket webSocket, WSDataFrameEventArgs e) =>
            session.HandleFrameAsync(e.DataFrame, e.InvokeNext);

        public Task OnWebSocketClosed(IWebSocket webSocket, ClosedEventArgs e) =>
            session.HandleClosedAsync(e.InvokeNext);
    }
}
