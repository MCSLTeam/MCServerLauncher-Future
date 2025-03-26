using Newtonsoft.Json;

namespace MCServerLauncher.Common.ProtoType.Event;

public interface IEventData
{
}

public sealed record InstanceLogEventData : IEventData
{
    [JsonRequired] public string Log { get; init; }
}