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