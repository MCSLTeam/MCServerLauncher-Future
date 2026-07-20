using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

namespace MCServerLauncher.Daemon.API.Application;

public interface IInstanceApplication
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

    Task<Result<UpdateInstanceSettingsResult, DaemonError>> UpdateInstanceSettingsAsync(
        UpdateInstanceSettingsRequest request,
        CancellationToken cancellationToken);
}

public interface IFileApplication
{
    Task<Result<DirectoryDetails, DaemonError>> GetDirectoryInfoAsync(
        PathRequest request,
        CancellationToken cancellationToken);

    Task<Result<FileDetails, DaemonError>> GetFileInfoAsync(
        PathRequest request,
        CancellationToken cancellationToken);

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

    Task<Result<Unit, DaemonError>> WriteUploadChunkAsync(
        UploadChunkRequest request,
        CancellationToken cancellationToken);

    Task<Result<Unit, DaemonError>> CloseUploadAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<Result<Unit, DaemonError>> CancelUploadAsync(
        Guid sessionId,
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
