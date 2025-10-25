using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

internal class HandleFileInfo : HandleBase
{
    public static ActionHandlerRegistry Register(ActionHandlerRegistry registry)
    {
        return registry
            .Register<GetDirectoryInfoParameter, GetDirectoryInfoResult>(
                ActionType.GetDirectoryInfo,
                Permission.Of("mcsl.daemon.file.info.directory"),
                (param, ctx, resolver, ct) =>
                    ValueTask.FromResult(ResultExt.Try(directoryInfoParameter =>
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
                            FileNotFoundException fileNotFoundException => Err<GetDirectoryInfoResult>(
                                ActionRetcode.FileNotFound.ToError().CauseBy(fileNotFoundException)),
                            IOException ioException => Err<GetDirectoryInfoResult>(
                                ActionRetcode.FileError.ToError().CauseBy(ioException)),
                            _ => Err<GetDirectoryInfoResult>(ActionRetcode.FileError.ToError().CauseBy(ex))
                        }))
            )
            .Register<GetFileInfoParameter, GetFileInfoResult>(
                ActionType.GetFileInfo,
                Permission.Of("mcsl.daemon.file.info.file"),
                (param, ctx, resolver, ct) =>
                    ValueTask.FromResult(ResultExt.Try(path =>
                            new GetFileInfoResult
                            {
                                Meta = FileManager.GetFileInfo(path)
                            }, param.Path)
                        .OrElse(ex => ex switch
                        {
                            FileNotFoundException fileNotFoundException => Err<GetFileInfoResult>(
                                ActionRetcode.FileNotFound.ToError().CauseBy(fileNotFoundException)),
                            IOException ioException => Err<GetFileInfoResult>(
                                ActionRetcode.FileError.ToError().CauseBy(ioException)),
                            _ => Err<GetFileInfoResult>(ActionRetcode.FileError.ToError().CauseBy(ex))
                        }))
            );
    }
}