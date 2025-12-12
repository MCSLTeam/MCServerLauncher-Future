using Newtonsoft.Json;

namespace MCServerLauncher.Common.ProtoType.Event;

public interface IEventFilter
{
}

[JsonObject(ItemRequired = Required.Always)]
public sealed record InstanceLogEventFilter : IEventFilter
{
    public Guid InstanceId { get; init; }
}