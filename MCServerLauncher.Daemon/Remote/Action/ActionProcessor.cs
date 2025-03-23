using System.Collections.ObjectModel;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Action.Results;
using MCServerLauncher.Daemon.Remote.Authentication.PermissionSystem;
using MCServerLauncher.Daemon.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MCServerLauncher.Common;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action;

public class ActionProcessor : IActionService
{
    private readonly Dictionary<ActionType, Func<JToken?, IResolver, CancellationToken, ValueTask<IActionResult?>>>
        _handlers;

    private readonly IReadOnlyDictionary<ActionType, IMatchable> _permissions;

    private readonly JsonSerializer _serializer;

    public ActionProcessor(ActionHandlerRegistry handlerRegistry, IWebJsonConverter webJsonConverter)
    {
        _handlers = handlerRegistry.Handlers;
        _permissions = new ReadOnlyDictionary<ActionType, IMatchable>(handlerRegistry.HandlerPermissions);
        _serializer = webJsonConverter.GetSerializer();
    }

    public async Task<JObject> ProcessAsync(JObject data, IResolver resolver, CancellationToken cancellationToken)
    {
        var echo = data.GetValue("echo")?.ToObject<string>();
        var actionType = data.GetValue("action")?.ToObject<ActionType>(_serializer);

        if (actionType is null) return ResponseUtils.Err("Invalid action", 1500, echo: echo);

        if (_handlers.TryGetValue(actionType.Value, out var handler))
            try
            {
                var result = await handler.Invoke(data.GetValue("params")!, resolver, cancellationToken);
                return ResponseUtils.Ok(result is null ? null : JObject.FromObject(result, _serializer), echo);
            }
            catch (ActionExecutionException aee)
            {
                return ResponseUtils.Err(actionType.Value, aee, fullMessage: true, echo: echo);
            }
            catch (Exception e)
            {
                return ResponseUtils.Err(actionType.Value, e, 1500, fullMessage: true, echo: echo);
            }

        return ResponseUtils.Err($"Action '{actionType.Value}' is not implemented", 1599, echo: echo);
    }
}