using System.Collections.Immutable;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Storage;
using TouchSocket.Core;
using RustyOptions;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

[ActionHandler(ActionType.FileUploadRequest, "mcsl.daemon.file.upload")]
internal class HandleFileUploadRequest : IActionHandler<FileUploadRequestParameter, FileUploadRequestResult>
{
    public Result<FileUploadRequestResult, ActionError> Handle(
        FileUploadRequestParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        var opened = FileSessionCoordinator.Shared.OpenLegacyUploadAsync(param.Path, param.Size, param.Sha1, ct)
            .GetAwaiter()
            .GetResult();
        return opened.Match(
            value =>
            {
                ctx.RegisterFileUploadSession(value.SessionId);
                return this.Ok(new FileUploadRequestResult { FileId = value.SessionId });
            },
            error => this.Err(LegacyFileActionAdapter.ToActionError(error)));
    }
}

[ActionHandler(ActionType.FileUploadChunk, "mcsl.daemon.file.upload")]
internal class HandleFileUploadChunk : IAsyncActionHandler<FileUploadChunkParameter, FileUploadChunkResult>
{
    public async Task<Result<FileUploadChunkResult, ActionError>> HandleAsync(
        FileUploadChunkParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        if (param.FileId == Guid.Empty)
            return this.Err(ActionRetcode.NotUploadingDownloading.WithMessage(param.FileId).ToError());

        try
        {
            var data = ImmutableArray.CreateRange(Convert.FromBase64String(param.Data));
            var result = await FileSessionCoordinator.Shared.WriteLegacyUploadChunkAsync(param.FileId, param.Offset, data, ct);
            return result.Match(
                value =>
                {
                    if (value.Done)
                        ctx.UnregisterFileUploadSession(param.FileId);
                    return this.Ok(new FileUploadChunkResult { Done = value.Done, Received = value.Received });
                },
                error => this.Err(LegacyFileActionAdapter.ToActionError(error)));
        }
        catch (FormatException exception)
        {
            return this.Err(ActionRetcode.ParamError.WithMessage(exception.Message).ToError());
        }
    }
}

[ActionHandler(ActionType.FileUploadCancel, "mcsl.daemon.file.upload")]
internal class HandleFileUploadCancel : IActionHandler<FileUploadCancelParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(
        FileUploadCancelParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        var result = FileSessionCoordinator.Shared.CancelUploadAsync(param.FileId, ct)
            .GetAwaiter()
            .GetResult();
        ctx.UnregisterFileUploadSession(param.FileId);
        return result.Match(
            _ => this.Ok(ActionHandlerExtensions.EmptyActionResult),
            error => this.Err(LegacyFileActionAdapter.ToActionError(error)));
    }
}
