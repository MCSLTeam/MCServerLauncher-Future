using System;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.DaemonClient.WebSocketPlugin;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Http.WebSockets;
using TouchSocket.Sockets;

namespace MCServerLauncher.DaemonClient.Connection;

internal sealed class TouchSocketClientTransport : DisposableObject
{
    private readonly ClientConnectionConfig _config;
    private readonly object _heartbeatSync = new();
    private CancellationTokenSource? _heartbeatCts;
    private bool _hasConnected;

    public TouchSocketClientTransport(ClientConnectionConfig config)
    {
        _config = config;
    }

    public DateTime LastPong { get; private set; } = DateTime.Now;
    public bool IsConnectionLost { get; private set; }
    public WebSocketClient Client { get; } = new();

    public event Action? ConnectionLost;
    public event Action? Reconnected;
    public event Action? ConnectionClosed;
    public event Action<EventType, long, IEventMeta?, IEventData?>? EventReceived;
    public event Action<ActionResponse>? ActionResponseReceived;

    public async Task OpenAsync(string address, int port, string token, bool isSecure,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Client.SetupAsync(CreateTransportConfig(address, port, token, isSecure));
        await Client.ConnectAsync(cancellationToken);
    }

    public Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Client.SendAsync(payload, WSDataType.Text, true, cancellationToken);
    }

    public Task CloseAsync()
    {
        StopHeartbeatLoop();
        return Client.CloseAsync();
    }

    protected override void ProtectedDispose()
    {
        StopHeartbeatLoop();
        Client.SafeDispose();
    }

    private TouchSocketConfig CreateTransportConfig(string address, int port, string token, bool isSecure)
    {
        var endpoint = BuildServerEndpoint(address, port, token, isSecure);
        return new TouchSocketConfig()
            .SetRemoteIPHost(new IPHost(endpoint))
            .ConfigurePlugins(a =>
            {
                var receivedPlugin = new WsReceivedPlugin();
                receivedPlugin.OnEventReceived += (t, l, m, d) => EventReceived?.Invoke(t, l, m, d);
                receivedPlugin.OnActionResponseReceived += response => ActionResponseReceived?.Invoke(response);
                a.Add(receivedPlugin);

                a.Add(new WsConnectionLifecyclePlugin(this));
                a.UseReconnection<WebSocketClient>();
            });
    }

    private static string BuildServerEndpoint(string address, int port, string token, bool isSecure)
    {
        var scheme = isSecure ? "wss" : "ws";
        return $"{scheme}://{address}:{port}/api/v1?token={token}";
    }

    private void OnConnected()
    {
        StartHeartbeatLoop();
        IsConnectionLost = false;
        LastPong = DateTime.Now;
        if (_hasConnected)
            Reconnected?.Invoke();

        _hasConnected = true;
    }

    private void OnConnectionLost()
    {
        if (IsConnectionLost)
            return;

        IsConnectionLost = true;
        ConnectionLost?.Invoke();
    }

    private void OnClosed()
    {
        StopHeartbeatLoop();
        if (!IsConnectionLost)
            ConnectionClosed?.Invoke();
    }

    private void OnPong()
    {
        LastPong = DateTime.Now;
    }

    private void StartHeartbeatLoop()
    {
        if (!_config.HeartBeat)
            return;

        CancellationTokenSource heartbeatCts;
        lock (_heartbeatSync)
        {
            _heartbeatCts?.Cancel();
            _heartbeatCts?.Dispose();
            _heartbeatCts = new CancellationTokenSource();
            heartbeatCts = _heartbeatCts;
        }

        _ = EasyTask.SafeRun(() => RunHeartbeatLoopAsync(heartbeatCts.Token));
    }

    private void StopHeartbeatLoop()
    {
        lock (_heartbeatSync)
        {
            _heartbeatCts?.Cancel();
            _heartbeatCts?.Dispose();
            _heartbeatCts = null;
        }
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        var failedCount = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_config.HeartBeatTick, cancellationToken)
                    .ConfigureAwait(EasyTask.ContinueOnCapturedContext);
                if (!Client.Online)
                    return;

                try
                {
                    await Client.PingAsync().ConfigureAwait(EasyTask.ContinueOnCapturedContext);
                    OnPong();
                    failedCount = 0;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    failedCount++;
                }

                if (failedCount > _config.MaxFailCount)
                {
                    OnConnectionLost();
                    await Client.CloseAsync("自动心跳失败次数达到最大，已断开连接。")
                        .ConfigureAwait(EasyTask.ContinueOnCapturedContext);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private sealed class WsConnectionLifecyclePlugin : PluginBase, IWebSocketConnectedPlugin, IWebSocketClosedPlugin
    {
        private readonly TouchSocketClientTransport _transport;

        public WsConnectionLifecyclePlugin(TouchSocketClientTransport transport)
        {
            _transport = transport;
        }

        public Task OnWebSocketClosed(IWebSocket webSocket, ClosedEventArgs e)
        {
            _transport.OnClosed();
            return e.InvokeNext();
        }

        public Task OnWebSocketConnected(IWebSocket webSocket, HttpContextEventArgs e)
        {
            _transport.OnConnected();
            return e.InvokeNext();
        }
    }
}