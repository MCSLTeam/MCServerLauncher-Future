using Newtonsoft.Json;

namespace MCServerLauncher.Common.ProtoType.Event;

public record EventPacket
{
    [JsonProperty(Required = Required.Always, PropertyName = "event")]
    public EventType EventType { get; init; }

    [JsonProperty(Required = Required.AllowNull, PropertyName = "meta")]
    public IEventMeta? EventMeta { get; init; }

    [JsonProperty(Required = Required.Always, PropertyName = "data")]
    public IEventData? EventData { get; init; }

    [JsonProperty(PropertyName = "time")]
    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}