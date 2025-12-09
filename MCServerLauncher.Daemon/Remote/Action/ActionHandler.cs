using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RustyOptions;
using TouchSocket.Core;
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
    Result<TParam, ActionError> ParseParameter(JToken? token)
    {
        if (token is null)
            return Result.Err<TParam, ActionError>(ActionRetcode.BadRequest.WithMessage("Missing parameters"));

        try
        {
            // paramToken一定不为null
            var result = token.ToObject<TParam>(JsonSerializer.Create(DaemonJsonSettings.Settings))!;
            return Result.Ok<TParam, ActionError>(result);
        }
        catch (JsonSerializationException e)
        {
            var @params = e.Path?.Split(new[] { '.' });
            var errorMessage = "Could not deserialize param";

            if (@params is not null) errorMessage += $"'{@params[1]}' at '{e.Path}'";

            return Result.Err<TParam, ActionError>(ActionRetcode.ParamError.WithMessage(errorMessage));
        }
        catch (NullReferenceException e)
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
                Data = JObject.FromObject(r, JsonSerializer.Create(DaemonJsonSettings.Settings)),
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
        JToken? param,
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
        JToken? param,
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