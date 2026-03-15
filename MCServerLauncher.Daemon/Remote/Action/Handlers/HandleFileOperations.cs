using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

[ActionHandler(ActionType.DeleteFile, "mcsl.daemon.file.delete.file")]
internal class HandleDeleteFile : IActionHandler<DeleteFileParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(DeleteFileParameter param, WsContext ctx, IResolver resolver,
        CancellationToken ct)
    {
        return ResultExt.Try(path =>
            {
                FileManager.DeleteFile(path);
                return new EmptyActionResult();
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

[ActionHandler(ActionType.DeleteDirectory, "mcsl.daemon.file.delete.directory")]
internal class HandleDeleteDirectory : IActionHandler<DeleteDirectoryParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(DeleteDirectoryParameter param, WsContext ctx, IResolver resolver,
        CancellationToken ct)
    {
        return ResultExt.Try(p =>
            {
                FileManager.DeleteDirectory(p.Path, p.Recursive);
                return new EmptyActionResult();
            }, param)
            .OrElse(ex => ex switch
            {
                DirectoryNotFoundException dirNotFoundException => this.Err(
                    ActionRetcode.FileNotFound.ToError().CauseBy(dirNotFoundException)),
                IOException ioException => this.Err(
                    ActionRetcode.FileError.ToError().CauseBy(ioException)),
                _ => this.Err(ActionRetcode.FileError.ToError().CauseBy(ex))
            });
    }
}

[ActionHandler(ActionType.RenameFile, "mcsl.daemon.file.rename.file")]
internal class HandleRenameFile : IActionHandler<RenameFileParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(RenameFileParameter param, WsContext ctx, IResolver resolver,
        CancellationToken ct)
    {
        return ResultExt.Try(p =>
            {
                FileManager.RenameFile(p.Path, p.NewName);
                return new EmptyActionResult();
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

[ActionHandler(ActionType.RenameDirectory, "mcsl.daemon.file.rename.directory")]
internal class HandleRenameDirectory : IActionHandler<RenameDirectoryParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(RenameDirectoryParameter param, WsContext ctx, IResolver resolver,
        CancellationToken ct)
    {
        return ResultExt.Try(p =>
            {
                FileManager.RenameDirectory(p.Path, p.NewName);
                return new EmptyActionResult();
            }, param)
            .OrElse(ex => ex switch
            {
                DirectoryNotFoundException dirNotFoundException => this.Err(
                    ActionRetcode.FileNotFound.ToError().CauseBy(dirNotFoundException)),
                IOException ioException => this.Err(
                    ActionRetcode.FileError.ToError().CauseBy(ioException)),
                _ => this.Err(ActionRetcode.FileError.ToError().CauseBy(ex))
            });
    }
}

[ActionHandler(ActionType.CreateDirectory, "mcsl.daemon.file.create.directory")]
internal class HandleCreateDirectory : IActionHandler<CreateDirectoryParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(CreateDirectoryParameter param, WsContext ctx, IResolver resolver,
        CancellationToken ct)
    {
        return ResultExt.Try(path =>
            {
                FileManager.CreateDirectory(path);
                return new EmptyActionResult();
            }, param.Path)
            .OrElse(ex => ex switch
            {
                IOException ioException => this.Err(
                    ActionRetcode.FileError.ToError().CauseBy(ioException)),
                _ => this.Err(ActionRetcode.FileError.ToError().CauseBy(ex))
            });
    }
}

[ActionHandler(ActionType.MoveFile, "mcsl.daemon.file.move.file")]
internal class HandleMoveFile : IActionHandler<MoveFileParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(MoveFileParameter param, WsContext ctx, IResolver resolver,
        CancellationToken ct)
    {
        return ResultExt.Try(p =>
            {
                FileManager.MoveFile(p.SourcePath, p.DestinationPath);
                return new EmptyActionResult();
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

[ActionHandler(ActionType.MoveDirectory, "mcsl.daemon.file.move.directory")]
internal class HandleMoveDirectory : IActionHandler<MoveDirectoryParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(MoveDirectoryParameter param, WsContext ctx, IResolver resolver,
        CancellationToken ct)
    {
        return ResultExt.Try(p =>
            {
                FileManager.MoveDirectory(p.SourcePath, p.DestinationPath);
                return new EmptyActionResult();
            }, param)
            .OrElse(ex => ex switch
            {
                DirectoryNotFoundException dirNotFoundException => this.Err(
                    ActionRetcode.FileNotFound.ToError().CauseBy(dirNotFoundException)),
                IOException ioException => this.Err(
                    ActionRetcode.FileError.ToError().CauseBy(ioException)),
                _ => this.Err(ActionRetcode.FileError.ToError().CauseBy(ex))
            });
    }
}

[ActionHandler(ActionType.CopyFile, "mcsl.daemon.file.copy.file")]
internal class HandleCopyFile : IActionHandler<CopyFileParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(CopyFileParameter param, WsContext ctx, IResolver resolver,
        CancellationToken ct)
    {
        return ResultExt.Try(p =>
            {
                FileManager.CopyFile(p.SourcePath, p.DestinationPath);
                return new EmptyActionResult();
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

[ActionHandler(ActionType.CopyDirectory, "mcsl.daemon.file.copy.directory")]
internal class HandleCopyDirectory : IActionHandler<CopyDirectoryParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(CopyDirectoryParameter param, WsContext ctx, IResolver resolver,
        CancellationToken ct)
    {
        return ResultExt.Try(p =>
            {
                FileManager.CopyDirectory(p.SourcePath, p.DestinationPath);
                return new EmptyActionResult();
            }, param)
            .OrElse(ex => ex switch
            {
                DirectoryNotFoundException dirNotFoundException => this.Err(
                    ActionRetcode.FileNotFound.ToError().CauseBy(dirNotFoundException)),
                IOException ioException => this.Err(
                    ActionRetcode.FileError.ToError().CauseBy(ioException)),
                _ => this.Err(ActionRetcode.FileError.ToError().CauseBy(ex))
            });
    }
}