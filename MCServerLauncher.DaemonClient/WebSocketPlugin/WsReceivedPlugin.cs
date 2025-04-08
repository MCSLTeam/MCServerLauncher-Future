using System;
using System.Threading.Tasks;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.DaemonClient.Connection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using TouchSocket.Core;
using TouchSocket.Http.WebSockets;

namespace MCServerLauncher.DaemonClient.WebSocketPlugin;

public class WsReceivedPlugin : PluginBase, IWebSocketReceivedPlugin
{
    public event Action<EventType, long, IEventMeta?, IEventData?>? OnEventReceived;
    private readonly ConnectionPendingRequests _pendingRequests;
    
    internal WsReceivedPlugin(ConnectionPendingRequests requests) 
    {
        _pendingRequests = requests;
    }

    // TODO 中继包支持
    public async Task OnWebSocketReceived(IWebSocket webSocket, WSDataFrameEventArgs e)
    {
        if (!e.DataFrame.IsText)
        {
            await e.InvokeNext();
            return;
        }

        var received = e.DataFrame.ToText();
        var json = JObject.Parse(received);
        if (json.SelectToken("event") is not null)
        {
            DispatchEvent(json);
        }
        else if (json.SelectToken("status") is not null)
        {
            DispatchAction(json);
        }

        await e.InvokeNext();
    }

    private void DispatchEvent(JObject json)
    {
        try
        {
            var packet = json.ToObject<EventPacket>(JsonSerializer.Create(JsonSettings.Settings))!;
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
            Log.Fatal(
                "[ClientConnection] [ReceiveLoop] Received unexpected event packet: {0}\nmay be connected to a unofficial daemon?",
                json);
            throw;
        }
    }

    private void DispatchAction(JObject json)
    {
        var response = json.ToObject<ActionResponse>(JsonSerializer.Create(JsonSettings.Settings))!;
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
}