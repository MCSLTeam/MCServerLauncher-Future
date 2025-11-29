using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using System.Text.RegularExpressions;
using MCServerLauncher.Common.Helpers;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

[ActionHandler(ActionType.FileDownloadRequest, "mcsl.daemon.file.download")]
class HandleFileDownloadRequest : IAsyncActionHandler<FileDownloadRequestParameter, FileDownloadRequestResult>
{
    public async Task<Result<FileDownloadRequestResult, ActionError>> HandleAsync(FileDownloadRequestParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
    {
        return await ResultExt.TryAsync(async requestParameter =>
            {
                return Option.Create(await FileManager.FileDownloadRequest(
                        requestParameter.Path,
                        requestParameter.Timeout.Map(t => TimeSpan.FromMilliseconds(t))
                    ))
                    .OkOr(ActionRetcode.RateLimitExceeded.WithMessage(
                        $"Max download sessions of file '{requestParameter.Path}' reached").ToError())
                    .AndThen(rv =>
                        HandleBase.Ok(new FileDownloadRequestResult
                        {
                            FileId = rv.Id,
                            Size = rv.Size,
                            Sha1 = rv.Sha1
                        }));
            }, param)
            .MapTask(result => result.UnwrapOrElse(ex =>
                HandleBase.Err<FileDownloadRequestResult>(ActionRetcode.FileError.ToError().CauseBy(ex)))
            );
    }
}

[ActionHandler(ActionType.FileDownloadRange, "mcsl.daemon.file.download")]
class HandleFileDownloadRange : IAsyncActionHandler<FileDownloadRangeParameter, FileDownloadRangeResult>
{
    public async Task<Result<FileDownloadRangeResult, ActionError>> HandleAsync(FileDownloadRangeParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
    {
        var match = HandleBase.RangePattern.Match(param.Range);
        if (!match.Success)
            return HandleBase.Err<FileDownloadRangeResult>(ActionRetcode.ParamError.WithMessage("Invalid range format")
                .ToError());

        var (from, to) = (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));

        return HandleBase.Ok(new FileDownloadRangeResult
        {
            Content = await FileManager.FileDownloadRange(param.FileId, from, to)
        });
    }
}

[ActionHandler(ActionType.FileDownloadClose, "mcsl.daemon.file.download")]
class HandleFileDownloadClose : IActionHandler<FileDownloadCloseParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(FileDownloadCloseParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
    {
        FileManager.FileDownloadClose(param.FileId);
        return HandleBase.Ok(ActionHandlerExtensions.EmptyActionResult);
    }
}