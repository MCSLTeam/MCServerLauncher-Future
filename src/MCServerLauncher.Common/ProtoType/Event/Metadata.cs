using SysTextJsonRequired = System.Text.Json.Serialization.JsonRequiredAttribute;
using SysTextJsonPropertyName = System.Text.Json.Serialization.JsonPropertyNameAttribute;

namespace MCServerLauncher.Common.ProtoType.Event;

public interface IEventMeta
{
}

public sealed record InstanceLogEventMeta : IEventMeta
{
    [SysTextJsonPropertyName("instance_id")]
    [SysTextJsonRequired]
    public Guid InstanceId { get; init; }
}
