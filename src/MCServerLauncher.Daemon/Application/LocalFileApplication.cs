using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Storage;
using RustyOptions;

namespace MCServerLauncher.Daemon.ApplicationCore;

internal sealed class LocalFileApplication(FileSessionCoordinator coordinator) : IFileApplication
{
    private readonly FileSessionCoordinator _coordinator = coordinator;

    public Task<Result<DirectoryDetails, DaemonError>> GetDirectoryInfoAsync(PathRequest request, CancellationToken cancellationToken)
        => _coordinator.GetDirectoryInfoAsync(request, cancellationToken);

    public Task<Result<FileDetails, DaemonError>> GetFileInfoAsync(PathRequest request, CancellationToken cancellationToken)
        => _coordinator.GetFileInfoAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> CreateDirectoryAsync(PathRequest request, CancellationToken cancellationToken)
        => _coordinator.CreateDirectoryAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> DeleteFileAsync(PathRequest request, CancellationToken cancellationToken)
        => _coordinator.DeleteFileAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> DeleteDirectoryAsync(DeleteDirectoryRequest request, CancellationToken cancellationToken)
        => _coordinator.DeleteDirectoryAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> RenameFileAsync(PathRenameRequest request, CancellationToken cancellationToken)
        => _coordinator.RenameFileAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> RenameDirectoryAsync(PathRenameRequest request, CancellationToken cancellationToken)
        => _coordinator.RenameDirectoryAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> MoveFileAsync(PathTransferRequest request, CancellationToken cancellationToken)
        => _coordinator.MoveFileAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> MoveDirectoryAsync(PathTransferRequest request, CancellationToken cancellationToken)
        => _coordinator.MoveDirectoryAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> CopyFileAsync(PathTransferRequest request, CancellationToken cancellationToken)
        => _coordinator.CopyFileAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> CopyDirectoryAsync(PathTransferRequest request, CancellationToken cancellationToken)
        => _coordinator.CopyDirectoryAsync(request, cancellationToken);

    public Task<Result<UploadSession, DaemonError>> OpenUploadAsync(UploadOpenRequest request, CancellationToken cancellationToken)
        => _coordinator.OpenUploadAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> WriteUploadChunkAsync(UploadChunkRequest request, CancellationToken cancellationToken)
        => _coordinator.WriteUploadChunkAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> CloseUploadAsync(Guid sessionId, CancellationToken cancellationToken)
        => _coordinator.CloseUploadAsync(sessionId, cancellationToken);

    public Task<Result<Unit, DaemonError>> CancelUploadAsync(Guid sessionId, CancellationToken cancellationToken)
        => _coordinator.CancelUploadAsync(sessionId, cancellationToken);

    public Task<Result<DownloadSession, DaemonError>> OpenDownloadAsync(DownloadOpenRequest request, CancellationToken cancellationToken)
        => _coordinator.OpenDownloadAsync(request, cancellationToken);

    public Task<Result<DownloadChunk, DaemonError>> ReadDownloadChunkAsync(DownloadChunkRequest request, CancellationToken cancellationToken)
        => _coordinator.ReadDownloadChunkAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> CloseDownloadAsync(Guid sessionId, CancellationToken cancellationToken)
        => _coordinator.CloseDownloadAsync(sessionId, cancellationToken);
}
