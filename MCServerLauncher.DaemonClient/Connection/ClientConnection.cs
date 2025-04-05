using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace MCServerLauncher.DaemonClient.Connection;

public class ClientConnection
{
    public event Action<EventType, long, IEventMeta?, IEventData?>? OnEventReceived;

    private const int CF_PROTOCOL_VERSION = 1;
    private const int CF_BUFFER_SIZE = 1024;

    private readonly CancellationTokenSource _cts;
    private readonly ConnectionPendingRequests _pendingRequests;

    private readonly ConnectionHeartBeatTimer? _heartbeatTimer;
    private Task? _receiveLoopTask;

    private ClientConnection(ClientConnectionConfig config)
    {
        _cts = new CancellationTokenSource();
        _pendingRequests = new ConnectionPendingRequests(config.PendingRequestCapacity);
        _heartbeatTimer = config.AutoPing ? new ConnectionHeartBeatTimer(this, config.PingInterval) : null;
        Config = config;
    }


    public bool Closed => WebSocket.State == WebSocketState.Closed;
    public DateTime LastPong { get; private set; } = DateTime.Now;
    public bool PingLost { get; private set; }
    public ClientWebSocket WebSocket { get; } = new();
    public ClientConnectionConfig Config { get; }

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

        // connect ws
        var uri = new Uri($"{(isSecure ? "wss" : "ws")}://{address}:{port}/api/v{CF_PROTOCOL_VERSION}?token={token}");

        await connection.WebSocket.ConnectAsync(uri, cancellationToken);

        connection._heartbeatTimer?.Start();

        // start receive loop
        connection._receiveLoopTask = Task.Factory.StartNew(connection.ReceiveLoop, TaskCreationOptions.LongRunning);
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
            Parameter = JToken.FromObject(param ?? new EmptyActionParameter(),
                JsonSerializer.Create(JsonSettings.Settings)),
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

    private void ThrowIfInvalidState()
    {
        if (WebSocket.State != WebSocketState.Open)
            throw new InvalidOperationException($"Invalid websocket state: {WebSocket.State}");
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
        var json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(
            request,
            Formatting.Indented,
            JsonSettings.Settings
        ));

        // TODO 大数据的分段传输
        return WebSocket.SendAsync(new ArraySegment<byte>(json), WebSocketMessageType.Text, true, cancellationToken);
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

        var jsonSerializer = JsonSerializer.Create(JsonSettings.Settings);
        var request = new ActionRequest
        {
            ActionType = actionType,
            Parameter = JToken.FromObject(param ?? new EmptyActionParameter(), jsonSerializer),
            Id = id
        };

        var tcs = await PrivateBeginRequestAsync(request, timeout, ct);
        return await PrivateEndRequestAsync<TResult>(tcs, id, timeout, ct);
    }

    /// <summary>
    ///     关闭连接
    /// </summary>
    public async Task CloseAsync()
    {
        if (_heartbeatTimer is not null)
            await _heartbeatTimer.Stop();

        _cts.Cancel();
        if (_receiveLoopTask is not null) await _receiveLoopTask; // 等待接收循环结束

        // TODO use close instead of abort
        // await WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        WebSocket.Abort();
        Log.Debug("[ClientConnection] closed");
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
        var tcs = new TaskCompletionSource<ActionResponse>();

        // add to pending
        if (!await _pendingRequests.AddPendingAsync(request.Id, tcs, timeout, ct))
        {
            Log.Error("[ClientConnection] failed to add pending request: pending list is full");
            throw new DaemonRequestLimitException();
        }

        await PrivateSendAsync(request, ct);
        return tcs;
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
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
            var response = (await tcs.Task.TimeoutAfter(timeout, cts.Token))!;
            var serializer = JsonSerializer.Create(JsonSettings.Settings);

            return response.RequestStatus switch
            {
                ActionRequestStatus.Ok => response.Data?.ToObject<TResult>(serializer),
                ActionRequestStatus.Error => throw new DaemonRequestException(response.ReturnCode, response.Message),
                _ => throw new NotImplementedException()
            };
        }
        catch (Exception e)when (e is TimeoutException or OperationCanceledException)
        {
            _pendingRequests.TryRemovePending(id, out _);
            if (e is TimeoutException)
                Log.Debug("[ClientConnection] timeout when waiting for echo: {0}", id);
            throw;
        }
    }

    /// <summary>
    ///     消息接收循环, 被设计运行在某一个线程中
    /// </summary>
    private async Task ReceiveLoop()
    {
        using var ms = new MemoryStream();
        var buffer = new byte[CF_BUFFER_SIZE];

        while (!_cts.Token.IsCancellationRequested)
        {
            while (!_cts.Token.IsCancellationRequested)
                try
                {
                    var result = await WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    ms.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage) break;

                    if (result.MessageType == WebSocketMessageType.Close)
                        return;
                }
                catch (WebSocketException e)
                {
                    Log.Error("[ClientConnection] Websocket exception encountered while receiving message.");
                    throw;
                }
                catch (OperationCanceledException)
                {
                    return;
                }

            Dispatch(Encoding.UTF8.GetString(ms.ToArray()));

            ms.SetLength(0); // reset
        }
    }

    /// <summary>
    ///     派发从服务器接收的应答并根据Action的规则解析包, 判断echo的一致性, 并为等待的任务设置相应的结果或异常
    /// </summary>
    /// <param name="json">从服务器接收的json</param>
    /// <exception cref="ArgumentNullException">echo为空</exception>
    private void Dispatch(string json)
    {
        try
        {
            if (JObject.Parse(json).SelectToken("event") is not null)
            {
                DispatchEvent(json);
                return;
            }

            var response = JsonConvert.DeserializeObject<ActionResponse>(json, JsonSettings.Settings)!;
            if (response.Id == Guid.Empty)
            {
                Log.Error("[ClientConnection] [ReceiveLoop] Received Id=Guid.Empty message: {0}.", response.Message);
                throw new ArgumentNullException(nameof(response.Id));
            }

            if (_pendingRequests.TryRemovePending(response.Id, out var pending))
                pending.SetResult(response);
            else
                Log.Warning($"[ClientConnection] Received canceled action's result: {json},\nignore it.");
        }
        catch (JsonException)
        {
            Log.Error(
                "[ClientConnection] [ReceiveLoop] Received unexpected message: {0}\nmay be connected to a unofficial daemon?",
                json);
            throw;
        }
    }

    private void DispatchEvent(string json)
    {
        try
        {
            var packet = JsonConvert.DeserializeObject<EventPacket>(json, JsonSettings.Settings)!;
            var eventType = packet.EventType;

            // TODO 改为异步? BeginInvoke?
            OnEventReceived?.Invoke(
                eventType,
                packet.Timestamp,
                eventType.GetEventMeta(packet.EventMeta, JsonSettings.Settings),
                eventType.GetEventData(packet.EventData, JsonSettings.Settings)
            );
        }
        catch (JsonException)
        {
            Log.Error(
                "[ClientConnection] [ReceiveLoop] Received unexpected event packet: {0}\nmay be connected to a unofficial daemon?",
                json);
            throw;
        }
    }

    internal void MarkAsPingLost()
    {
        PingLost = true;
    }
}