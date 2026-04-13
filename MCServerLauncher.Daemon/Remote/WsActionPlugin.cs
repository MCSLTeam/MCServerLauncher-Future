using MCServerLauncher.Common.ProtoType.Action;
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
            var context = Container.GetContext((webSocket.Client as IHttpSessionClient)!.Id);
            var response = executor.ProcessAction(e.DataFrame.PayloadData, context);

            if (response is not null)
            {
                var utf8Payload = StjJsonSerializer.SerializeToUtf8Bytes(response, DaemonRpcTypeInfoCache<ActionResponse>.TypeInfo);
                var frame = new WSDataFrame(utf8Payload)
                {
                    Opcode = WSDataType.Text,
                    FIN = true
                };
                await webSocket.SendAsync(frame);
            }
        }

        await e.InvokeNext();
    }


    public IHttpService HttpService { get; init; } = httpService;
    public WsContextContainer Container { get; init; } = container;
}
