namespace MCServerLauncher.Daemon.Remote.Event;

public interface IEventService
{
    public event  Action<EventType, Dictionary<string, object>> Signal;
    void OnEvent(EventType type, Dictionary<string, object> data);
}