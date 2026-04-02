using Newtonsoft.Json;
using SysTextJsonRequired = System.Text.Json.Serialization.JsonRequiredAttribute;
using SysTextJsonPropertyName = System.Text.Json.Serialization.JsonPropertyNameAttribute;

namespace MCServerLauncher.Common.ProtoType.Event;

public interface IEventMeta
{
}

[JsonObject(ItemRequired = Required.Always)]
public sealed record InstanceLogEventMeta : IEventMeta
{
    [JsonProperty(PropertyName = "instance_id", Required = Required.Always)]
    [SysTextJsonPropertyName("instance_id")]
    [SysTextJsonRequired]
    public Guid InstanceId { get; init; }
}
