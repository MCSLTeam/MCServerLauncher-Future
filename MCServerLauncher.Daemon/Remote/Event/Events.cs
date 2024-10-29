namespace MCServerLauncher.Daemon.Remote.Event;

public interface IEventData{}

public static class Events
{
    public record struct InstanceLogEvent(string InstanceName, string Log) : IEventData;
}