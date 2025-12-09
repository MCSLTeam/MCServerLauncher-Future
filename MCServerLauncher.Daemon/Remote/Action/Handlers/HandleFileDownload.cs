using System.Text.RegularExpressions;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

[ActionHandler(ActionType.FileDownloadRequest, "mcsl.daemon.file.download")]
internal class HandleFileDownloadRequest : IAsyncActionHandler<FileDownloadRequestParameter, FileDownloadRequestResult>
{
    public async Task<Result<FileDownloadRequestResult, ActionError>> HandleAsync(FileDownloadRequestParameter param,
        WsContext ctx, IResolver resolver, CancellationToken ct)
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
                        this.Ok(new FileDownloadRequestResult
                        {
                            FileId = rv.Id,
                            Size = rv.Size,
                            Sha1 = rv.Sha1
                        }));
            }, param)
            .MapTask(result => result.UnwrapOrElse(ex =>
                this.Err(ActionRetcode.FileError.ToError().CauseBy(ex)))
            );
    }
}

[ActionHandler(ActionType.FileDownloadRange, "mcsl.daemon.file.download")]
internal class HandleFileDownloadRange : IAsyncActionHandler<FileDownloadRangeParameter, FileDownloadRangeResult>
{
    public static readonly Regex RangePattern = new(@"^(\d+)..(\d+)$");

    public async Task<Result<FileDownloadRangeResult, ActionError>> HandleAsync(FileDownloadRangeParameter param,
        WsContext ctx, IResolver resolver, CancellationToken ct)
    {
        var match = RangePattern.Match(param.Range);
        if (!match.Success)
            return this.Err(ActionRetcode.ParamError.WithMessage("Invalid range format")
                .ToError());

        var (from, to) = (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));

        return this.Ok(new FileDownloadRangeResult
        {
            Content = await FileManager.FileDownloadRange(param.FileId, from, to)
        });
    }
}

[ActionHandler(ActionType.FileDownloadClose, "mcsl.daemon.file.download")]
internal class HandleFileDownloadClose : IActionHandler<FileDownloadCloseParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(FileDownloadCloseParameter param, WsContext ctx,
        IResolver resolver, CancellationToken ct)
    {
        FileManager.FileDownloadClose(param.FileId);
        return this.Ok(ActionHandlerExtensions.EmptyActionResult);
    }
}