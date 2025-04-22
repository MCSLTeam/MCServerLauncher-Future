using MCServerLauncher.Common.ProtoType.Action;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action;

/// <summary>
///     Action服务处理接口
/// </summary>
public interface IActionService
{
    /// <summary>
    ///     处理请求
    /// </summary>
    /// <param name="request">Action请求体</param>
    /// <param name="context">Websocket上下文,通过ClientId获得</param>
    /// <param name="resolver">容器resolver</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<ActionResponse> ProcessAsync(
        ActionRequest request,
        WsContext context,
        IResolver resolver,
        CancellationToken cancellationToken
    );
}