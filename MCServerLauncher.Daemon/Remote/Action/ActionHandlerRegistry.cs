﻿using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action;

/// <summary>
///     Action处理函数注册表
/// </summary>
public class ActionHandlerRegistry
{
    public Dictionary<
            ActionType,
            Func<JToken?, WsContext, IResolver, CancellationToken, ValueTask<IActionResult?>>
        >
        Handlers { get; } = new();

    // private readonly Dictionary<ActionType, Func<JToken, IResolver, IActionResult>> _syncHandlers = new();

    public Dictionary<ActionType, IMatchable> HandlerPermissions { get; } = new();

    private static TParam ParseParameter<TParam>(JToken? paramToken)
        where TParam : class, IActionParameter
    {
        ActionExceptionHelper.ThrowIf(paramToken is null, ActionRetcode.BadRequest.WithMessage("Missing parameters"));

        try
        {
            // paramToken一定不为null
            return paramToken!.ToObject<TParam>(JsonSerializer.Create(DaemonJsonSettings.Settings))!;
        }
        catch (JsonSerializationException e)
        {
            var @params = e.Path?.Split(new[] { '.' });
            ActionExceptionHelper.ThrowIf(@params is null,
                ActionRetcode.ParamError.WithMessage("Could not deserialize param"));

            throw e.Context(
                ActionRetcode.ParamError.WithMessage(
                    $"Could not deserialize param '{@params![1]}' at '{e.Path}'")
            );
        }
        catch (NullReferenceException e)
        {
            throw e.Context(ActionRetcode.ParamError.WithMessage("Could not deserialize param"));
        }
        catch (Exception e)
        {
            throw e.Context(
                ActionRetcode.ParamError.WithMessage("Could not deserialize param: " + e.Message));
        }
    }

    # region Register async handlers

    public ActionHandlerRegistry Register<TParam, TResult>(
        ActionType actionType,
        IMatchable actionPermission,
        Func<TParam, WsContext, IResolver, CancellationToken, ValueTask<TResult>> handler
    )
        where TParam : class, IActionParameter
        where TResult : class, IActionResult
    {
        SetActionPermission(actionType, actionPermission);
        Handlers[actionType] = async (paramToken, ctx, resolver, cancellationToken) =>
        {
            var param = ParseParameter<TParam>(paramToken);
            return await handler(param, ctx, resolver, cancellationToken);
        };
        return this;
    }

    public ActionHandlerRegistry Register<TParam>(
        ActionType actionType,
        IMatchable actionPermission,
        Func<TParam, WsContext, IResolver, CancellationToken, ValueTask> handler
    )
        where TParam : class, IActionParameter
    {
        SetActionPermission(actionType, actionPermission);
        Handlers[actionType] = async (paramToken, ctx, resolver, cancellationToken) =>
        {
            var param = ParseParameter<TParam>(paramToken);
            await handler(param, ctx, resolver, cancellationToken);
            return null;
        };
        return this;
    }

    public ActionHandlerRegistry Register<TResult>(
        ActionType actionType,
        IMatchable actionPermission,
        Func<WsContext, IResolver, CancellationToken, ValueTask<TResult>> handler
    )
        where TResult : class, IActionResult
    {
        SetActionPermission(actionType, actionPermission);
        Handlers[actionType] = async (_, ctx, resolver, cancellationToken) =>
        {
            var result = await handler(ctx, resolver, cancellationToken);
            return result;
        };
        return this;
    }

    public ActionHandlerRegistry Register(
        ActionType actionType,
        IMatchable actionPermission,
        Func<WsContext, IResolver, CancellationToken, ValueTask> handler
    )
    {
        SetActionPermission(actionType, actionPermission);
        Handlers[actionType] = async (_, ctx, resolver, cancellationToken) =>
        {
            await handler(ctx, resolver, cancellationToken);
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