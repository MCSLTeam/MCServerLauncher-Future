using System.Net.WebSockets;
using MCServerLauncher.Daemon.Remote.Authentication;
using Serilog;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Http.WebSockets;
using TouchSocket.Sockets;

namespace MCServerLauncher.Daemon.Remote;

public class WsBasePlugin : PluginBase, IWsPlugin, IWebSocketHandshakedPlugin, IWebSocketClosedPlugin
{
    public WsBasePlugin(IHttpService httpService, WsContextContainer container)
    {
        HttpService = httpService;
        Container = container;
    }

    public async Task OnWebSocketClosed(IWebSocket webSocket, ClosedEventArgs e)
    {
        var context = Container.RemoveContext(this.GetClientId(webSocket));
        Log.Information("[Remote] Websocket connection from {0} with ClientId={1} disconnected",
            webSocket.Client.GetIPPort(), context.ClientId);

        await e.InvokeNext();
    }

    public async Task OnWebSocketHandshaked(IWebSocket webSocket, HttpContextEventArgs e)
    {
        var token = e.Context.Request.Query["token"]!;
        WsContext context;
        try
        {
            var (jti, permissions, validTo) = JwtUtils.ReadToken(token);
            context = this.CreateWsContext(webSocket, jti, permissions, validTo);
        }
        catch (Exception)
        {
            Log.Warning("[Remote] Can't get permissions from token, may be token is expired");
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Invalid token");
            await e.InvokeNext();
            return;
        }

        // get peer ip
        Log.Information("[Remote] Accept token: \"{0}...\" from {1} with Id={2}", token[..5],
            webSocket.Client.GetIPPort(),
            context.ClientId);

        await e.InvokeNext();
    }

    public IHttpService HttpService { get; init; }
    public WsContextContainer Container { get; init; }
}