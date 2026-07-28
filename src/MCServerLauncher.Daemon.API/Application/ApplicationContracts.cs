using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

namespace MCServerLauncher.Daemon.API.Application;

public interface IInstanceQueryApplication
{
    Task<Result<InstanceReport, DaemonError>> GetInstanceReportAsync(
        InstanceReference request,
        CancellationToken cancellationToken);

    Task<Result<InstanceReportList, DaemonError>> ListInstanceReportsAsync(
        CancellationToken cancellationToken);

    Task<Result<InstanceLogResult, DaemonError>> GetInstanceLogAsync(
        InstanceLogQuery request,
        CancellationToken cancellationToken);

    Task<Result<InstanceSettingsResult, DaemonError>> GetInstanceSettingsAsync(
        InstanceReference request,
        CancellationToken cancellationToken);
}

public interface IInstanceManagementApplication
{
    Task<Result<CreateInstanceResult, DaemonError>> CreateInstanceAsync(
        CreateInstanceRequest request,
        CancellationToken cancellationToken);

    Task<Result<Unit, DaemonError>> RemoveInstanceAsync(
        InstanceReference request,
        CancellationToken cancellationToken);

    Task<Result<Unit, DaemonError>> StartInstanceAsync(
        InstanceReference request,
        CancellationToken cancellationToken);

    Task<Result<Unit, DaemonError>> StopInstanceAsync(
        InstanceReference request,
        CancellationToken cancellationToken);

    Task<Result<Unit, DaemonError>> HaltInstanceAsync(
        InstanceReference request,
        CancellationToken cancellationToken);

    Task<Result<Unit, DaemonError>> SendCommandAsync(
        InstanceCommandRequest request,
        CancellationToken cancellationToken);

    Task<Result<UpdateInstanceSettingsResult, DaemonError>> UpdateInstanceSettingsAsync(
        UpdateInstanceSettingsRequest request,
        CancellationToken cancellationToken);
}

public interface IInstanceApplication : IInstanceQueryApplication, IInstanceManagementApplication
{

    Task<Result<ConsoleSession, DaemonError>> OpenConsoleAsync(
        ConsoleOpenRequest request,
        CancellationToken cancellationToken);

    Task<Result<Unit, DaemonError>> ResizeConsoleAsync(
        ConsoleResizeRequest request,
        CancellationToken cancellationToken);

    Task<Result<Unit, DaemonError>> CloseConsoleAsync(
        ConsoleSessionReference request,
        CancellationToken cancellationToken);

    Task<Result<Unit, DaemonError>> WriteConsoleAsync(
        Guid sessionId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken);

}

/// <summary>
/// Contained filesystem metadata plus bounded download reads. Downloads are pull-based — a chunk
/// read returns its bytes to the caller — so they need no connection of their own; the session
/// coordinator owns them and expires an abandoned one on its own timer.
/// </summary>
public interface IFileReadApplication
{
    Task<Result<DirectoryDetails, DaemonError>> GetDirectoryInfoAsync(
        PathRequest request,
        CancellationToken cancellationToken);

    Task<Result<FileDetails, DaemonError>> GetFileInfoAsync(
        PathRequest request,
        CancellationToken cancellationToken);

    Task<Result<DownloadSession, DaemonError>> OpenDownloadAsync(
        DownloadOpenRequest request,
        CancellationToken cancellationToken);

    Task<Result<DownloadChunk, DaemonError>> ReadDownloadChunkAsync(
        DownloadChunkRequest request,
        CancellationToken cancellationToken);

    Task<Result<Unit, DaemonError>> CloseDownloadAsync(
        Guid sessionId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Contained filesystem mutation: path operations plus staged uploads. An upload commits only on
/// close, so an abandoned session leaves no partial file behind.
/// </summary>
public interface IFileWriteApplication
{
    Task<Result<Unit, DaemonError>> CreateDirectoryAsync(
        PathRequest request,
        CancellationToken cancellationToken);

    Task<Result<Unit, DaemonError>> DeleteFileAsync(
        PathRequest request,
        CancellationToken cancellationToken);

    Task<Result<Unit, DaemonError>> DeleteDirectoryAsync(
        DeleteDirectoryRequest request,
        CancellationToken cancellationToken);

    Task<Result<Unit, DaemonError>> RenameFileAsync(
        PathRenameRequest request,
        CancellationToken cancellationToken);

    Task<Result<Unit, DaemonError>> RenameDirectoryAsync(
        PathRenameRequest request,
        CancellationToken cancellationToken);

    Task<Result<Unit, DaemonError>> MoveFileAsync(
        PathTransferRequest request,
        CancellationToken cancellationToken);

    Task<Result<Unit, DaemonError>> MoveDirectoryAsync(
        PathTransferRequest request,
        CancellationToken cancellationToken);

    Task<Result<Unit, DaemonError>> CopyFileAsync(
        PathTransferRequest request,
        CancellationToken cancellationToken);

    Task<Result<Unit, DaemonError>> CopyDirectoryAsync(
        PathTransferRequest request,
        CancellationToken cancellationToken);

    Task<Result<UploadSession, DaemonError>> OpenUploadAsync(
        UploadOpenRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes one staged chunk. It has no method name of its own: on the wire the bytes ride a
    /// binary frame authorized by the lease that <c>mcsl.file.upload.open</c> created, so callers
    /// are gated on that same permission here.
    /// </summary>
    Task<Result<Unit, DaemonError>> WriteUploadChunkAsync(
        UploadChunkRequest request,
        CancellationToken cancellationToken);

    Task<Result<Unit, DaemonError>> CloseUploadAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<Result<Unit, DaemonError>> CancelUploadAsync(
        Guid sessionId,
        CancellationToken cancellationToken);
}

/// <summary>
/// The full contained file surface, composed from the two narrow views a plugin can be granted.
/// </summary>
public interface IFileApplication : IFileReadApplication, IFileWriteApplication;

public interface ISystemApplication
{
    Task<Result<SystemInfo, DaemonError>> GetSystemInfoAsync(CancellationToken cancellationToken);

    Task<Result<JavaRuntimeList, DaemonError>> ListJavaRuntimesAsync(CancellationToken cancellationToken);
}

public interface IEventRuleApplication
{
    Task<Result<EventRuleSet, DaemonError>> GetEventRulesAsync(
        EventRuleQuery request,
        CancellationToken cancellationToken);

    Task<Result<Unit, DaemonError>> UpdateEventRulesAsync(
        EventRuleUpdateRequest request,
        CancellationToken cancellationToken);
}
