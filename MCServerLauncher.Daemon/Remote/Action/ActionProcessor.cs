using System.Collections.ObjectModel;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Remote.Authentication.PermissionSystem;
using MCServerLauncher.Daemon.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action;

public class ActionProcessor : IActionService
{
    private readonly Dictionary<ActionType,
            Func<JToken?, WsContext, IResolver, CancellationToken, ValueTask<IActionResult?>>>
        _handlers;

    private readonly IReadOnlyDictionary<ActionType, IMatchable> _permissions;

    public ActionProcessor(ActionHandlerRegistry handlerRegistry)
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
            return ResponseUtils.Err(request, $"Permission denied for action '{request.ActionType}'",
                ActionReturnCode.PermissionDenied);

        if (!_handlers.TryGetValue(request.ActionType, out var handler))
            return ResponseUtils.Err(request, $"Action '{request.ActionType}' is not implemented",
                ActionReturnCode.ActionNotImplement);

        // 执行
        try
        {
            var result = await handler.Invoke(request.Parameter, context, resolver, cancellationToken);
            return ResponseUtils.Ok(
                result is null
                    ? new JObject()
                    : JObject.FromObject(result, JsonSerializer.Create(DaemonJsonSettings.Settings)),
                request.Id
            );
        }
        catch (ActionException aee)
        {
            return ResponseUtils.Err(request, aee, false);
        }
        catch (Exception e)
        {
            return ResponseUtils.Err(request, e, ActionReturnCode.InternalError, true);
        }
    }
}