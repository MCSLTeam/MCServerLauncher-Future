using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using TouchSocket.Core;
using Result = RustyOptions.Result;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

[ActionHandler(ActionType.FileUploadRequest, "mcsl.daemon.file.upload")]
class HandleFileUploadRequest : IActionHandler<FileUploadRequestParameter, FileUploadRequestResult>
{
    public Result<FileUploadRequestResult, ActionError> Handle(FileUploadRequestParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
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
                ? HandleBase.Ok(new FileUploadRequestResult
                {
                    FileId = fileId
                })
                : HandleBase.Err<FileUploadRequestResult>(
                    ActionRetcode.DiskFull.WithMessage("Failed to pre-allocate space").ToError()
                ));
    }
}

[ActionHandler(ActionType.FileUploadChunk, "mcsl.daemon.file.upload")]
class HandleFileUploadChunk : IAsyncActionHandler<FileUploadChunkParameter, FileUploadChunkResult>
{
    public async Task<Result<FileUploadChunkResult, ActionError>> HandleAsync(FileUploadChunkParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
    {
        if (param.FileId == Guid.Empty)
            return HandleBase.Err<FileUploadChunkResult>(ActionRetcode.NotUploadingDownloading.WithMessage(param.FileId)
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
            result.OrElse(ex => HandleBase.Err<FileUploadChunkResult>(ActionRetcode.FileError.ToError().CauseBy(ex)))
        );
    }
}

[ActionHandler(ActionType.FileUploadCancel, "mcsl.daemon.file.upload")]
class HandleFileUploadCancel : IActionHandler<FileUploadCancelParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(FileUploadCancelParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
    {
        return FileManager.FileUploadCancel(param.FileId)
            ? HandleBase.Ok(ActionHandlerExtensions.EmptyActionResult)
            : HandleBase.Err<EmptyActionResult>(ActionRetcode.NotUploadingDownloading.WithMessage(param.FileId).ToError());
    }
}