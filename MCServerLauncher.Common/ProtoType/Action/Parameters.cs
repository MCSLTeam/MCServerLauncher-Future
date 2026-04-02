using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Instance;
using System.Text.Json;
using MCServerLauncher.Common.ProtoType.Serialization;
using Newtonsoft.Json;
using SysTextJsonRequired = System.Text.Json.Serialization.JsonRequiredAttribute;
using SysTextJsonPropertyName = System.Text.Json.Serialization.JsonPropertyNameAttribute;

namespace MCServerLauncher.Common.ProtoType.Action;

public interface IActionParameter
{
}

public sealed record EmptyActionParameter : IActionParameter;

public sealed record SubscribeEventParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public EventType Type { get; init; }

    [JsonProperty(PropertyName = "meta", Required = Required.AllowNull)]
    [SysTextJsonPropertyName("meta")]
    [JsonConverter(typeof(NewtonsoftJsonElementConverter))]
    [SysTextJsonRequired]
    public JsonElement? Meta { get; init; }
}

public sealed record UnsubscribeEventParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public EventType Type { get; init; }

    [JsonProperty(PropertyName = "meta", Required = Required.AllowNull)]
    [SysTextJsonPropertyName("meta")]
    [JsonConverter(typeof(NewtonsoftJsonElementConverter))]
    [SysTextJsonRequired]
    public JsonElement? Meta { get; init; }
}

public sealed record FileUploadRequestParameter : IActionParameter
{
    public string? Path { get; init; }
    public string? Sha1 { get; init; }
    public long? Timeout { get; init; }
    [JsonRequired]
    [SysTextJsonRequired]
    public long Size { get; init; }
}

public sealed record FileUploadChunkParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public Guid FileId { get; init; }

    [JsonRequired]
    [SysTextJsonRequired]
    public long Offset { get; init; }

    [JsonRequired]
    [SysTextJsonRequired]
    public string Data { get; init; } = null!;
}

public sealed record FileUploadCancelParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public Guid FileId { get; init; }
}

public sealed record FileDownloadRequestParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public string Path { get; init; } = null!;
    public long? Timeout { get; init; }
}

public sealed record FileDownloadRangeParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public Guid FileId { get; init; }

    [JsonRequired]
    [SysTextJsonRequired]
    public string Range { get; init; } = null!;
}

public sealed record FileDownloadCloseParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public Guid FileId { get; init; }
}

public sealed record GetFileInfoParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public string Path { get; init; } = null!;
}

public sealed record GetDirectoryInfoParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public string Path { get; init; } = null!;
}

public sealed record DeleteFileParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public string Path { get; init; } = null!;
}

public sealed record DeleteDirectoryParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public string Path { get; init; } = null!;

    [JsonRequired]
    [SysTextJsonRequired]
    public bool Recursive { get; init; }
}

public sealed record RenameFileParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public string Path { get; init; } = null!;

    [JsonRequired]
    [SysTextJsonRequired]
    public string NewName { get; init; } = null!;
}

public sealed record RenameDirectoryParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public string Path { get; init; } = null!;

    [JsonRequired]
    [SysTextJsonRequired]
    public string NewName { get; init; } = null!;
}

public sealed record CreateDirectoryParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public string Path { get; init; } = null!;
}

public sealed record MoveFileParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public string SourcePath { get; init; } = null!;

    [JsonRequired]
    [SysTextJsonRequired]
    public string DestinationPath { get; init; } = null!;
}

public sealed record MoveDirectoryParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public string SourcePath { get; init; } = null!;

    [JsonRequired]
    [SysTextJsonRequired]
    public string DestinationPath { get; init; } = null!;
}

public sealed record CopyFileParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public string SourcePath { get; init; } = null!;

    [JsonRequired]
    [SysTextJsonRequired]
    public string DestinationPath { get; init; } = null!;
}

public sealed record CopyDirectoryParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public string SourcePath { get; init; } = null!;

    [JsonRequired]
    [SysTextJsonRequired]
    public string DestinationPath { get; init; } = null!;
}

public sealed record AddInstanceParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public InstanceFactorySetting Setting { get; init; } = null!;
}

public sealed record RemoveInstanceParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public Guid Id { get; init; }
}

public sealed record StartInstanceParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public Guid Id { get; init; }
}

public sealed record StopInstanceParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public Guid Id { get; init; }
}

public sealed record SendToInstanceParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public Guid Id { get; init; }

    [JsonRequired]
    [SysTextJsonRequired]
    public string Message { get; init; } = null!;
}

public sealed record KillInstanceParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public Guid Id { get; init; }
}

public sealed record GetInstanceReportParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public Guid Id { get; init; }
}

public sealed record GetInstanceLogHistoryParameter : IActionParameter
{
    [JsonRequired]
    [SysTextJsonRequired]
    public Guid Id { get; init; }
}
