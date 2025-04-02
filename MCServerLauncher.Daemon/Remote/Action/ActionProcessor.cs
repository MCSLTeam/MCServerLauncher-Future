using System.Collections.ObjectModel;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Remote.Authentication.PermissionSystem;
using MCServerLauncher.Daemon.Utils;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action;

public class ActionProcessor : IActionService
{
    private readonly Dictionary<ActionType, Func<JToken?, IResolver, CancellationToken, ValueTask<IActionResult?>>>
        _handlers;

    private readonly IReadOnlyDictionary<ActionType, IMatchable> _permissions;

    public ActionProcessor(ActionHandlerRegistry handlerRegistry)
    {
        _handlers = handlerRegistry.Handlers;
        _permissions = new ReadOnlyDictionary<ActionType, IMatchable>(handlerRegistry.HandlerPermissions);
    }

    public async Task<ActionResponse> ProcessAsync(ActionRequest request, IResolver resolver,
        CancellationToken cancellationToken)
    {
        var userPermission = resolver.GetRequiredService<WsServiceContext>().Permissions;
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
            var result = await handler.Invoke(request.Parameter, resolver, cancellationToken);
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