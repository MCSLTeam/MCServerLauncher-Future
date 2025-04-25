using MCServerLauncher.Common.ProtoType.Status;
using Newtonsoft.Json;

namespace MCServerLauncher.Common.ProtoType.Event;
public interface IEventData
{
}

public sealed record InstanceLogEventData : IEventData
{
    [JsonRequired] public string Log { get; init; } = null!;
}

public sealed record DaemonReportEventData : IEventData
{
    [JsonRequired] public DaemonReport Report { get; init; } = null!;
}