using System.Collections.ObjectModel;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Remote.Authentication;
using Newtonsoft.Json.Linq;
using RustyOptions;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action;

/// <summary>
///     Action服务实现
/// </summary>
public class ActionService : IActionService
{
    private readonly Dictionary<ActionType,
            Func<JToken?, WsContext, IResolver, CancellationToken,
                ValueTask<Result<Option<IActionResult>, ActionError>>>>
        _handlers;

    private readonly IReadOnlyDictionary<ActionType, IMatchable> _permissions;

    public ActionService(ActionHandlerRegistry handlerRegistry)
    {
        _handlers = handlerRegistry.Handlers;
        _permissions = new ReadOnlyDictionary<ActionType, IMatchable>(handlerRegistry.HandlerPermissions);
    }

    public async Task<ActionResponse> ProcessAsync(ActionRequest request, WsContext context, IResolver resolver,
        CancellationToken cancellationToken)
    {
        var userPermission = context.Permissions;
        if (_permissions.TryGetValue(request.ActionType, out var actionPermission) &&
            !userPermission.Matches(actionPermission))
            return ResponseUtils.Err(
                ActionRetcode.PermissionDenied.WithMessage($"Permission denied for action '{request.ActionType}'"),
                request.Id
            );

        if (!_handlers.TryGetValue(request.ActionType, out var handler))
            return ResponseUtils.Err(
                ActionRetcode.UnknownAction.WithMessage($"Action '{request.ActionType}' is not implemented"),
                request.Id
            );

        // 执行
        try
        {
            return ResponseUtils.FromResult(
                await handler.Invoke(request.Parameter, context, resolver, cancellationToken),
                request.Id
            );
        }
        catch (Exception e)
        {
            return ResponseUtils.Err(request, e, ActionRetcode.UnexpectedError, true);
        }
    }
}