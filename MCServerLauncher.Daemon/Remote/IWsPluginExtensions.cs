using TouchSocket.Http;
using TouchSocket.Http.WebSockets;
using TouchSocket.Sockets;

namespace MCServerLauncher.Daemon.Remote;

public static class IWsPluginExtensions
{
    public static string GetClientId(this IWsPlugin _, IWebSocket webSocket)
    {
        return webSocket.Client is IHttpSessionClient httpClient ? httpClient.Id : "";
    }

    public static IWebSocket? GetWebSocket(this IWsPlugin plugin, string id)
    {
        return plugin.HttpService.TryGetClient(id, out var client) && client.Protocol == Protocol.WebSocket
            ? client.WebSocket
            : null;
    }

    public static WsContext GetWsContext(this IWsPlugin plugin, IWebSocket webSocket)
    {
        return plugin.Container.GetContext(plugin.GetClientId(webSocket))!;
    }

    public static WsContext CreateWsContext(
        this IWsPlugin plugin,
        IWebSocket webSocket,
        Guid jti,
        string? permissions,
        DateTime expiredTo
    )
    {
        return plugin.Container.CreateContext(plugin.GetClientId(webSocket), jti, permissions, expiredTo);
    }
}