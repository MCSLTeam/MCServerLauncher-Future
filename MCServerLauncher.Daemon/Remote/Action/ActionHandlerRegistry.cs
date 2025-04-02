using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Remote.Authentication.PermissionSystem;
using MCServerLauncher.Daemon.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action;

public class ActionHandlerRegistry
{
    public Dictionary<
            ActionType,
            Func<JToken?, IResolver, CancellationToken, ValueTask<IActionResult?>>
        >
        Handlers { get; } = new();

    // private readonly Dictionary<ActionType, Func<JToken, IResolver, IActionResult>> _syncHandlers = new();

    public Dictionary<ActionType, IMatchable> HandlerPermissions { get; } = new();

    private static TParam ParseParameter<TParam>(JToken? paramToken)
        where TParam : class, IActionParameter
    {
        ActionExceptionHelper.ThrowIf(paramToken is null, ActionReturnCode.ParameterIsNull, "Parameter is null");

        try
        {
            // paramToken一定不为null
            return paramToken!.ToObject<TParam>(JsonSerializer.Create(DaemonJsonSettings.Settings))!;
        }
        catch (JsonSerializationException e)
        {
            var @params = e.Path?.Split(new[] { '.' }).ToArray();
            ActionExceptionHelper.ThrowIf(@params is null, ActionReturnCode.ParameterParseError,
                "Could not deserialize param");

            throw e.Context(
                ActionReturnCode.ParameterParseError,
                $"Could not deserialize param '{@params![1]}', in json path: '{e.Path}'"
            );
        }
        catch (NullReferenceException e)
        {
            throw e.Context(ActionReturnCode.ParameterParseError, "Could not deserialize param");
        }
        catch (Exception e)
        {
            throw e.Context("Error occurred during param deserialization: " + e.Message);
        }
    }

    # region Register async handlers

    public ActionHandlerRegistry Register<TParam, TResult>(
        ActionType actionType,
        IMatchable actionPermission,
        Func<TParam, IResolver, CancellationToken, ValueTask<TResult>> handler
    )
        where TParam : class, IActionParameter
        where TResult : class, IActionResult
    {
        SetActionPermission(actionType, actionPermission);
        Handlers[actionType] = async (paramToken, resolver, cancellationToken) =>
        {
            var param = ParseParameter<TParam>(paramToken);
            return await handler(param, resolver, cancellationToken);
        };
        return this;
    }

    public ActionHandlerRegistry Register<TParam>(
        ActionType actionType,
        IMatchable actionPermission,
        Func<TParam, IResolver, CancellationToken, ValueTask> handler
    )
        where TParam : class, IActionParameter
    {
        SetActionPermission(actionType, actionPermission);
        Handlers[actionType] = async (paramToken, resolver, cancellationToken) =>
        {
            var param = ParseParameter<TParam>(paramToken);
            await handler(param, resolver, cancellationToken);
            return null;
        };
        return this;
    }

    public ActionHandlerRegistry Register<TResult>(
        ActionType actionType,
        IMatchable actionPermission,
        Func<IResolver, CancellationToken, ValueTask<TResult>> handler
    )
        where TResult : class, IActionResult
    {
        SetActionPermission(actionType, actionPermission);
        Handlers[actionType] = async (_, resolver, cancellationToken) =>
        {
            var result = await handler(resolver, cancellationToken);
            return result;
        };
        return this;
    }

    public ActionHandlerRegistry Register(
        ActionType actionType,
        IMatchable actionPermission,
        Func<IResolver, CancellationToken, ValueTask> handler
    )
    {
        SetActionPermission(actionType, actionPermission);
        Handlers[actionType] = async (_, resolver, cancellationToken) =>
        {
            await handler(resolver, cancellationToken);
            return null;
        };
        return this;
    }

    private void SetActionPermission(ActionType actionType, IMatchable actionPermission)
    {
        HandlerPermissions[actionType] = actionPermission;
    }

    # endregion
}