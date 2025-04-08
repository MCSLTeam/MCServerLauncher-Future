using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Utils;
using Newtonsoft.Json;
using Serilog;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Http.WebSockets;
using TouchSocket.Sockets;

namespace MCServerLauncher.Daemon.Remote;

public class WsActionPlugin : PluginBase, IWsPlugin, IWebSocketReceivedPlugin

{
    private readonly IActionService _actionService;

    public WsActionPlugin(IActionService actionService,
        IHttpService httpService, WsContextContainer container)
    {
        _actionService = actionService;
        HttpService = httpService;
        Container = container;
    }

    // TODO 中继包支持
    public async Task OnWebSocketReceived(IWebSocket webSocket, WSDataFrameEventArgs e)
    {
        if (e.DataFrame.IsText)
        {
            var actionString = e.DataFrame.ToText();
            ActionRequest request;

            try
            {
                request = JsonConvert.DeserializeObject<ActionRequest>(actionString, DaemonJsonSettings.Settings)!;
                Log.Verbose("[Remote] Received message:{0}", request);
            }
            catch (Exception exception) when (exception is JsonException or NullReferenceException)
            {
                var err = ResponseUtils.Err("Could not parse action json", ActionReturnCode.InternalError);
                await webSocket.SendAsync(JsonConvert.SerializeObject(err, DaemonJsonSettings.Settings));
                await e.InvokeNext();
                return;
            }

            // TODO 并发度问题(限制与优化)&背压控制
            var context = this.GetWsContext(webSocket);
            var id = context.ClientId;
            var resolver = webSocket.Client.Resolver;
            Task.Run(async () =>
            {
                var result = await _actionService.ProcessAsync(request, context, resolver, CancellationToken.None);

                var text = JsonConvert.SerializeObject(result, DaemonJsonSettings.Settings);
                Log.Verbose("[Remote] Sending message: \n{0}", text);

                var ws = this.GetWebSocket(id);
                if (ws != null) await ws.SendAsync(text);
                else
                    Log.Warning("[Remote] Failed to respond action, because websocket connection closed or lost.");
            }).ConfigureFalseAwait();
        }

        if (e.DataFrame.IsClose) await webSocket.SafeCloseAsync();

        await e.InvokeNext();
    }


    public IHttpService HttpService { get; init; }
    public WsContextContainer Container { get; init; }
}