using MCServerLauncher.Common.ProtoType.Event;

namespace MCServerLauncher.Daemon.Remote.Event;

public class EventService : IEventService
{
    public event Action<EventType, IEventMeta?, IEventData?>? Signal;

    public void OnEvent(EventType type, IEventMeta? meta, IEventData? data)
    {
        Signal?.Invoke(type, meta, data);
    }
}

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
}