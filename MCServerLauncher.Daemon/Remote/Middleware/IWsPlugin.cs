using TouchSocket.Http;

namespace MCServerLauncher.Daemon.Remote;

public interface IWsPlugin
{
    IHttpService HttpService { get; init; }
    WsContextContainer Container { get; init; }
}