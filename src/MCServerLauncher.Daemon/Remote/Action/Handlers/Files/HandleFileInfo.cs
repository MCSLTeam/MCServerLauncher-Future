using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Storage;
using RustyOptions;
using TouchSocket.Core;
using LegacyDirectoryEntry = MCServerLauncher.Common.ProtoType.Files.DirectoryEntry;
using LegacyFileMetadata = MCServerLauncher.Common.ProtoType.Files.FileMetadata;
using LegacyDirectoryMetadata = MCServerLauncher.Common.ProtoType.Files.DirectoryMetadata;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

[ActionHandler(ActionType.GetDirectoryInfo, "mcsl.daemon.file.info.directory")]
internal class HandleGetDirectoryInfo : IActionHandler<GetDirectoryInfoParameter, GetDirectoryInfoResult>
{
    public Result<GetDirectoryInfoResult, ActionError> Handle(
        GetDirectoryInfoParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        var result = FileSessionCoordinator.Shared.GetDirectoryInfoAsync(new PathRequest(param.Path), ct)
            .GetAwaiter()
            .GetResult();
        return result.Match(
            value => this.Ok(new GetDirectoryInfoResult
            {
                Parent = value.Parent,
                Files = value.Files.Select(file => new LegacyDirectoryEntry.FileInformation
                {
                    Name = file.Name,
                    Meta = ToLegacy(file.Meta)
                }).ToArray(),
                Directories = value.Directories.Select(directory => new LegacyDirectoryEntry.DirectoryInformation
                {
                    Name = directory.Name,
                    Meta = ToLegacy(directory.Meta)
                }).ToArray()
            }),
            error => this.Err(LegacyFileActionAdapter.ToActionError(error)));
    }

    private static LegacyFileMetadata ToLegacy(FileMetadata metadata)
    {
        return new LegacyFileMetadata
        {
            CreationTime = metadata.CreationTime.ToUnixTimeSeconds(),
            Hidden = metadata.Hidden,
            LastAccessTime = metadata.LastAccessTime.ToUnixTimeSeconds(),
            LastWriteTime = metadata.LastWriteTime.ToUnixTimeSeconds(),
            ReadOnly = metadata.ReadOnly,
            Size = metadata.Size
        };
    }

    private static LegacyDirectoryMetadata ToLegacy(DirectoryMetadata metadata)
    {
        return new LegacyDirectoryMetadata
        {
            CreationTime = metadata.CreationTime.ToUnixTimeSeconds(),
            Hidden = metadata.Hidden,
            LastAccessTime = metadata.LastAccessTime.ToUnixTimeSeconds(),
            LastWriteTime = metadata.LastWriteTime.ToUnixTimeSeconds()
        };
    }
}

[ActionHandler(ActionType.GetFileInfo, "mcsl.daemon.file.info.file")]
internal class HandleGetFileInfo : IActionHandler<GetFileInfoParameter, GetFileInfoResult>
{
    public Result<GetFileInfoResult, ActionError> Handle(
        GetFileInfoParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        var result = FileSessionCoordinator.Shared.GetFileInfoAsync(new PathRequest(param.Path), ct)
            .GetAwaiter()
            .GetResult();
        return result.Match(
            value => this.Ok(new GetFileInfoResult
            {
                Meta = new LegacyFileMetadata
                {
                    CreationTime = value.Meta.CreationTime.ToUnixTimeSeconds(),
                    Hidden = value.Meta.Hidden,
                    LastAccessTime = value.Meta.LastAccessTime.ToUnixTimeSeconds(),
                    LastWriteTime = value.Meta.LastWriteTime.ToUnixTimeSeconds(),
                    ReadOnly = value.Meta.ReadOnly,
                    Size = value.Meta.Size
                }
            }),
            error => this.Err(LegacyFileActionAdapter.ToActionError(error)));
    }
}
