using MCServerLauncher.Common.ProtoType.Status;
using SysTextJsonRequired = System.Text.Json.Serialization.JsonRequiredAttribute;

namespace MCServerLauncher.Common.ProtoType.Event;

public interface IEventData
{
}

public sealed record InstanceLogEventData : IEventData
{
    [SysTextJsonRequired]
    public string Log { get; init; } = null!;
}

public sealed record DaemonReportEventData : IEventData
{
    [SysTextJsonRequired]
    public DaemonReport Report { get; init; }
}