using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Utils;
using Newtonsoft.Json;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Http.WebSockets;

namespace MCServerLauncher.Daemon.Remote;

public class WsActionPlugin : PluginBase, IWsPlugin, IWebSocketReceivedPlugin

{
    private readonly IActionExecutor _executor;

    public WsActionPlugin(IActionExecutor executor,
        IHttpService httpService, WsContextContainer container)
    {
        _executor = executor;
        HttpService = httpService;
        Container = container;
    }
    
    public async Task OnWebSocketReceived(IWebSocket webSocket, WSDataFrameEventArgs e)
    {
        if (e.DataFrame.IsText)
        {
            var actionString = e.DataFrame.ToText();
            var context = Container.GetContext((webSocket.Client as IHttpSessionClient)!.Id);
            var response = _executor.ProcessAction(actionString, context);

            if (response is not null)
                await webSocket.SendAsync(JsonConvert.SerializeObject(response, DaemonJsonSettings.Settings));
        }

        await e.InvokeNext();
    }


    public IHttpService HttpService { get; init; }
    public WsContextContainer Container { get; init; }
}