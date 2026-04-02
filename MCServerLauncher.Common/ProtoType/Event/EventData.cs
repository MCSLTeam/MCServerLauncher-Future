using MCServerLauncher.Common.ProtoType.Status;
using Newtonsoft.Json;
using SysTextJsonRequired = System.Text.Json.Serialization.JsonRequiredAttribute;

namespace MCServerLauncher.Common.ProtoType.Event;

public interface IEventData
{
}

public sealed record InstanceLogEventData : IEventData
{
    [JsonRequired]
    [SysTextJsonRequired]
    public string Log { get; init; } = null!;
}

public sealed record DaemonReportEventData : IEventData
{
    [JsonRequired]
    [SysTextJsonRequired]
    public DaemonReport Report { get; init; }
}