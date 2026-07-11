using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Common.ProtoType.Action;
using RustyOptions;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

internal static class LegacyFileActionAdapter
{
    public static Result<EmptyActionResult, ActionError> ToEmptyResult<TParam>(
        IActionHandlerBase<TParam, EmptyActionResult> handler,
        Task<Result<Unit, DaemonError>> operation)
        where TParam : class, IActionParameter
    {
        var result = operation.GetAwaiter().GetResult();
        return result.Match(
            _ => handler.Ok(ActionHandlerExtensions.EmptyActionResult),
            error => handler.Err(ToActionError(error)));
    }

    public static ActionError ToActionError(DaemonError error)
    {
        var retcode = error.Kind switch
        {
            DaemonErrorKind.Validation => ActionRetcode.ParamError,
            DaemonErrorKind.NotFound => ActionRetcode.FileNotFound,
            DaemonErrorKind.Conflict => ActionRetcode.AlreadyUploadingDownloading,
            DaemonErrorKind.Permission => ActionRetcode.FileAccessDenied,
            DaemonErrorKind.Storage => ActionRetcode.FileError,
            _ => ActionRetcode.FileError
        };

        return retcode.WithMessage(error.Message).ToError();
    }
}
