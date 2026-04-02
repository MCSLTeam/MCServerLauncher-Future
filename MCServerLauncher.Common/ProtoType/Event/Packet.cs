using MCServerLauncher.Common.ProtoType.Serialization;
using Newtonsoft.Json;
using SysTextJsonConverter = System.Text.Json.Serialization.JsonConverterAttribute;
using SysTextJsonRequired = System.Text.Json.Serialization.JsonRequiredAttribute;
using SysTextJsonPropertyName = System.Text.Json.Serialization.JsonPropertyNameAttribute;

namespace MCServerLauncher.Common.ProtoType.Event;

public record EventPacket
{
    [JsonProperty(Required = Required.Always, PropertyName = "event")]
    [SysTextJsonPropertyName("event")]
    [SysTextJsonRequired]
    public EventType EventType { get; init; }

    [JsonProperty(Required = Required.AllowNull, PropertyName = "meta")]
    [SysTextJsonPropertyName("meta")]
    [JsonConverter(typeof(NewtonsoftJsonPayloadBufferConverter))]
    [SysTextJsonConverter(typeof(StjJsonPayloadBufferConverter))]
    public JsonPayloadBuffer? EventMeta { get; init; }

    [JsonProperty(Required = Required.Always, PropertyName = "data")]
    [SysTextJsonPropertyName("data")]
    [SysTextJsonRequired]
    [JsonConverter(typeof(NewtonsoftJsonPayloadBufferConverter))]
    [SysTextJsonConverter(typeof(StjJsonPayloadBufferConverter))]
    public JsonPayloadBuffer? EventData { get; init; }

    [JsonProperty(PropertyName = "time")]
    [SysTextJsonPropertyName("time")]
    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
