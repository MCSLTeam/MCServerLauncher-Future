using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.DaemonClient.WebSocketPlugin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using TouchSocket.Core;
using TouchSocket.Http.WebSockets;
using TouchSocket.Sockets;

namespace MCServerLauncher.DaemonClient.Connection;

internal class ClientConnection : IDisposable
{
    private const int CF_PROTOCOL_VERSION = 1;

    private readonly CancellationTokenSource _cts;
    private readonly ConnectionPendingRequests _pendingRequests;
    private bool _disposed;

    private ClientConnection(ClientConnectionConfig config)
    {
        _cts = new CancellationTokenSource();
        _pendingRequests = new ConnectionPendingRequests(config.PendingRequestCapacity);
        Config = config;
    }

    public SubscribedEvents SubscribedEvents { get; } = new();
    public DateTime LastPong { get; private set; } = DateTime.Now;
    public bool PingLost { get; private set; }
    public WebSocketClient Client { get; } = new();
    public ClientConnectionConfig Config { get; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this); // 阻止终结器重复释放资源
    }

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

        // connect ws
        await connection.Client.SetupAsync(new TouchSocketConfig()
            .SetRemoteIPHost(new IPHost($"ws://{address}:{port}/api/v{CF_PROTOCOL_VERSION}?token={token}"))
            .ConfigurePlugins(a =>
            {
                var receivedPlugin = new WsReceivedPlugin(connection._pendingRequests);
                receivedPlugin.OnEventReceived += (t, l, m, d) => { connection.OnEventReceived?.Invoke(t, l, m, d); };
                a.Add(receivedPlugin);

                if (config.HeartBeat)
                {
                    var heartbeatPlugin = a.UseWebSocketHeartbeat();
                    heartbeatPlugin.MaxFailCount = config.MaxFailCount;
                    heartbeatPlugin.Tick = config.HeartBeatTick;
                }

                a.Add(new WsStateRecoveryPlugin(connection));

                a.UseWebSocketReconnection();
            })
        );

        await connection.Client.ConnectAsync();
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
        var json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(
            request,
            Formatting.Indented,
            JsonSettings.Settings
        ));

        cancellationToken.ThrowIfCancellationRequested();
        // TODO 大数据的分段传输
        return Client.SendAsync(new ReadOnlyMemory<byte>(json), WSDataType.Text);
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
        _cts.Cancel();
        await Client.CloseAsync();
        SubscribedEvents.Events.Clear(); // 清空标记的已订阅事件
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
                ActionRequestStatus.Error => throw new DaemonRequestException(ActionRetcode.FromCode(response.Retcode), response.Message),
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

    internal void MarkAsPingLost()
    {
        PingLost = true;
    }

    ~ClientConnection()
    {
        Dispose(false);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            _cts.Dispose();
            Client.SafeDispose();
        }

        _disposed = true;
    }
}