using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Event.Meta;

namespace MCServerLauncher.Daemon.Remote.Event;

public class EventService : IEventService
{
    public event Action<EventType, IEventMeta?, object?>? Signal;

    public void OnEvent(EventType type, IEventMeta? meta, object? data)
    {
        Signal?.Invoke(type, meta, data);
    }
}

public static class EventServiceExtensions
{
    public static void OnInstanceLog(this IEventService service, Guid instanceId, string log)
    {
        service.OnEvent(EventType.InstanceLog, new InstanceLogEventMeta(instanceId), log);
    }
}