using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

[ActionHandler(ActionType.GetDirectoryInfo, "mcsl.daemon.file.info.directory")]
class HandleGetDirectoryInfo : IActionHandler<GetDirectoryInfoParameter, GetDirectoryInfoResult>
{
    public Result<GetDirectoryInfoResult, ActionError> Handle(GetDirectoryInfoParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
    {
        return ResultExt.Try(directoryInfoParameter =>
            {
                var entry = FileManager.GetDirectoryInfo(directoryInfoParameter.Path);
                return new GetDirectoryInfoResult
                {
                    Parent = entry.Parent,
                    Directories = entry.Directories,
                    Files = entry.Files
                };
            }, param)
            .OrElse(ex => ex switch
            {
                FileNotFoundException fileNotFoundException => this.Err(
                    ActionRetcode.FileNotFound.ToError().CauseBy(fileNotFoundException)),
                IOException ioException => this.Err(
                    ActionRetcode.FileError.ToError().CauseBy(ioException)),
                _ => this.Err(ActionRetcode.FileError.ToError().CauseBy(ex))
            });
    }
}

[ActionHandler(ActionType.GetFileInfo, "mcsl.daemon.file.info.file")]
class HandleGetFileInfo : IActionHandler<GetFileInfoParameter, GetFileInfoResult>
{
    public Result<GetFileInfoResult, ActionError> Handle(GetFileInfoParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
    {
        return ResultExt.Try(path =>
                new GetFileInfoResult
                {
                    Meta = FileManager.GetFileInfo(path)
                }, param.Path)
            .OrElse(ex => ex switch
            {
                FileNotFoundException fileNotFoundException => this.Err(
                    ActionRetcode.FileNotFound.ToError().CauseBy(fileNotFoundException)),
                IOException ioException => this.Err(
                    ActionRetcode.FileError.ToError().CauseBy(ioException)),
                _ => this.Err(ActionRetcode.FileError.ToError().CauseBy(ex))
            });
    }
}