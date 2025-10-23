using System.Text.RegularExpressions;
using MCServerLauncher.Common.ProtoType.Action;
using RustyOptions;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

/// <summary>
///     注册各种Action处理函数
/// </summary>
internal class HandleBase
{
    protected Regex RangePattern = new(@"^(\d+)..(\d+)$");
    
    internal static ValueTask<Result<TActionResult, ActionError>> ValueTaskOk<TActionResult>(TActionResult result)
        where TActionResult : IActionResult
    {
        return ValueTask.FromResult(new Result<TActionResult, ActionError>(result));
    }

    internal static ValueTask<Result<Unit, ActionError>> ValueTaskOk()
    {
        return ValueTask.FromResult(new Result<Unit, ActionError>(Unit.Default));
    }

    internal static ValueTask<Result<Unit, ActionError>> ValueTaskErr(ActionError error)
    {
        return ValueTask.FromResult(new Result<Unit, ActionError>(error));
    }

    internal static Result<TActionResult, ActionError> Ok<TActionResult>(TActionResult result)
        where TActionResult : IActionResult
    {
        return new Result<TActionResult, ActionError>(result);
    }

    internal static Result<Unit, ActionError> Ok()
    {
        return new Result<Unit, ActionError>(Unit.Default);
    }

    internal static Result<TActionResult, ActionError> Err<TActionResult>(ActionError error)
        where TActionResult : IActionResult
    {
        return Result.Err<TActionResult, ActionError>(error);
    }

    internal static Result<Unit, ActionError> Err(ActionError error)
    {
        return Result.Err<Unit, ActionError>(error);
    }
}