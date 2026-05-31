using MCServerLauncher.Common.ProtoType.Serialization;
using SysTextJsonConverter = System.Text.Json.Serialization.JsonConverterAttribute;
using SysTextJsonRequired = System.Text.Json.Serialization.JsonRequiredAttribute;
using SysTextJsonPropertyName = System.Text.Json.Serialization.JsonPropertyNameAttribute;

namespace MCServerLauncher.Common.ProtoType.Event;

public record EventPacket
{
    [SysTextJsonPropertyName("event")]
    [SysTextJsonRequired]
    public EventType EventType { get; init; }

    [SysTextJsonPropertyName("meta")]
    [SysTextJsonConverter(typeof(StjJsonPayloadBufferConverter))]
    public JsonPayloadBuffer? EventMeta { get; init; }

    [SysTextJsonPropertyName("data")]
    [SysTextJsonRequired]
    [SysTextJsonConverter(typeof(StjJsonPayloadBufferConverter))]
    public JsonPayloadBuffer? EventData { get; init; }

    [SysTextJsonPropertyName("time")]
    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
