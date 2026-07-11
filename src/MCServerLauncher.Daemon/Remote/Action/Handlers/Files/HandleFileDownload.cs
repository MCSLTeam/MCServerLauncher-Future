using System.Text;
using System.Text.RegularExpressions;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Storage;
using RustyOptions;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

[ActionHandler(ActionType.FileDownloadRequest, "mcsl.daemon.file.download")]
internal class HandleFileDownloadRequest : IAsyncActionHandler<FileDownloadRequestParameter, FileDownloadRequestResult>
{
    public async Task<Result<FileDownloadRequestResult, ActionError>> HandleAsync(
        FileDownloadRequestParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        var opened = await FileSessionCoordinator.Shared.OpenLegacyDownloadAsync(param.Path, ct);
        return opened.Match(
            value =>
            {
                ctx.RegisterFileDownloadSession(value.SessionId);
                return this.Ok(new FileDownloadRequestResult
                {
                    FileId = value.SessionId,
                    Size = value.Size,
                    Sha1 = value.Sha1
                });
            },
            error => this.Err(LegacyFileActionAdapter.ToActionError(error)));
    }
}

[ActionHandler(ActionType.FileDownloadRange, "mcsl.daemon.file.download")]
internal class HandleFileDownloadRange : IAsyncActionHandler<FileDownloadRangeParameter, FileDownloadRangeResult>
{
    private static readonly Regex RangePattern = new(@"^(\d+)\.\.(\d+)$", RegexOptions.CultureInvariant);

    public async Task<Result<FileDownloadRangeResult, ActionError>> HandleAsync(
        FileDownloadRangeParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        var match = RangePattern.Match(param.Range);
        if (!match.Success
            || !long.TryParse(match.Groups[1].Value, out var from)
            || !long.TryParse(match.Groups[2].Value, out var to))
            return this.Err(ActionRetcode.ParamError.WithMessage("Invalid range format.").ToError());

        var result = await FileSessionCoordinator.Shared.ReadLegacyDownloadRangeAsync(param.FileId, from, to, ct);
        return result.Match(
            value =>
            {
                if ((value.Length & 1) != 0)
                    return this.Err(ActionRetcode.FileError.WithMessage(
                        "The legacy string download protocol cannot represent an odd byte count.").ToError());

                return this.Ok(new FileDownloadRangeResult
                {
                    Content = Encoding.BigEndianUnicode.GetString(value.AsSpan())
                });
            },
            error => this.Err(LegacyFileActionAdapter.ToActionError(error)));
    }
}

[ActionHandler(ActionType.FileDownloadClose, "mcsl.daemon.file.download")]
internal class HandleFileDownloadClose : IActionHandler<FileDownloadCloseParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(
        FileDownloadCloseParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        var result = FileSessionCoordinator.Shared.CloseLegacyDownloadAsync(param.FileId, ct)
            .GetAwaiter()
            .GetResult();
        ctx.UnregisterFileDownloadSession(param.FileId);
        return result.Match(
            _ => this.Ok(ActionHandlerExtensions.EmptyActionResult),
            error => this.Err(LegacyFileActionAdapter.ToActionError(error)));
    }
}
