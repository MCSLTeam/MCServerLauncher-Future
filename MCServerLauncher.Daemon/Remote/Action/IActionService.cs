using MCServerLauncher.Common.ProtoType.Action;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action;

/// <summary>
///     Action处理接口
/// </summary>
public interface IActionService
{
    public Task<ActionResponse> ProcessAsync(ActionRequest request, WsContext context, IResolver resolver,
        CancellationToken cancellationToken);
}