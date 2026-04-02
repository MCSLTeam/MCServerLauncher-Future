using MCServerLauncher.Common.ProtoType.Files;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Common.ProtoType.Status;
using Newtonsoft.Json;
using SysTextJsonRequired = System.Text.Json.Serialization.JsonRequiredAttribute;
using SysTextJsonPropertyName = System.Text.Json.Serialization.JsonPropertyNameAttribute;

namespace MCServerLauncher.Common.ProtoType.Action;

public interface IActionResult
{
}

public sealed record EmptyActionResult : IActionResult;

public sealed record GetPermissionsResult : IActionResult
{
    [JsonRequired]
    [SysTextJsonRequired]
    public string[] Permissions { get; init; } = null!;
}

public sealed record PingResult : IActionResult
{
    [JsonRequired]
    [SysTextJsonRequired]
    public long Time { get; init; }
}

public sealed record GetJavaListResult : IActionResult
{
    [JsonRequired]
    [SysTextJsonRequired]
    public JavaInfo[] JavaList { get; init; } = null!;
}

public sealed record FileUploadRequestResult : IActionResult
{
    [JsonRequired]
    [SysTextJsonRequired]
    public Guid FileId { get; init; }
}

public sealed record FileUploadChunkResult : IActionResult
{
    [JsonRequired]
    [SysTextJsonRequired]
    public bool Done { get; init; }

    [JsonRequired]
    [SysTextJsonRequired]
    public long Received { get; init; }
}

public sealed record FileDownloadRequestResult : IActionResult
{
    [JsonRequired]
    [SysTextJsonRequired]
    public Guid FileId { get; init; }

    [JsonRequired]
    [SysTextJsonRequired]
    public long Size { get; init; }

    [JsonRequired]
    [SysTextJsonRequired]
    public string Sha1 { get; init; } = null!;
}

public sealed record FileDownloadRangeResult : IActionResult
{
    [JsonRequired]
    [SysTextJsonRequired]
    public string Content { get; init; } = null!;
}

public sealed record GetFileInfoResult : IActionResult
{
    [JsonRequired]
    [SysTextJsonRequired]
    public FileMetadata Meta { get; init; } = null!;
}

public sealed record GetDirectoryInfoResult : IActionResult
{
    public string? Parent { get; init; }

    [JsonRequired]
    [SysTextJsonRequired]
    public DirectoryEntry.FileInformation[] Files { get; init; } = null!;

    [JsonRequired]
    [SysTextJsonRequired]
    public DirectoryEntry.DirectoryInformation[] Directories { get; init; } = null!;
}

public sealed record AddInstanceResult : IActionResult
{
    [JsonRequired]
    [SysTextJsonRequired]
    public InstanceConfig Config { get; init; } = null!;
}

public sealed record GetInstanceReportResult : IActionResult
{
    [JsonRequired]
    [SysTextJsonRequired]
    public InstanceReport Report { get; init; } = null!;
}

public sealed record GetInstanceLogHistoryResult : IActionResult
{
    [JsonRequired]
    [SysTextJsonRequired]
    public string[] Logs { get; init; } = null!;
}

public sealed record GetAllReportsResult : IActionResult
{
    [JsonRequired]
    [SysTextJsonRequired]
    public Dictionary<Guid, InstanceReport> Reports { get; init; } = null!;
}

public sealed record GetSystemInfoResult : IActionResult
{
    [JsonRequired]
    [SysTextJsonRequired]
    public SystemInfo Info { get; init; }
}

public sealed record GetEventRulesResult : IActionResult
{
    [JsonProperty("rules")]
    [SysTextJsonPropertyName("rules")]
    public List<EventTrigger.EventRule> Rules { get; init; } = new();
}
