using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Instance;
using System.Text.Json;
using SysTextJsonRequired = System.Text.Json.Serialization.JsonRequiredAttribute;
using SysTextJsonPropertyName = System.Text.Json.Serialization.JsonPropertyNameAttribute;

namespace MCServerLauncher.Common.ProtoType.Action;

public interface IActionParameter
{
}

public sealed record EmptyActionParameter : IActionParameter;

public sealed record SubscribeEventParameter : IActionParameter
{
    [SysTextJsonRequired]
    public EventType Type { get; init; }

    [SysTextJsonPropertyName("meta")]
    [SysTextJsonRequired]
    public JsonElement? Meta { get; init; }
}

public sealed record UnsubscribeEventParameter : IActionParameter
{
    [SysTextJsonRequired]
    public EventType Type { get; init; }

    [SysTextJsonPropertyName("meta")]
    [SysTextJsonRequired]
    public JsonElement? Meta { get; init; }
}

public sealed record FileUploadRequestParameter : IActionParameter
{
    public string? Path { get; init; }
    public string? Sha1 { get; init; }
    public long? Timeout { get; init; }
    [SysTextJsonRequired]
    public long Size { get; init; }
}

public sealed record FileUploadChunkParameter : IActionParameter
{
    [SysTextJsonRequired]
    public Guid FileId { get; init; }

    [SysTextJsonRequired]
    public long Offset { get; init; }

    [SysTextJsonRequired]
    public string Data { get; init; } = null!;
}

public sealed record FileUploadCancelParameter : IActionParameter
{
    [SysTextJsonRequired]
    public Guid FileId { get; init; }
}

public sealed record FileDownloadRequestParameter : IActionParameter
{
    [SysTextJsonRequired]
    public string Path { get; init; } = null!;
    public long? Timeout { get; init; }
}

public sealed record FileDownloadRangeParameter : IActionParameter
{
    [SysTextJsonRequired]
    public Guid FileId { get; init; }

    [SysTextJsonRequired]
    public string Range { get; init; } = null!;
}

public sealed record FileDownloadCloseParameter : IActionParameter
{
    [SysTextJsonRequired]
    public Guid FileId { get; init; }
}

public sealed record GetFileInfoParameter : IActionParameter
{
    [SysTextJsonRequired]
    public string Path { get; init; } = null!;
}

public sealed record GetDirectoryInfoParameter : IActionParameter
{
    [SysTextJsonRequired]
    public string Path { get; init; } = null!;
}

public sealed record DeleteFileParameter : IActionParameter
{
    [SysTextJsonRequired]
    public string Path { get; init; } = null!;
}

public sealed record DeleteDirectoryParameter : IActionParameter
{
    [SysTextJsonRequired]
    public string Path { get; init; } = null!;

    [SysTextJsonRequired]
    public bool Recursive { get; init; }
}

public sealed record RenameFileParameter : IActionParameter
{
    [SysTextJsonRequired]
    public string Path { get; init; } = null!;

    [SysTextJsonRequired]
    public string NewName { get; init; } = null!;
}

public sealed record RenameDirectoryParameter : IActionParameter
{
    [SysTextJsonRequired]
    public string Path { get; init; } = null!;

    [SysTextJsonRequired]
    public string NewName { get; init; } = null!;
}

public sealed record CreateDirectoryParameter : IActionParameter
{
    [SysTextJsonRequired]
    public string Path { get; init; } = null!;
}

public sealed record MoveFileParameter : IActionParameter
{
    [SysTextJsonRequired]
    public string SourcePath { get; init; } = null!;

    [SysTextJsonRequired]
    public string DestinationPath { get; init; } = null!;
}

public sealed record MoveDirectoryParameter : IActionParameter
{
    [SysTextJsonRequired]
    public string SourcePath { get; init; } = null!;

    [SysTextJsonRequired]
    public string DestinationPath { get; init; } = null!;
}

public sealed record CopyFileParameter : IActionParameter
{
    [SysTextJsonRequired]
    public string SourcePath { get; init; } = null!;

    [SysTextJsonRequired]
    public string DestinationPath { get; init; } = null!;
}

public sealed record CopyDirectoryParameter : IActionParameter
{
    [SysTextJsonRequired]
    public string SourcePath { get; init; } = null!;

    [SysTextJsonRequired]
    public string DestinationPath { get; init; } = null!;
}

public sealed record AddInstanceParameter : IActionParameter
{
    [SysTextJsonRequired]
    public InstanceFactorySetting Setting { get; init; } = null!;
}

public sealed record RemoveInstanceParameter : IActionParameter
{
    [SysTextJsonRequired]
    public Guid Id { get; init; }
}

public sealed record StartInstanceParameter : IActionParameter
{
    [SysTextJsonRequired]
    public Guid Id { get; init; }
}

public sealed record StopInstanceParameter : IActionParameter
{
    [SysTextJsonRequired]
    public Guid Id { get; init; }
}

public sealed record SendToInstanceParameter : IActionParameter
{
    [SysTextJsonRequired]
    public Guid Id { get; init; }

    [SysTextJsonRequired]
    public string Message { get; init; } = null!;
}

public sealed record KillInstanceParameter : IActionParameter
{
    [SysTextJsonRequired]
    public Guid Id { get; init; }
}

public sealed record GetInstanceReportParameter : IActionParameter
{
    [SysTextJsonRequired]
    public Guid Id { get; init; }
}

public sealed record GetInstanceLogHistoryParameter : IActionParameter
{
    [SysTextJsonRequired]
    public Guid Id { get; init; }
}
