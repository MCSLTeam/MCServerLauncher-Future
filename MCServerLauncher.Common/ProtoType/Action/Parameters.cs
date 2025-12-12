using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Instance;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCServerLauncher.Common.ProtoType.Action;

public interface IActionParameter
{
}

public sealed record EmptyActionParameter : IActionParameter;

public sealed record SubscribeEventParameter : IActionParameter
{
    [JsonRequired] public EventType Type { get; init; }
    public JToken? Filter { get; init; }
}

public sealed record UnsubscribeEventParameter : IActionParameter
{
    [JsonRequired] public Guid Subscriber { get; init; }
}

public sealed record FileUploadRequestParameter : IActionParameter
{
    public string? Path { get; init; }
    public string? Sha1 { get; init; }
    public long? Timeout { get; init; }
    [JsonRequired] public long Size { get; init; }
}

public sealed record FileUploadChunkParameter : IActionParameter
{
    [JsonRequired] public Guid FileId { get; init; }
    [JsonRequired] public long Offset { get; init; }
    [JsonRequired] public string Data { get; init; } = null!;
}

public sealed record FileUploadCancelParameter : IActionParameter
{
    [JsonRequired] public Guid FileId { get; init; }
}

public sealed record FileDownloadRequestParameter : IActionParameter
{
    [JsonRequired] public string Path { get; init; } = null!;
    public long? Timeout { get; init; }
}

public sealed record FileDownloadRangeParameter : IActionParameter
{
    [JsonRequired] public Guid FileId { get; init; }
    [JsonRequired] public string Range { get; init; } = null!;
}

public sealed record FileDownloadCloseParameter : IActionParameter
{
    [JsonRequired] public Guid FileId { get; init; }
}

public sealed record GetFileInfoParameter : IActionParameter
{
    [JsonRequired] public string Path { get; init; } = null!;
}

public sealed record GetDirectoryInfoParameter : IActionParameter
{
    [JsonRequired] public string Path { get; init; } = null!;
}

public sealed record AddInstanceParameter : IActionParameter
{
    [JsonRequired] public InstanceFactorySetting Setting { get; init; } = null!;
}

public sealed record RemoveInstanceParameter : IActionParameter
{
    [JsonRequired] public Guid Id { get; init; }
}

public sealed record StartInstanceParameter : IActionParameter
{
    [JsonRequired] public Guid Id { get; init; }
}

public sealed record StopInstanceParameter : IActionParameter
{
    [JsonRequired] public Guid Id { get; init; }
}

public sealed record SendToInstanceParameter : IActionParameter
{
    [JsonRequired] public Guid Id { get; init; }
    [JsonRequired] public string Message { get; init; } = null!;
}

public sealed record KillInstanceParameter : IActionParameter
{
    [JsonRequired] public Guid Id { get; init; }
}

public sealed record GetInstanceReportParameter : IActionParameter
{
    [JsonRequired] public Guid Id { get; init; }
}