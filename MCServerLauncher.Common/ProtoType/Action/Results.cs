using MCServerLauncher.Common.ProtoType.Files;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Common.Utils;
using Newtonsoft.Json;

namespace MCServerLauncher.Common.ProtoType.Action;

public interface IActionResult
{
}

public sealed record EmptyActionResult : IActionResult;

public sealed record GetPermissionsResult : IActionResult
{
    [JsonRequired] public string[] Permissions { get; init; } = null!;
}

public sealed record PingResult : IActionResult
{
    [JsonRequired] public long Time { get; init; }
}

public sealed record GetJavaListResult : IActionResult
{
    [JsonRequired] public List<JavaInfo> JavaList { get; init; } = null!;
}

public sealed record FileUploadRequestResult : IActionResult
{
    [JsonRequired] public Guid FileId { get; init; }
}

public sealed record FileUploadChunkResult : IActionResult
{
    [JsonRequired] public bool Done { get; init; }
    [JsonRequired] public long Received { get; init; }
}

public sealed record FileDownloadRequestResult : IActionResult
{
    [JsonRequired] public Guid FileId { get; init; }
    [JsonRequired] public long Size { get; init; }
    [JsonRequired] public string Sha1 { get; init; } = null!;
}

public sealed record FileDownloadRangeResult : IActionResult
{
    [JsonRequired] public string Content { get; init; } = null!;
}

public sealed record GetFileInfoResult : IActionResult
{
    [JsonRequired] public FileMetadata Meta { get; init; } = null!;
}

public sealed record GetDirectoryInfoResult : IActionResult
{
    public string? Parent { get; init; }
    [JsonRequired] public DirectoryEntry.FileInformation[] Files { get; init; } = null!;
    [JsonRequired] public DirectoryEntry.DirectoryInformation[] Directories { get; init; } = null!;
}

public sealed record AddInstanceResult : IActionResult
{
    [JsonRequired] public InstanceConfig Config { get; init; } = null!;
}

public sealed record RemoveInstanceResult : IActionResult
{
    [JsonRequired] public bool Done { get; init; }
}

public sealed record StartInstanceResult : IActionResult
{
    [JsonRequired] public bool Done { get; init; }
}

public sealed record StopInstanceResult : IActionResult
{
    [JsonRequired] public bool Done { get; init; }
}

public sealed record GetInstanceStatusResult : IActionResult
{
    [JsonRequired] public InstanceStatus Status { get; init; } = null!;
}

public sealed record GetAllStatusResult : IActionResult
{
    [JsonRequired] public Dictionary<Guid, InstanceStatus> Status { get; init; } = null!;
}

public sealed record GetSystemInfoResult : IActionResult
{
    [JsonRequired] public SystemInfo Info { get; init; }
}