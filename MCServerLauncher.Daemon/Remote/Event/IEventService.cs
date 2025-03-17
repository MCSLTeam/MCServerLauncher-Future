namespace MCServerLauncher.Daemon.Remote.Event;

public interface IEventService
{
    event Action<EventType, IEventMeta?, object?>? Signal;

    /// <summary>
    ///     触发事件。特别的: meta为null时将忽略meta,全部触发(仅当事件类型包含额外元数据)
    /// </summary>
    /// <param name="type"></param>
    /// <param name="meta"></param>
    /// <param name="data"></param>
    void OnEvent(EventType type, IEventMeta? meta, object? data);
}