using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Action.Parameters;
using MCServerLauncher.Common.ProtoType.Action.Results;
using MCServerLauncher.Daemon.Remote.Authentication.PermissionSystem;
using MCServerLauncher.Daemon.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action;

public class ActionHandlerRegistry
{
    private readonly JsonSerializer _jsonSerializer;

    public ActionHandlerRegistry(IWebJsonConverter webJsonConverter)
    {
        _jsonSerializer = webJsonConverter.GetSerializer();
    }

    public Dictionary<ActionType, Func<JToken?, IResolver, CancellationToken, Task<IActionResult>>>
        Handlers { get; } = new();

    // private readonly Dictionary<ActionType, Func<JToken, IResolver, IActionResult>> _syncHandlers = new();

    public Dictionary<ActionType, IMatchable> HandlerPermissions { get; } = new();

    # region Register async handlers

    public ActionHandlerRegistry Register<TParam, TResult>(
        ActionType actionType,
        IMatchable actionPermission,
        Func<TParam, IResolver, CancellationToken, Task<TResult>> handler
    )
        where TParam : IActionParameter
        where TResult : IActionResult
    {
        SetActionPermission(actionType, actionPermission);
        Handlers[actionType] = async (data, resolver, cancellationToken) =>
        {
            var param = (data ?? new JObject()).ToObject<TParam>(_jsonSerializer);
            return await handler(param!, resolver, cancellationToken);
        };
        return this;
    }

    public ActionHandlerRegistry Register<TResult>(
        ActionType actionType,
        IMatchable actionPermission,
        Func<IResolver, CancellationToken, Task<TResult>> handler
    )
        where TResult : IActionResult
    {
        return Register<EmptyActionParameter, TResult>(actionType, actionPermission, (_, sp, ct) => handler(sp, ct));
    }

    public ActionHandlerRegistry Register<TParam>(
        ActionType actionType,
        IMatchable actionPermission,
        Func<TParam, IResolver, CancellationToken, Task> handler
    )
        where TParam : IActionParameter
    {
        SetActionPermission(actionType, actionPermission);
        Handlers[actionType] = async (data, resolver, cancellationToken) =>
        {
            var param = data.ToObject<TParam>(_jsonSerializer);
            await handler(param!, resolver, cancellationToken);
            return new EmptyActionResult();
        };
        return this;
    }

    public ActionHandlerRegistry Register(
        ActionType actionType,
        IMatchable actionPermission,
        Func<IResolver, CancellationToken, Task> handler
    )
    {
        return Register<EmptyActionParameter>(actionType, actionPermission, (_, sp, ct) => handler(sp, ct));
    }

    private void SetActionPermission(ActionType actionType, IMatchable actionPermission)
    {
        HandlerPermissions[actionType] = actionPermission;
    }

    # endregion

    // #region Register sync handlers
    //
    // public ActionHandlerRegistry Register<TParam, TResult>(ActionType actionType,
    //     Func<TParam, IResolver, TResult> handler)
    //     where TParam : IActionParameter
    //     where TResult : IActionResult
    // {
    //     _syncHandlers[actionType] = (data, resolver) =>
    //     {
    //         var param = data.ToObject<TParam>(_jsonSerializer);
    //         return handler(param!, resolver);
    //     };
    //     return this;
    // }
    //
    // public ActionHandlerRegistry Register<TResult>(ActionType actionType, Func<IResolver, TResult> handler)
    //     where TResult : IActionResult
    // {
    //     return Register<EmptyActionParameter, TResult>(actionType, (_, sp) => handler(sp));
    // }
    //
    // public ActionHandlerRegistry Register<TParam>(ActionType actionType, Action<TParam, IResolver> handler)
    //     where TParam : IActionParameter
    // {
    //     _syncHandlers[actionType] = (data, resolver) =>
    //     {
    //         var param = data.ToObject<TParam>(_jsonSerializer);
    //         handler(param!, resolver);
    //         return new EmptyActionResult();
    //     };
    //     return this;
    // }
    //
    // public ActionHandlerRegistry Register(ActionType actionType, Action<IResolver> handler)
    // {
    //     return Register<EmptyActionParameter>(actionType, (_, sp) => handler(sp));
    // }
    //
    // #endregion
}