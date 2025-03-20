using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace MCServerLauncher.DaemonClient;

internal class HeartBeatTimerState
{
    public HeartBeatTimerState(ClientConnection conn, SynchronizationContext ctx)
    {
        Connection = conn;
        ConnectionContext = ctx;
    }

    public ClientConnection Connection { get; set; }
    public int PingPacketLost { get; set; }
    public SynchronizationContext ConnectionContext { get; set; }
}

internal class ConnectionPendingRequest
{
    private readonly SemaphoreSlim _full;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JObject>> _pendings = new();
    public readonly int Size;

    public ConnectionPendingRequest(int size)
    {
        Size = size;
        _full = new SemaphoreSlim(size);
    }

    public async Task<bool> AddPendingAsync(string echo, TaskCompletionSource<JObject> tcs, int timeout,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await _full.WaitAsync(timeout, cancellationToken)) return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        return _pendings.TryAdd(echo, tcs); // 确保echo在size范围内不会重复
    }

    public bool TryRemovePending(string echo, out TaskCompletionSource<JObject> tcs)
    {
        var rv = _pendings.TryRemove(echo, out tcs);
        _full.Release();
        return rv;
    }

    public bool TryGetPending(string echo, out TaskCompletionSource<JObject> tcs)
    {
        return _pendings.TryGetValue(echo, out tcs);
    }
}

internal class ConnectionHeartBeatTimer
{
    private readonly CancellationTokenSource _cancelTokenSource = new();
    private readonly TimeSpan _interval;
    private readonly HeartBeatTimerState _state;
    private Task? _timerLoopTask;

    public ConnectionHeartBeatTimer(ClientConnection conn, TimeSpan interval)
    {
        _state = new HeartBeatTimerState(conn, SynchronizationContext.Current!);
        _interval = interval;
    }

    public void Start()
    {
        _timerLoopTask = TimerLoop(_state);
    }

    public async Task Stop()
    {
        _cancelTokenSource.Cancel();
        if (_timerLoopTask != null) await _timerLoopTask;
    }

    private async Task TimerLoop(HeartBeatTimerState state)
    {
        var innerTasks = new HashSet<Task>();
        while (!_cancelTokenSource.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, _cancelTokenSource.Token);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            var innerTask = OnTimer(state);
            innerTasks.Add(innerTask);
            innerTasks.RemoveWhere(t => t.IsCompleted);
        }

        await Task.WhenAll(innerTasks);
    }

    /// <summary>
    ///     心跳定时器超时逻辑: 根据连接情况设定ClientConnection的PingLost，根据config判断是否关闭连接
    /// </summary>
    /// <param name="state">Timer状态</param>
    private async Task OnTimer(HeartBeatTimerState state)
    {
        Log.Debug("[ClientConnection] Heartbeat timer triggered.");

        if (_cancelTokenSource.IsCancellationRequested) return;
        try
        {
            var result = await state.Connection.RequestAsync(
                ActionType.Ping,
                new Dictionary<string, object>(),
                Guid.NewGuid().ToString(),
                state.Connection.Config.PingTimeout,
                _cancelTokenSource.Token
            );

            state.PingPacketLost = 0;
            var timestamp = result["time"]!.ToObject<long>();
            Log.Debug($"[ClientConnection] Ping packet received, timestamp: {timestamp}");
        }
        catch (TimeoutException)
        {
            if (_cancelTokenSource.IsCancellationRequested) return;

            state.PingPacketLost++;
            Log.Warning($"[ClientConnection] Ping packet lost, lost {state.PingPacketLost} times.");
            // 切换到ClientConnection所在线程,防止数据竞争
            state.ConnectionContext.Post(_ => state.Connection.MarkAsPingLost(), null);
        }

        if (state.PingPacketLost < state.Connection.Config.MaxPingPacketLost) return;
        Log.Error("Ping packet lost too many times, close connection.");
        // 关闭连接
        await state.Connection.CloseAsync();
    }
}

public class ClientConnection
{
    private const int ProtocolVersion = 1;
    private const int BufferSize = 1024;

    private readonly CancellationTokenSource _cts;
    private readonly ConnectionHeartBeatTimer _heartbeatTimer;
    private readonly ConnectionPendingRequest _pendingRequests;
    private Task? _receiveLoopTask;

    private ClientConnection(ClientConnectionConfig config)
    {
        _cts = new CancellationTokenSource();
        _pendingRequests = new ConnectionPendingRequest(config.PendingRequestCapacity);
        _heartbeatTimer = new ConnectionHeartBeatTimer(this, config.PingInterval);
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
    public static async Task<ClientConnection> OpenAsync(string address, int port, string token,
        bool isSecure, ClientConnectionConfig config, CancellationToken cancellationToken = default)
    {
        // create instance
        ClientConnection connection = new(config);

        // connect ws
        var uri = new Uri($"{(isSecure ? "wss" : "ws")}://{address}:{port}/api/v{ProtocolVersion}?token={token}");

        await connection.WebSocket.ConnectAsync(uri, cancellationToken);

        connection._heartbeatTimer.Start();

        // start receive loop
        connection._receiveLoopTask = Task.Factory.StartNew(connection.ReceiveLoop, TaskCreationOptions.LongRunning);
        return connection;
    }

    /// <summary>
    ///     包装action并发送,内部函数
    /// </summary>
    /// <param name="ws"></param>
    /// <param name="actionType"></param>
    /// <param name="args"></param>
    /// <param name="echo"></param>
    /// <param name="cancellationToken"></param>
    private static Task SendAsync(ClientWebSocket ws, ActionType actionType, Dictionary<string, object> args,
        string? echo, CancellationToken cancellationToken)
    {
        Dictionary<string, object> data = new()
        {
            { "action", actionType.ToShakeCase() },
            { "params", args }
        };

        if (!string.IsNullOrEmpty(echo)) data.Add("echo", echo!);

        var json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data, Formatting.Indented));
        return ws.SendAsync(new ArraySegment<byte>(json), WebSocketMessageType.Text, true, cancellationToken);
    }

    /// <summary>
    ///     发送一个action(不等待应答 / 丢弃应答)
    /// </summary>
    /// <param name="actionType"></param>
    /// <param name="args"></param>
    /// <param name="echo"></param>
    /// <param name="cancellationToken"></param>
    public Task SendAsync(ActionType actionType, Dictionary<string, object> args, string? echo = null,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(WebSocket, actionType, args, echo, cancellationToken);
    }

    /// <summary>
    ///     一个RPC过程,包含了发送和等待回复。若echo为null，则会生成一个随机的echo作为rpc标识，并等待回复。
    /// </summary>
    /// <param name="actionType">action类型</param>
    /// <param name="args">该action参数</param>
    /// <param name="echo">echo</param>
    /// <param name="timeout">微秒</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<JObject> RequestAsync(ActionType actionType, Dictionary<string, object> args,
        string? echo = null, int timeout = 5000, CancellationToken cancellationToken = default)
    {
        echo ??= Guid.NewGuid().ToString();
        SendAsync(actionType, args, echo, cancellationToken);
        return ExpectAsync(echo, timeout, cancellationToken);
    }

    /// <summary>
    ///     关闭连接
    /// </summary>
    public async Task CloseAsync()
    {
        await _heartbeatTimer.Stop();

        _cts.Cancel();
        if (_receiveLoopTask != null) await _receiveLoopTask; // 等待接收循环结束
        
        // TODO use close instead of abort
        // await WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        WebSocket.Abort();
        Log.Debug("[ClientConnection] closed");
    }

    /// <summary>
    ///     期待一个回复
    /// </summary>
    /// <param name="timeout">回复超时</param>
    /// <param name="echo">echo校验</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    /// <exception cref="TimeoutException">期待超时</exception>
    private async Task<JObject> ExpectAsync(string echo, int timeout, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<JObject>();

        // add to pending
        if (!await _pendingRequests.AddPendingAsync(echo, tcs, timeout, cancellationToken))
        {
            Log.Error("[ClientConnection] failed to add pending request: pending list is full");
            throw new Exception("failed to add pending request: pending list is full");
        }

        var task = await Task.WhenAny(tcs.Task, Task.Delay(timeout, cancellationToken));

        // timeout or cancelled
        if (task != tcs.Task)
        {
            // remove from pending
            if (!_pendingRequests.TryRemovePending(echo, out _))
            {
                Log.Error("[ClientConnection] failed to remove pending request, echo: {0}", echo);
                throw new Exception($"failed to remove pending request, echo: {echo}");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                Log.Debug("[ClientConnection] cancelled when waiting for echo: {0}", echo);
            }
            else throw new TimeoutException($"Timeout when waiting for echo: {echo}");
        }

        var received = tcs.Task.Result;

        // error handling
        var status = received["status"]!.ToString();
        if (status == null) throw new Exception($"status is null: {received}");

        return status switch
        {
            "ok" => (received["data"]! as JObject)!,
            "error" => throw new Exception(received["data"]!["message"]?.ToString() ?? "unknown error"),
            _ => throw new Exception($"unknown status: {status}")
        };
    }

    /// <summary>
    ///     消息接收循环, 被设计运行在某一个线程中
    /// </summary>
    private async Task ReceiveLoop()
    {
        var ms = new MemoryStream();
        var buffer = new byte[BufferSize];

        while (!_cts.Token.IsCancellationRequested)
        {
            while (!_cts.Token.IsCancellationRequested)
                try
                {
                    var result = await WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    ms.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage) break;

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        ms.Dispose();
                        return;
                    }
                }
                catch (WebSocketException e)
                {
                    Console.WriteLine(e.ToString());
                    ms.Dispose();
                    return;
                }
                catch (OperationCanceledException)
                {
                    ms.Dispose();
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
        var data = JObject.Parse(json);
        data.TryGetValue("echo", out var rawEcho);

        if (rawEcho == null)
        {
            Log.Error(
                "[ClientConnection] [ReceiveLoop] Received unexpected message: echo is null. may be connected to a unofficial daemon?");
            throw new ArgumentNullException(nameof(rawEcho));
        }

        var echo = rawEcho.ToString();

        if (_pendingRequests.TryRemovePending(echo, out var pending))
            pending.SetResult(data);
        else
            Log.Warning($"[ClientConnection] Received redundant message: redundant message: {json}");
    }

    internal void MarkAsPingLost()
    {
        PingLost = true;
    }
}