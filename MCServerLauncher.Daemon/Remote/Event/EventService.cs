namespace MCServerLauncher.Daemon.Remote.Event;

public class EventService : IEventService
{
    public event Action<EventType, Dictionary<string, object>> Signal;

    public void OnEvent(EventType type, Dictionary<string, object> data)
    {
        Signal?.Invoke(type, data);
    }
}