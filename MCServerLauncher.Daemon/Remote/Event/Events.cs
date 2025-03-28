using MCServerLauncher.Common.ProtoType.Event;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCServerLauncher.Daemon.Remote.Event;

public static class EventTypeExtensions
{
    private static readonly JValue NullToken = JValue.CreateNull();

    /// <summary>
    ///     获取事件元数据,元数据是事件的一个属性,有的具有(额外)元数据,有的没有(仅需事件类型就可以区分)
    /// </summary>
    /// <param name="type">事件类型</param>
    /// <param name="metaToken">元数据原始token</param>
    /// <param name="settings">元数据序列化器设置</param>
    /// <exception cref="NullReferenceException">当Event要求EventMeta而metaToken为空,抛出空引用异常</exception>
    /// <exception cref="ArgumentException"><see cref="metaToken" />的值为null</exception>
    /// <returns></returns>
    public static IEventMeta? GetEventMeta(this EventType type, JToken? metaToken,
        JsonSerializerSettings? settings = null)
    {
        return type switch
        {
            EventType.InstanceLog => PrivateGetEventMeta<InstanceLogEventMeta>(metaToken,
                settings is null ? JsonSerializer.Create(settings) : JsonSerializer.CreateDefault()),
            _ => null
        };
    }

    private static TEventMeta? PrivateGetEventMeta<TEventMeta>(JToken? token, JsonSerializer serializer)
        where TEventMeta : class, IEventMeta
    {
        if (token is null) return null;

        if (token.Equals(NullToken)) throw new ArgumentException("event meta token is null");

        return token.ToObject<TEventMeta>(serializer);
    }
}