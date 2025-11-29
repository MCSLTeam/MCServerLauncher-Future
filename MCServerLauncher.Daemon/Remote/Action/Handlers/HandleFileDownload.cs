using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

internal class HandleFileDownload : HandleBase
{
    public static ActionHandlerRegistry Register(ActionHandlerRegistry registry)
    {
        return registry
            .Register<FileUploadRequestParameter, FileUploadRequestResult>(
                ActionType.FileUploadRequest,
                Permission.Of("mcsl.daemon.file.upload"),
                (param, ctx, resolver, ct) =>
                    ValueTask.FromResult(Result
                        .Try(() => FileManager.FileUploadRequest(
                            param.Path,
                            param.Size,
                            param.Timeout.Map(t => TimeSpan.FromMilliseconds(t)),
                            param.Sha1
                        ))
                        .MapErr(ex => new ActionError(ActionRetcode.FileError).CauseBy(ex))
                        .AndThen(fileId => fileId != Guid.Empty
                            ? Ok(new FileUploadRequestResult
                            {
                                FileId = fileId
                            })
                            : Err<FileUploadRequestResult>(
                                ActionRetcode.DiskFull.WithMessage("Failed to pre-allocate space").ToError()
                            ))
                    )
            )
            .Register<FileUploadChunkParameter, FileUploadChunkResult>(
                ActionType.FileUploadChunk,
                Permission.Of("mcsl.daemon.file.upload"),
                async (param, ctx, resolver, ct) =>
                {
                    if (param.FileId == Guid.Empty)
                        return Err<FileUploadChunkResult>(ActionRetcode.NotUploadingDownloading
                            .WithMessage(param.FileId)
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
                        result.OrElse(ex => Err<FileUploadChunkResult>(ActionRetcode.FileError.ToError().CauseBy(ex)))
                    );
                })
            .Register<FileUploadCancelParameter>(
                ActionType.FileUploadCancel,
                Permission.Of("mcsl.daemon.file.upload"),
                (param, ctx, resolver, ct) =>
                    FileManager.FileUploadCancel(param.FileId)
                        ? ValueTaskOk()
                        : ValueTaskErr(ActionRetcode.NotUploadingDownloading.WithMessage(param.FileId))
            );
    }
}