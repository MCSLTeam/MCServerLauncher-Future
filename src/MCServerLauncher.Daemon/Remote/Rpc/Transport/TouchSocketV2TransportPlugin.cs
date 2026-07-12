using System.Collections.Concurrent;
using System.Net.WebSockets;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Dispatch;
using MCServerLauncher.Daemon.Remote.Rpc.Events;
using MCServerLauncher.Daemon.Remote.Rpc.Files;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Http.WebSockets;
using TouchSocket.Sockets;

namespace MCServerLauncher.Daemon.Remote.Rpc.Transport;

internal sealed class TouchSocketV2TransportPlugin : PluginBase,
    IWebSocketConnectedPlugin, IWebSocketReceivedPlugin, IWebSocketClosedPlugin, IAsyncDisposable
{
    internal const string Endpoint = "/api/v2";
    private readonly IDaemonApplication _application;
    private readonly FrozenProtocolCatalog _catalog;
    private readonly V2EventConnectionRegistry _events;
    private readonly IV2RpcDiagnosticSink _rpcDiagnostics;
    private readonly IV2InboundDiagnosticSink _inboundDiagnostics;
    private readonly TimeProvider _timeProvider;
    private readonly Func<Task> _beforeStartExpiry;
    private readonly ConcurrentDictionary<string, ConnectionState> _connections = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _v2Connections = new(StringComparer.Ordinal);
    private int _stopping;

    internal TouchSocketV2TransportPlugin(IDaemonApplication application, FrozenProtocolCatalog catalog,
        V2EventConnectionRegistry events, IV2RpcDiagnosticSink rpcDiagnostics,
        IV2InboundDiagnosticSink inboundDiagnostics, TimeProvider? timeProvider = null,
        Func<Task>? beforeStartExpiry = null)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _rpcDiagnostics = rpcDiagnostics ?? throw new ArgumentNullException(nameof(rpcDiagnostics));
        _inboundDiagnostics = inboundDiagnostics ?? throw new ArgumentNullException(nameof(inboundDiagnostics));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _beforeStartExpiry = beforeStartExpiry ?? (() => Task.CompletedTask);
    }

    public async Task OnWebSocketConnected(IWebSocket webSocket, HttpContextEventArgs e)
    {
        if (!StringComparer.Ordinal.Equals(e.Context.Request.RelativeURL, Endpoint))
        {
            await e.InvokeNext().ConfigureAwait(false);
            return;
        }
        var connectionId = ConnectionId(webSocket);
        _v2Connections.TryAdd(connectionId, 0);
        var token = e.Context.Request.Query["token"].First;
        if (token is null || !TryAuthenticateToken(token, _timeProvider, out var verified))
        {
            await RejectV2Async(connectionId,
                () => CloseDirectAsync(webSocket, WebSocketCloseStatus.PolicyViolation, "Invalid V2 token"))
                .ConfigureAwait(false);
            return;
        }
        var permissionText = verified.Permissions;
        var validTo = verified.ValidTo.UtcDateTime;
        var permissions = permissionText?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        await HandleConnectedAsync(
            e.Context.Request.RelativeURL,
            connectionId,
            permissions,
            new TouchSocketV2OutboundSender(webSocket),
            e.InvokeNext,
            () => CloseDirectAsync(webSocket, WebSocketCloseStatus.EndpointUnavailable, "V2 transport stopping"),
            () => CloseDirectAsync(webSocket, WebSocketCloseStatus.InternalServerError, "V2 connection setup failed"),
            validTo == DateTime.MaxValue ? DateTimeOffset.MaxValue : new DateTimeOffset(validTo, TimeSpan.Zero)).ConfigureAwait(false);
    }

    internal async Task HandleConnectedAsync(
        string relativeUrl,
        string connectionId,
        IEnumerable<string> permissions,
        IV2OutboundSender sender,
        Func<Task> invokeNext,
        Func<Task> rejectStopping,
        Func<Task> rejectSetup,
        DateTimeOffset? expiresAt = null)
    {
        ArgumentNullException.ThrowIfNull(relativeUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(invokeNext);
        ArgumentNullException.ThrowIfNull(rejectStopping);
        ArgumentNullException.ThrowIfNull(rejectSetup);

        if (!StringComparer.Ordinal.Equals(relativeUrl, Endpoint))
        {
            await invokeNext().ConfigureAwait(false);
            return;
        }
        _v2Connections.TryAdd(connectionId, 0);
        if (Volatile.Read(ref _stopping) != 0)
        {
            await RejectV2Async(connectionId, rejectStopping).ConfigureAwait(false);
            return;
        }

        V2ConnectionOwner? owner = null;
        ConnectionState? state = null;
        try
        {
            owner = new V2ConnectionOwner(sender, permissions, _timeProvider);
            if (_events.TryAttach(connectionId, owner, out var eventEntry) != V2EventConnectionAttachResult.Attached)
                throw new InvalidOperationException("The V2 event connection could not be attached.");
            var filesResult = V2FileSessionConnection.Attach(_application.Files, _catalog, owner, _timeProvider);
            if (filesResult.IsErr(out _)) throw new InvalidOperationException("The V2 file connection could not be attached.");
            var files = filesResult.Unwrap();
            var context = new V2RpcConnectionContext(owner, eventEntry!.Ledger, owner.ConnectionToken, files);
            var pipeline = new V2InboundMessagePipeline(_catalog, new V2RpcDispatcher(_catalog, _rpcDiagnostics), context, owner, files, _inboundDiagnostics);
            state = new ConnectionState(owner, pipeline);
            if (Volatile.Read(ref _stopping) != 0) throw new InvalidOperationException("The V2 transport is stopping.");
            if (!_connections.TryAdd(connectionId, state)) throw new InvalidOperationException("Duplicate V2 connection identifier.");
            if (Volatile.Read(ref _stopping) != 0)
            {
                await RemoveAndCloseAsync(connectionId, V2ConnectionCloseReason.Abort).ConfigureAwait(false);
                return;
            }
            var pump = owner.Start();
            Observe(MonitorOwnerAsync(connectionId, state, pump));
            if (expiresAt is { } expiry && expiry != DateTimeOffset.MaxValue)
            {
                await _beforeStartExpiry().ConfigureAwait(false);
                state.StartExpiry(token => ExpireAsync(connectionId, state, expiry, token));
            }
        }
        catch
        {
            if (state is not null)
                await state.StopExpiryAsync().ConfigureAwait(false);
            if (owner is not null)
            {
                try { await owner.AbortAsync().ConfigureAwait(false); }
                catch (Exception exception) { _inboundDiagnostics.RecordUnexpected(connectionId, exception); }
            }
            else await RejectV2Async(connectionId, rejectSetup).ConfigureAwait(false);
        }
    }

    public async Task OnWebSocketReceived(IWebSocket webSocket, WSDataFrameEventArgs e)
    {
        var connectionId = ConnectionId(webSocket);
        await HandleFrameAsync(connectionId, webSocket.GetMessageCombinator(), e.DataFrame, e.InvokeNext)
            .ConfigureAwait(false);
    }

    internal async Task HandleFrameAsync(
        string connectionId,
        WebSocketMessageCombinator combinator,
        WSDataFrame frame,
        Func<Task> invokeNext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(combinator);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(invokeNext);
        if (!_v2Connections.ContainsKey(connectionId))
        {
            await invokeNext().ConfigureAwait(false);
            return;
        }
        if (!_connections.TryGetValue(connectionId, out var connectionState))
            return;

        WebSocketMessage message;
        try
        {
            var assembler = connectionState.GetAssembler(combinator);
            if (!assembler.TryAssemble(frame, out message))
                return;
        }
        catch
        {
            BeginRemoveAndClose(connectionId, V2ConnectionCloseReason.Abort);
            return;
        }
        using (message)
            await HandleReceivedAsync(connectionId, message.Opcode, message.PayloadData, invokeNext).ConfigureAwait(false);
    }

    internal async Task HandleReceivedAsync(
        string connectionId,
        WSDataType opcode,
        ReadOnlyMemory<byte> payload,
        Func<Task> invokeNext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(invokeNext);
        if (!_v2Connections.ContainsKey(connectionId))
        {
            await invokeNext().ConfigureAwait(false);
            return;
        }
        if (!_connections.TryGetValue(connectionId, out var state))
            return;
        switch (opcode)
        {
            case WSDataType.Text:
                await RemoveIfClosingAsync(connectionId,
                    await state.Pipeline.ReceiveTextAsync(payload, state.Owner.ConnectionToken).ConfigureAwait(false)).ConfigureAwait(false);
                break;
            case WSDataType.Binary:
                await RemoveIfClosingAsync(connectionId,
                    await state.Pipeline.ReceiveBinaryAsync(payload, state.Owner.ConnectionToken).ConfigureAwait(false)).ConfigureAwait(false);
                break;
            case WSDataType.Ping:
            case WSDataType.Pong:
                break;
            default: BeginRemoveAndClose(connectionId, V2ConnectionCloseReason.Abort); break;
        }
    }

    public async Task OnWebSocketClosed(IWebSocket webSocket, ClosedEventArgs e)
    {
        await HandleClosedAsync(ConnectionId(webSocket), e.InvokeNext).ConfigureAwait(false);
    }

    internal async Task HandleClosedAsync(string connectionId, Func<Task> invokeNext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(invokeNext);
        if (!_v2Connections.TryRemove(connectionId, out _))
        {
            await invokeNext().ConfigureAwait(false);
            return;
        }
        await RemoveAndCloseAsync(connectionId, V2ConnectionCloseReason.Peer).ConfigureAwait(false);
    }

    internal int ConnectionCount => _connections.Count;
    internal int V2ConnectionMarkerCount => _v2Connections.Count;

    internal static bool TryAuthenticateToken(string token, TimeProvider timeProvider, out V2VerifiedToken verified) =>
        TryAuthenticateToken(token, timeProvider, JwtUtils.ValidateToken, JwtUtils.ReadToken, out verified);

    internal static bool TryAuthenticateToken(string token, TimeProvider timeProvider,
        Func<string, bool> validate,
        Func<string, (Guid JTI, string? Permissions, DateTime ValidTo)> read,
        out V2VerifiedToken verified)
    {
        verified = default;
        try
        {
            if (!validate(token)) return false;
            var (jti, permissions, validTo) = read(token);
            if (permissions is null) return false;
            var expiry = validTo == DateTime.MaxValue ? DateTimeOffset.MaxValue : new DateTimeOffset(validTo, TimeSpan.Zero);
            if (expiry <= timeProvider.GetUtcNow()) return false;
            verified = new V2VerifiedToken(jti, permissions, expiry);
            return true;
        }
        catch { return false; }
    }

    internal async Task ShutdownAsync()
    {
        Interlocked.Exchange(ref _stopping, 1);
        while (!_connections.IsEmpty)
            await Task.WhenAll(_connections.Keys.Select(id => RemoveAndCloseAsync(id, V2ConnectionCloseReason.Abort))).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await ShutdownAsync().ConfigureAwait(false);

    private async Task RemoveAndCloseAsync(string id, V2ConnectionCloseReason reason)
    {
        if (_connections.TryRemove(id, out var state))
        {
            state.ClearAssembler();
            var close = state.Owner.AbortAsync(reason);
            await state.StopExpiryAsync().ConfigureAwait(false);
            await close.ConfigureAwait(false);
        }
    }

    private Task RemoveIfClosingAsync(string connectionId, V2InboundOutcome outcome)
    {
        if (outcome.Disposition is V2InboundDisposition.ConnectionAborted or V2InboundDisposition.ConnectionClosing)
            BeginRemoveAndClose(connectionId, V2ConnectionCloseReason.Abort);
        return Task.CompletedTask;
    }

    private void BeginRemoveAndClose(string id, V2ConnectionCloseReason reason)
    {
        if (!_connections.TryRemove(id, out var state)) return;
        state.ClearAssembler();
        var close = state.Owner.AbortAsync(reason);
        Observe(FinalizeRemovedStateAsync(state, close));
    }

    private async Task FinalizeRemovedStateAsync(ConnectionState state, Task close)
    {
        await state.StopExpiryAsync().ConfigureAwait(false);
        await close.ConfigureAwait(false);
    }

    private async Task ExpireAsync(
        string id,
        ConnectionState state,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var delay = expiresAt - _timeProvider.GetUtcNow();
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
            if (_connections.TryRemove(new KeyValuePair<string, ConnectionState>(id, state)))
            {
                state.ClearAssembler();
                await state.Owner.AbortAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task MonitorOwnerAsync(string id, ConnectionState state, Task pump)
    {
        try
        {
            await pump.ConfigureAwait(false);
            await state.Owner.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _inboundDiagnostics.RecordUnexpected(Guid.NewGuid().ToString("N"), exception);
        }
        finally
        {
            _connections.TryRemove(new KeyValuePair<string, ConnectionState>(id, state));
            state.ClearAssembler();
            await state.StopExpiryAsync().ConfigureAwait(false);
        }
    }

    internal async Task RejectV2Async(string connectionId, Func<Task> close)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(close);
        _v2Connections.TryAdd(connectionId, 0);
        try { await close().ConfigureAwait(false); }
        catch (Exception exception) { _inboundDiagnostics.RecordUnexpected(connectionId, exception); }
    }

    internal static async Task CloseDirectAsync(
        IWebSocket webSocket,
        WebSocketCloseStatus status,
        string description)
    {
        Exception? lastFailure = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var result = await webSocket.CloseAsync(status, description).ConfigureAwait(false);
                if (result.IsSuccess)
                    return;
                lastFailure = new IOException($"TouchSocket WebSocket close failed: {result.Message}");
            }
            catch (Exception exception)
            {
                lastFailure = exception;
            }
        }
        throw new IOException("TouchSocket WebSocket close failed after retry.", lastFailure);
    }

    private static void Observe(Task task) => _ = task.ContinueWith(static completed => _ = completed.Exception,
        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);

    private static string ConnectionId(IWebSocket webSocket) =>
        webSocket.Client is IHttpSessionClient client ? client.Id : throw new InvalidOperationException("WebSocket has no HTTP session client.");

    private sealed class ConnectionState(V2ConnectionOwner owner, V2InboundMessagePipeline pipeline)
    {
        private readonly object _expiryGate = new();
        private readonly CancellationTokenSource _expiryCancellation = new();
        private Task? _expiryTask;
        private bool _expiryStopped;
        private TouchSocketV2MessageAssembler? _assembler;

        internal V2ConnectionOwner Owner { get; } = owner;
        internal V2InboundMessagePipeline Pipeline { get; } = pipeline;

        internal TouchSocketV2MessageAssembler GetAssembler(WebSocketMessageCombinator combinator)
        {
            lock (_expiryGate)
                return _assembler ??= new TouchSocketV2MessageAssembler(combinator);
        }

        internal void ClearAssembler()
        {
            TouchSocketV2MessageAssembler? assembler;
            lock (_expiryGate)
            {
                assembler = _assembler;
                _assembler = null;
            }
            assembler?.Clear();
        }

        internal void StartExpiry(Func<CancellationToken, Task> start)
        {
            ArgumentNullException.ThrowIfNull(start);
            Task task;
            lock (_expiryGate)
            {
                if (_expiryStopped)
                    return;
                task = start(_expiryCancellation.Token);
                _expiryTask = task;
            }
            Observe(task);
        }

        internal async Task StopExpiryAsync()
        {
            Task? task;
            var disposeWithoutTask = false;
            lock (_expiryGate)
            {
                if (!_expiryStopped)
                {
                    _expiryStopped = true;
                    _expiryCancellation.Cancel();
                }
                task = _expiryTask;
                disposeWithoutTask = task is null;
            }

            if (task is not null)
            {
                try { await task.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch { }
            }
            lock (_expiryGate)
            {
                if (_expiryTask is null || _expiryTask.IsCompleted)
                    _expiryCancellation.Dispose();
                else if (disposeWithoutTask)
                    _expiryCancellation.Dispose();
            }
        }
    }
}

internal readonly record struct V2VerifiedToken(Guid Jti, string Permissions, DateTimeOffset ValidTo);

internal sealed class TouchSocketV2MessageAssembler(WebSocketMessageCombinator combinator)
{
    internal const int MaxTextMessageSize = 10 * 1024 * 1024;
    internal const int MaxBinaryMessageSize = BinaryFrameCodec.HeaderSize + (int)BinaryFrameCodec.DefaultMaximumChunkSize;
    private readonly WebSocketMessageCombinator _combinator = combinator ?? throw new ArgumentNullException(nameof(combinator));
    private readonly object _gate = new();
    private bool _combining;
    private int _payloadLength;
    private WSDataType _initialOpcode;

    internal bool TryAssemble(WSDataFrame frame, out WebSocketMessage message)
    {
        lock (_gate)
            return TryAssembleLocked(frame, out message);
    }

    private bool TryAssembleLocked(WSDataFrame frame, out WebSocketMessage message)
    {
        ArgumentNullException.ThrowIfNull(frame);
        try
        {
            if (frame.Opcode is WSDataType.Ping or WSDataType.Pong or WSDataType.Close)
            {
                if (!frame.FIN || frame.PayloadData.Length > 125)
                    throw new InvalidDataException("A WebSocket control frame must be final and at most 125 bytes.");
                var payload = frame.PayloadData.ToArray();
                message = new WebSocketMessage(frame.Opcode, payload, static () => { });
                return true;
            }

            switch (frame.Opcode)
            {
                case WSDataType.Text:
                case WSDataType.Binary:
                    if (_combining)
                        throw new InvalidDataException("A fragmented V2 message cannot be interleaved with another data message.");
                    _initialOpcode = frame.Opcode;
                    CheckLength(frame.PayloadData.Length, LimitFor(frame.Opcode));
                    if (!frame.FIN)
                    {
                        _combining = true;
                        _payloadLength = frame.PayloadData.Length;
                    }
                    break;
                case WSDataType.Cont:
                    if (!_combining)
                        throw new InvalidDataException("A V2 continuation frame has no initial data frame.");
                    CheckLength(checked(_payloadLength + frame.PayloadData.Length), LimitFor(_initialOpcode));
                    _payloadLength += frame.PayloadData.Length;
                    break;
                default:
                    throw new InvalidDataException($"Unsupported V2 WebSocket opcode '{frame.Opcode}'.");
            }

            var complete = _combinator.TryCombine(frame, out message);
            if (frame.FIN && frame.Opcode is WSDataType.Text or WSDataType.Binary or WSDataType.Cont)
            {
                if (!complete)
                    throw new InvalidDataException("TouchSocket did not complete a final V2 data frame.");
                _combining = false;
                _payloadLength = 0;
                _initialOpcode = default;
            }
            if (complete)
            {
                var opcode = message.Opcode;
                var payload = message.PayloadData.ToArray();
                message.Dispose();
                _combinator.Clear();
                message = new WebSocketMessage(opcode, payload, static () => { });
            }
            return complete;
        }
        catch
        {
            _combinator.Clear();
            _combining = false;
            _payloadLength = 0;
            _initialOpcode = default;
            message = default;
            throw;
        }
    }

    internal void Clear()
    {
        lock (_gate)
        {
            _combinator.Clear();
            _combining = false;
            _payloadLength = 0;
            _initialOpcode = default;
        }
    }

    private static int LimitFor(WSDataType opcode) => opcode == WSDataType.Binary
        ? MaxBinaryMessageSize
        : MaxTextMessageSize;

    private static void CheckLength(int length, int limit)
    {
        if (length > limit)
            throw new InvalidDataException($"A V2 inbound message cannot exceed {limit} bytes.");
    }
}
