using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using TouchSocket.Core;
using Result = RustyOptions.Result;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

[ActionHandler(ActionType.FileUploadRequest, "mcsl.daemon.file.upload")]
internal class HandleFileUploadRequest : IActionHandler<FileUploadRequestParameter, FileUploadRequestResult>
{
    public Result<FileUploadRequestResult, ActionError> Handle(FileUploadRequestParameter param, WsContext ctx,
        IResolver resolver, CancellationToken ct)
    {
        return Result
            .Try(() => FileManager.FileUploadRequest(
                param.Path,
                param.Size,
                param.Timeout.Map(t => TimeSpan.FromMilliseconds(t)),
                param.Sha1
            ))
            .MapErr(ex => new ActionError(ActionRetcode.FileError).CauseBy(ex))
            .AndThen(fileId => fileId != Guid.Empty
                ? this.Ok(new FileUploadRequestResult
                {
                    FileId = fileId
                })
                : this.Err(
                    ActionRetcode.DiskFull.WithMessage("Failed to pre-allocate space").ToError()
                ));
    }
}

[ActionHandler(ActionType.FileUploadChunk, "mcsl.daemon.file.upload")]
internal class HandleFileUploadChunk : IAsyncActionHandler<FileUploadChunkParameter, FileUploadChunkResult>
{
    public async Task<Result<FileUploadChunkResult, ActionError>> HandleAsync(FileUploadChunkParameter param,
        WsContext ctx, IResolver resolver, CancellationToken ct)
    {
        if (param.FileId == Guid.Empty)
            return this.Err(ActionRetcode.NotUploadingDownloading.WithMessage(param.FileId)
                .ToError());

        return await ResultExt.TryAsync(async chunkParameter =>
        {
            var (done, received) = await FileManager.FileUploadChunk(
                chunkParameter.FileId,
                chunkParameter.Offset,
                chunkParameter.Data
            );

            return new FileUploadChunkResult
            {
                Done = done,
                Received = received
            };
        }, param).MapTask(result =>
            result.OrElse(ex => this.Err(ActionRetcode.FileError.ToError().CauseBy(ex)))
        );
    }
}

[ActionHandler(ActionType.FileUploadCancel, "mcsl.daemon.file.upload")]
internal class HandleFileUploadCancel : IActionHandler<FileUploadCancelParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(FileUploadCancelParameter param, WsContext ctx,
        IResolver resolver, CancellationToken ct)
    {
        return FileManager.FileUploadCancel(param.FileId)
            ? this.Ok(ActionHandlerExtensions.EmptyActionResult)
            : this.Err(ActionRetcode.NotUploadingDownloading.WithMessage(param.FileId).ToError());
    }
}