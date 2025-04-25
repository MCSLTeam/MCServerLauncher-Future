using Newtonsoft.Json;

namespace MCServerLauncher.Common.ProtoType.Event;

public interface IEventMeta
{
}

[JsonObject(ItemRequired = Required.Always)]
public sealed record InstanceLogEventMeta : IEventMeta
{
    public Guid InstanceId { get; init; }
}