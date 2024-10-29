namespace MCServerLauncher.Daemon.Remote.Event;

public class EventService : IEventService
{
    public event Action<EventType, IEventData>? Signal;

    public void OnEvent(EventType type, IEventData data)
    {
        Signal?.Invoke(type, data);
    }
}