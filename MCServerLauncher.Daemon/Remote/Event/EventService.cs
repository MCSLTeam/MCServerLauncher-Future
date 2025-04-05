using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Status;

namespace MCServerLauncher.Daemon.Remote.Event;

public class EventService : IEventService
{
    public event Action<EventType, IEventMeta?, IEventData?>? Signal;

    public void OnEvent(EventType type, IEventMeta? meta, IEventData? data)
    {
        Signal?.Invoke(type, meta, data);
    }
}

/// <summary>
///     事件服务扩展, 各种事件的触发函数都在这里实现
/// </summary>
public static class EventServiceExtensions
{
    public static void OnInstanceLog(this IEventService service, Guid instanceId, string log)
    {
        service.OnEvent(
            EventType.InstanceLog,
            new InstanceLogEventMeta { InstanceId = instanceId },
            new InstanceLogEventData { Log = log }
        );
    }

    public static void OnDaemonReport(this IEventService service, DaemonReport report)
    {
        service.OnEvent(
            EventType.DaemonReport,
            null,
            new DaemonReportEventData { Report = report }
        );
    }
}