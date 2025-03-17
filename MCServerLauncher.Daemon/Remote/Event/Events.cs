using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCServerLauncher.Daemon.Remote.Event;

public enum EventType
{
    InstanceLog
}

public static class EventTypeExtensions
{
    /// <summary>
    ///     获取事件元数据,元数据是事件的一个属性,有的具有(额外)元数据,有的没有(仅需事件类型就可以区分)
    /// </summary>
    /// <param name="type">事件类型</param>
    /// <param name="token">元数据原始token</param>
    /// <param name="serializer">元数据序列化器</param>
    /// <returns></returns>
    public static IEventMeta? GetEventMeta(this EventType type, JToken token, JsonSerializer? serializer = null)
    {
        return type switch
        {
            EventType.InstanceLog => token.ToObject<InstanceLogEventMeta>(serializer ?? JsonSerializer.CreateDefault()),
            _ => null
        };
    }
}

#region Event Meta

public interface IEventMeta
{
}

public record InstanceLogEventMeta(Guid InstanceId) : IEventMeta;

#endregion