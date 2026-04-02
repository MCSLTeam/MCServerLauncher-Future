using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Serialization;
using StjJsonSerializer = System.Text.Json.JsonSerializer;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Http.WebSockets;

namespace MCServerLauncher.Daemon.Remote;

public class WsActionPlugin(IActionExecutor executor,
    IHttpService httpService, WsContextContainer container)
    : PluginBase, IWsPlugin, IWebSocketReceivedPlugin

{
    public async Task OnWebSocketReceived(IWebSocket webSocket, WSDataFrameEventArgs e)
    {
        if (e.DataFrame.IsText)
        {
            var actionString = e.DataFrame.ToText();
            var context = Container.GetContext((webSocket.Client as IHttpSessionClient)!.Id);
            var response = executor.ProcessAction(actionString, context);

            if (response is not null)
                await webSocket.SendAsync(StjJsonSerializer.Serialize(response, DaemonRpcJsonBoundary.StjOptions));
        }

        await e.InvokeNext();
    }


    public IHttpService HttpService { get; init; } = httpService;
    public WsContextContainer Container { get; init; } = container;
}
