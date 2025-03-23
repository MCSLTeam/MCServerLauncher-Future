using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Event.Meta;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCServerLauncher.Daemon.Remote.Event;

public static class EventTypeExtensions
{
    /// <summary>
    ///     获取事件元数据,元数据是事件的一个属性,有的具有(额外)元数据,有的没有(仅需事件类型就可以区分)
    /// </summary>
    /// <param name="type">事件类型</param>
    /// <param name="metaToken">元数据原始token</param>
    /// <param name="serializer">元数据序列化器</param>
    /// <exception cref="NullReferenceException">当Event要求EventMeta而metaToken为空,抛出空引用异常</exception>
    /// <returns></returns>
    public static IEventMeta? GetEventMeta(this EventType type, JToken? metaToken, JsonSerializer? serializer = null)
    {
        return type switch
        {
            EventType.InstanceLog => metaToken!.ToObject<InstanceLogEventMeta>(serializer ?? JsonSerializer.CreateDefault()),
            _ => null
        };
    }
}
