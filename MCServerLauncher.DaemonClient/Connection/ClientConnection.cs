using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.DaemonClient.Serialization;
using MCServerLauncher.DaemonClient.WebSocketPlugin;
using Serilog;
using TouchSocket.Http.WebSockets;

namespace MCServerLauncher.DaemonClient.Connection;

internal class ClientConnection : DisposableObject
{
    private static readonly JsonSerializerOptions RpcStjOptions = DaemonClientRpcJsonBoundary.StjOptions;

    private const int CF_PROTOCOL_VERSION = 1;

    private readonly CancellationTokenSource _cts;
    private readonly ConnectionPendingRequests _pendingRequests;
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<ActionResponse>> _pendingResponses = new();
    private readonly TouchSocketClientTransport _transport;

    private ClientConnection(ClientConnectionConfig config)
    {
        _cts = new CancellationTokenSource();
        _pendingRequests = new ConnectionPendingRequests(config.PendingRequestCapacity);
        _transport = new TouchSocketClientTransport(config);
        Config = config;

        _transport.EventReceived += (t, l, m, d) => OnEventReceived?.Invoke(t, l, m, d);
        _transport.ActionResponseReceived += HandleActionResponse;
        _transport.Reconnected += async () =>
        {
            Reconnected?.Invoke();
            await OnReconnectedEventHandler();
        };
        _transport.ConnectionLost += () =>
        {
            _pendingRequests.Close();
            CancelPendingResponses();
            ConnectionLost?.Invoke();
        };
        _transport.ConnectionClosed += () =>
        {
            _pendingRequests.Close();
            CancelPendingResponses();
            ConnectionClosed?.Invoke();
        };
    }

    public SubscribedEvents SubscribedEvents { get; } = new();
    public DateTime LastPong => _transport.LastPong;
    public bool IsConnectionLost => _transport.IsConnectionLost;
    public WebSocketClient Client => _transport.Client;
    public ClientConnectionConfig Config { get; }

    public event Action? ConnectionLost;
    public event Action? Reconnected;
    public event Action? ConnectionClosed;

    public event Action<EventType, long, IEventMeta?, IEventData?>? OnEventReceived;

    /// <summary>
    ///     建立连接
    /// </summary>
    /// <param name="address">ip地址</param>
    /// <param name="port">端口</param>
    /// <param name="token">jwt</param>
    /// <param name="isSecure">是否使用SSL</param>
    /// <param name="config">连接配置</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <exception cref="WebSocketException">WebSocket连接失败</exception>
    /// <exception cref="TimeoutException">连接超时</exception>
    /// <returns></returns>
    public static async Task<ClientConnection> OpenAsync(
        string address,
        int port,
        string token,
        bool isSecure,
        ClientConnectionConfig config,
        CancellationToken cancellationToken = default
    )
    {
        // create instance
        ClientConnection connection = new(config);
        await connection._transport.OpenAsync(address, port, token, isSecure, cancellationToken);
        return connection;
    }

    public Task SendAsync(ActionType actionType,
        IActionParameter? param,
        CancellationToken ct)
    {
        ThrowIfInvalidState();
        return PrivateSendAsync(new ActionRequest
        {
            ActionType = actionType,
            Parameter = SerializeParameterForTransport(param),
            Id = Guid.NewGuid()
        }, ct);
    }

    public async Task<TResult> RequestAsync<TResult>(
        ActionType actionType,
        IActionParameter? param,
        int timeout = -1,
        CancellationToken ct = default
    )
        where TResult : class, IActionResult
    {
        ThrowIfInvalidState();
        var rv = await PrivateRequestAsync<TResult>(actionType, param, timeout, ct);
        return rv!;
    }

    public async Task RequestAsync(
        ActionType actionType,
        IActionParameter? param,
        int timeout = -1,
        CancellationToken ct = default
    )
    {
        ThrowIfInvalidState();
        await PrivateRequestAsync<EmptyActionResult>(actionType, param, timeout, ct);
    }

    /// <summary>
    ///     关闭连接
    /// </summary>
    public async Task CloseAsync()
    {
        _cts.Cancel();
        await _transport.CloseAsync();
        SubscribedEvents.Events.Clear(); // 清空标记的已订阅事件
        Log.Debug("[ClientConnection] closed");
    }

    private void ThrowIfInvalidState()
    {
        if (!Client.Online) throw new InvalidOperationException("websocket is offline");
    }

    /// <summary>
    ///     发送一个action
    /// </summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    private Task PrivateSendAsync(
        ActionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var json = SerializeActionRequestForTransport(request);
        return _transport.SendAsync(new ReadOnlyMemory<byte>(json), cancellationToken);
    }

    internal static byte[] SerializeActionRequestForTransport(ActionRequest request)
    {
        if (!DaemonClientTransportInstrumentationScope.TryGetCurrent(out var instrumentation))
            return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(request, RpcStjOptions);

        var startTimestamp = Stopwatch.GetTimestamp();
        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(request, RpcStjOptions);
        instrumentation.OnOutboundSerialize(
            DaemonClientTransportStopwatch.GetElapsedTime(startTimestamp),
            payload.Length);
        return payload;
    }

    /// <summary>
    ///     一个RPC过程,包含了发送和等待回复。若echo为null，则会生成一个随机的echo作为rpc标识，并等待回复。
    /// </summary>
    /// <param name="actionType">action类型</param>
    /// <param name="param">该action参数</param>
    /// <param name="timeout">微秒</param>
    /// <param name="ct"></param>
    /// <returns></returns>
    private async Task<TResult?> PrivateRequestAsync<TResult>(
        ActionType actionType,
        IActionParameter? param,
        int timeout,
        CancellationToken ct
    )
        where TResult : class, IActionResult
    {
        var id = Guid.NewGuid();

        var request = new ActionRequest
        {
            ActionType = actionType,
            Parameter = SerializeParameterForTransport(param),
            Id = id
        };

        var tcs = await PrivateBeginRequestAsync(request, timeout, ct);
        return await PrivateEndRequestAsync<TResult>(tcs, id, timeout, ct);
    }

    private static JsonElement SerializeParameterForTransport(IActionParameter? param)
    {
        var value = param ?? new EmptyActionParameter();
        return SerializeRuntimeObjectToTransportElement(value);
    }

    private static JsonElement SerializeRuntimeObjectToTransportElement(object value)
    {
        return System.Text.Json.JsonSerializer.SerializeToElement(value, value.GetType(), RpcStjOptions);
    }


    /// <summary>
    ///     开始一个RPC过程
    /// </summary>
    /// <param name="request"></param>
    /// <param name="timeout"></param>
    /// <param name="ct"></param>
    /// <exception cref="OperationCanceledException"></exception>
    /// <exception cref="DaemonRequestLimitException"></exception>
    /// <returns></returns>
    private async Task<TaskCompletionSource<ActionResponse>> PrivateBeginRequestAsync(
        ActionRequest request,
        int timeout,
        CancellationToken ct
    )
    {
        var tcs = new TaskCompletionSource<ActionResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        // add to pending
        if (!await _pendingRequests.AddPendingAsync(request.Id, tcs, timeout, ct))
        {
            Log.Error("[ClientConnection] failed to add pending request: pending list is full");
            throw new DaemonRequestLimitException();
        }

        if (!_pendingResponses.TryAdd(request.Id, tcs))
        {
            _pendingRequests.TryRemovePending(request.Id, out _);
            throw new InvalidOperationException($"Duplicate pending request id: {request.Id}");
        }

        try
        {
            await PrivateSendAsync(request, ct);
            return tcs;
        }
        catch
        {
            TryCompletePendingResponse(request.Id, out _);
            throw;
        }
    }

    /// <summary>
    ///     期待一个回复
    /// </summary>
    /// <param name="tcs"></param>
    /// <param name="timeout">回复超时</param>
    /// <param name="id">id</param>
    /// <param name="ct"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    /// <exception cref="TimeoutException">期待超时</exception>
    /// <exception cref="OperationCanceledException">取消</exception>
    private async Task<TResult?> PrivateEndRequestAsync<TResult>(
        TaskCompletionSource<ActionResponse> tcs,
        Guid id,
        int timeout,
        CancellationToken ct
    )
        where TResult : class, IActionResult
    {
        try
        {
            var response = await tcs.Task.WaitAsync(timeout, ct);
            return response.RequestStatus switch
            {
                ActionRequestStatus.Ok => response.Data is null
                    ? null
                    : System.Text.Json.JsonSerializer.Deserialize<TResult>(response.Data.Value.GetRawText(), RpcStjOptions),
                ActionRequestStatus.Error => throw new DaemonRequestException(ActionRetcode.FromCode(response.Retcode),
                    response.Message),
                _ => throw new NotImplementedException()
            };
        }
        catch (Exception e)when (e is TimeoutException or OperationCanceledException)
        {
            TryCompletePendingResponse(id, out _);
            if (e is TimeoutException)
                Log.Debug("[ClientConnection] timeout when waiting for echo: {0}", id);
            throw;
        }
    }

    private void HandleActionResponse(ActionResponse response)
    {
        if (TryCompletePendingResponse(response.Id, out var pending))
            pending.TrySetResult(response);
        else
            Log.Warning("[ClientConnection] Received canceled action's result: {RequestId}, ignore it.", response.Id);
    }

    private bool TryCompletePendingResponse(Guid id, out TaskCompletionSource<ActionResponse> pending)
    {
        if (_pendingResponses.TryRemove(id, out pending!))
        {
            _pendingRequests.TryRemovePending(id, out _);
            return true;
        }

        pending = null!;
        return false;
    }

    private void CancelPendingResponses()
    {
        foreach (var pending in _pendingResponses)
            if (_pendingResponses.TryRemove(pending.Key, out var taskCompletionSource))
            {
                _pendingRequests.TryRemovePending(pending.Key, out _);
                taskCompletionSource.TrySetCanceled();
            }
    }

    private async Task OnReconnectedEventHandler()
    {
        var cts = new CancellationTokenSource();
        cts.CancelAfter(5000);
        var events = SubscribedEvents.EventSet;
        Log.Debug("[ClientConnection] Try recovery {Count} subscribed events", events.Count);

        foreach (var @event in SubscribedEvents.Events)
            try
            {
                await RequestAsync(ActionType.SubscribeEvent, new SubscribeEventParameter
                {
                    Type = @event.Type,
                    Meta = @event.Meta is null ? null : SerializeRuntimeObjectToTransportElement(@event.Meta)
                }, ct: cts.Token);
            }
            catch (OperationCanceledException)
            {
                Log.Debug("[ClientConnection] Cannot recover subscribed event = {0}: timeout", @event);
                events.Remove(@event);
            }
            catch (Exception e)
            {
                Log.Debug(e, "[ClientConnection] Cannot recover subscribed event = {0}", @event);
                events.Remove(@event);
            }
    }

    protected override void ProtectedDispose()
    {
        _cts.Dispose();
        _transport.Dispose();
    }
}
