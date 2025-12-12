using MCServerLauncher.Common.ProtoType.Event;

namespace MCServerLauncher.Daemon.Remote.Event;

public class EventService : IEventService
{
    public event Action<EventType, IEventFilter?, IEventData?>? Signal;

    public void OnEvent(EventType type, IEventFilter? meta, IEventData? data)
    {
        Signal?.Invoke(type, meta, data);
    }
}