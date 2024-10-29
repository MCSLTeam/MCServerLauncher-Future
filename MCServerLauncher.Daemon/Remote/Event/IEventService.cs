namespace MCServerLauncher.Daemon.Remote.Event;

public interface IEventService
{
    public event Action<EventType, IEventData>? Signal;
    void OnEvent(EventType type, IEventData data);
}