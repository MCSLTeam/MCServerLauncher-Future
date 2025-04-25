using System;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.DaemonClient.Connection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Http.WebSockets;

namespace MCServerLauncher.DaemonClient.WebSocketPlugin;

internal class WsStateRecoveryPlugin : PluginBase, IWebSocketHandshakedPlugin
{
    private readonly ClientConnection _connection;

    public WsStateRecoveryPlugin(ClientConnection connection)
    {
        _connection = connection;
    }

    public async Task OnWebSocketHandshaked(IWebSocket webSocket, HttpContextEventArgs e)
    {
        // 订阅未取消订阅的事件 (已订阅事件的持久化保存: 断线重连的恢复)
        var cts = new CancellationTokenSource();
        cts.CancelAfter(5000);
        var failedSet = _connection.SubscribedEvents.EventSet;
        Log.Debug("[WsStateRecoveryPlugin] Try recovery {Count} subscribed events", failedSet.Count);
        try
        {
            foreach (var (type, meta) in _connection.SubscribedEvents.Events)
            {
                await _connection.RequestAsync(ActionType.SubscribeEvent, new SubscribeEventParameter
                {
                    Type = type,
                    Meta = meta is null ? null : JToken.FromObject(meta, JsonSerializer.Create(JsonSettings.Settings))
                }, ct: cts.Token);
                failedSet.Remove((type, meta));
            }
        }
        catch (OperationCanceledException)
        {
            Log.Error("[WsStateRecoveryPlugin] Cannot recovery {Count} subscribed events: {Set}", failedSet.Count,
                failedSet);
        }
    }
}