using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Storage;
using RustyOptions;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

[ActionHandler(ActionType.DeleteFile, "mcsl.daemon.file.delete.file")]
internal class HandleDeleteFile : IActionHandler<DeleteFileParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(DeleteFileParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
        => LegacyFileActionAdapter.ToEmptyResult(this, FileSessionCoordinator.Shared.DeleteFileAsync(new PathRequest(param.Path), ct));
}

[ActionHandler(ActionType.DeleteDirectory, "mcsl.daemon.file.delete.directory")]
internal class HandleDeleteDirectory : IActionHandler<DeleteDirectoryParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(DeleteDirectoryParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
        => LegacyFileActionAdapter.ToEmptyResult(this, FileSessionCoordinator.Shared.DeleteDirectoryAsync(new DeleteDirectoryRequest(param.Path, param.Recursive), ct));
}

[ActionHandler(ActionType.RenameFile, "mcsl.daemon.file.rename.file")]
internal class HandleRenameFile : IActionHandler<RenameFileParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(RenameFileParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
        => LegacyFileActionAdapter.ToEmptyResult(this, FileSessionCoordinator.Shared.RenameFileAsync(new PathRenameRequest(param.Path, param.NewName), ct));
}

[ActionHandler(ActionType.RenameDirectory, "mcsl.daemon.file.rename.directory")]
internal class HandleRenameDirectory : IActionHandler<RenameDirectoryParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(RenameDirectoryParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
        => LegacyFileActionAdapter.ToEmptyResult(this, FileSessionCoordinator.Shared.RenameDirectoryAsync(new PathRenameRequest(param.Path, param.NewName), ct));
}

[ActionHandler(ActionType.CreateDirectory, "mcsl.daemon.file.create.directory")]
internal class HandleCreateDirectory : IActionHandler<CreateDirectoryParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(CreateDirectoryParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
        => LegacyFileActionAdapter.ToEmptyResult(this, FileSessionCoordinator.Shared.CreateDirectoryAsync(new PathRequest(param.Path), ct));
}

[ActionHandler(ActionType.MoveFile, "mcsl.daemon.file.move.file")]
internal class HandleMoveFile : IActionHandler<MoveFileParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(MoveFileParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
        => LegacyFileActionAdapter.ToEmptyResult(this, FileSessionCoordinator.Shared.MoveFileAsync(new PathTransferRequest(param.SourcePath, param.DestinationPath), ct));
}

[ActionHandler(ActionType.MoveDirectory, "mcsl.daemon.file.move.directory")]
internal class HandleMoveDirectory : IActionHandler<MoveDirectoryParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(MoveDirectoryParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
        => LegacyFileActionAdapter.ToEmptyResult(this, FileSessionCoordinator.Shared.MoveDirectoryAsync(new PathTransferRequest(param.SourcePath, param.DestinationPath), ct));
}

[ActionHandler(ActionType.CopyFile, "mcsl.daemon.file.copy.file")]
internal class HandleCopyFile : IActionHandler<CopyFileParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(CopyFileParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
        => LegacyFileActionAdapter.ToEmptyResult(this, FileSessionCoordinator.Shared.CopyFileAsync(new PathTransferRequest(param.SourcePath, param.DestinationPath), ct));
}

[ActionHandler(ActionType.CopyDirectory, "mcsl.daemon.file.copy.directory")]
internal class HandleCopyDirectory : IActionHandler<CopyDirectoryParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(CopyDirectoryParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
        => LegacyFileActionAdapter.ToEmptyResult(this, FileSessionCoordinator.Shared.CopyDirectoryAsync(new PathTransferRequest(param.SourcePath, param.DestinationPath), ct));
}
