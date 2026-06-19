using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.Internal.Performance;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Serialization;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using System.Text.Json;
using TouchSocket.Core;
using JsonElement = System.Text.Json.JsonElement;
using Result = RustyOptions.Result;

namespace MCServerLauncher.Daemon.Remote.Action;

/// <summary>
///     Action Handler的基础接口
/// </summary>
/// <typeparam name="TParam">Action参数类型</typeparam>
/// <typeparam name="TResult">Action返回值类型</typeparam>
internal interface IActionHandlerBase<TParam, TResult>
    where TParam : class, IActionParameter
    where TResult : class, IActionResult
{
    // TODO 之后换成System.Text.Json的JsonElement
    Result<TParam, ActionError> ParseParameter(JsonElement? token)
    {
        if (token is null)
            return Result.Err<TParam, ActionError>(ActionRetcode.ParamError.WithMessage("Missing parameters"));

        try
        {
            var result = JsonElementHotPathAdapters.Deserialize(
                token.Value,
                DaemonRpcTypeInfoCache<TParam>.TypeInfo);
            return Result.Ok<TParam, ActionError>(result);
        }
        catch (JsonException e)
        {
            var errorMessage = "Could not deserialize param";

            if (!string.IsNullOrWhiteSpace(e.Path))
            {
                var @params = e.Path.Split('.');
                errorMessage += @params.Length > 1
                    ? $"'{@params[1]}' at '{e.Path}'"
                    : $" at '{e.Path}'";
            }

            return Result.Err<TParam, ActionError>(ActionRetcode.ParamError.WithMessage(errorMessage));
        }
        catch (NullReferenceException)
        {
            return Result.Err<TParam, ActionError>(ActionRetcode.ParamError.WithMessage("Could not deserialize param"));
        }
        catch (Exception e)
        {
            return Result.Err<TParam, ActionError>(
                ActionRetcode.ParamError.WithMessage("Could not deserialize param: " + e.Message));
        }
    }

    ActionResponse ToResponse(Result<TResult, ActionError> result, Guid id)
    {
        return result.Match(
            r => new ActionResponse
            {
                RequestStatus = ActionRequestStatus.Ok,
                Retcode = ActionRetcode.Ok.Code,
                Message = ActionRetcode.Ok.Message,
                Data = ToJsonElement(r),
                Id = id
            },
            err => new ActionResponse
            {
                RequestStatus = ActionRequestStatus.Error,
                Retcode = err.Retcode.Code,
                Message = err.ToString(),
                Data = null,
                Id = id
            });
    }

    private static JsonElement ToJsonElement(TResult? value)
    {
        if (value is null)
            return default;

        return JsonElementHotPathAdapters.SerializeToElement(value, DaemonRpcTypeInfoCache<TResult>.TypeInfo);
    }
}

/// <summary>
///     同步ActionHandler接口（适用于耗时短，没有IO阻塞/计算量的任务）， 直接在当前线程处理
/// </summary>
/// <typeparam name="TParam"></typeparam>
/// <typeparam name="TResult"></typeparam>
internal interface IActionHandler<TParam, TResult> : IActionHandlerBase<TParam, TResult>
    where TParam : class, IActionParameter
    where TResult : class, IActionResult
{
    Result<TResult, ActionError> Handle(TParam param, WsContext ctx, IResolver resolver, CancellationToken ct);
}

/// <summary>
///     异步ActionHandler接口（适用于耗时长，有IO阻塞/计算量的任务），提交到线程池处理
/// </summary>
/// <typeparam name="TParam"></typeparam>
/// <typeparam name="TResult"></typeparam>
internal interface IAsyncActionHandler<TParam, TResult> : IActionHandlerBase<TParam, TResult>
    where TParam : class, IActionParameter
    where TResult : class, IActionResult
{
    Task<Result<TResult, ActionError>> HandleAsync(
        TParam param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct
    );
}

internal static class ActionHandlerExtensions
{
    public static readonly EmptyActionParameter EmptyActionParameter = new();
    public static readonly EmptyActionResult EmptyActionResult = new();


    #region ResultHelper

    public static Result<TResult, ActionError> Ok<TParam, TResult>(this IActionHandlerBase<TParam, TResult> self,
        TResult result)
        where TParam : class, IActionParameter
        where TResult : class, IActionResult
    {
        return Result.Ok<TResult, ActionError>(result);
    }

    public static Result<TResult, ActionError> Err<TParam, TResult>(this IActionHandlerBase<TParam, TResult> self,
        ActionError error)
        where TParam : class, IActionParameter
        where TResult : class, IActionResult
    {
        return Result.Err<TResult, ActionError>(error);
    }

    #endregion

    #region ProcessHelper

    public static ActionResponse Process<TParam, TResult>(
        this IActionHandler<TParam, TResult> self,
        JsonElement? param,
        Guid id,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct
    )
        where TParam : class, IActionParameter
        where TResult : class, IActionResult
    {
        var parsed = self.ParseParameter(param);

        var result = parsed.IsErr(out var err)
            ? Result.Err<TResult, ActionError>(err!)
            : self.Handle(parsed.Ok().Unwrap(), ctx, resolver, ct);

        return self.ToResponse(result, id);
    }

    public static async Task<ActionResponse> ProcessAsync<TParam, TResult>(
        this IAsyncActionHandler<TParam, TResult> self,
        JsonElement? param,
        Guid id,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct
    )
        where TParam : class, IActionParameter
        where TResult : class, IActionResult
    {
        var parsed = self.ParseParameter(param);

        var task = parsed.IsErr(out var err)
            ? Task.FromResult(Result.Err<TResult, ActionError>(err!))
            : self.HandleAsync(parsed.Ok().Unwrap(), ctx, resolver, ct);

        var result = await task;
        return self.ToResponse(result, id);
    }

    #endregion
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal class ActionHandlerAttribute : Attribute
{
    public ActionHandlerAttribute(ActionType actionType, string permission)
    {
        ActionType = actionType;
        Permission = Authentication.Permission.Of(permission);
    }

    public ActionType ActionType { get; }
    public IMatchable Permission { get; }
}
