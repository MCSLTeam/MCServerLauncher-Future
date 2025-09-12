using MCServerLauncher.Common.ProtoType.Files;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Common.ProtoType.Status;
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
    [JsonRequired] public JavaInfo[] JavaList { get; init; } = null!;
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
    [JsonRequired] public string Name { get; init; } = null!;
    [JsonRequired] public FileData[] Files { get; init; } = null!;
}

public sealed record AddInstanceResult : IActionResult
{
    [JsonRequired] public InstanceConfig Config { get; init; } = null!;
}

public sealed record GetInstanceReportResult : IActionResult
{
    [JsonRequired] public InstanceReport Report { get; init; } = null!;
}

public sealed record GetAllReportsResult : IActionResult
{
    [JsonRequired] public Dictionary<Guid, InstanceReport> Reports { get; init; } = null!;
}

public sealed record GetSystemInfoResult : IActionResult
{
    [JsonRequired] public SystemInfo Info { get; init; }
}