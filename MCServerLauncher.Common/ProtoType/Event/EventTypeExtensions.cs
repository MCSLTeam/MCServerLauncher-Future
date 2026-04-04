using System.IO;
using MCServerLauncher.Common.Internal.Performance;
using MCServerLauncher.Common.ProtoType.Serialization;
using Newtonsoft.Json;

namespace MCServerLauncher.Common.ProtoType.Event;

public static class EventTypeExtensions
{
    /// <summary>
    ///     获取事件元数据, 元数据是事件的一个属性, 有的具有(额外)元数据, 有的没有(仅需事件类型就可以区分)。
    ///     约定: null(缺失meta) => 返回null; 显式JSON null(meta:null) => 抛出异常。
    /// </summary>
    /// <param name="type">事件类型</param>
    /// <param name="metaToken">元数据原始负载</param>
    /// <param name="settings">元数据序列化器设置</param>
    /// <exception cref="ArgumentException"><see cref="metaToken" />是显式 JSON null 时抛出</exception>
    public static IEventMeta? GetEventMeta(this EventType type, JsonPayloadBuffer? metaToken,
        JsonSerializerSettings? settings = null)
    {
        return type switch
        {
            EventType.InstanceLog => PrivateGetEventMeta<InstanceLogEventMeta>(metaToken,
                settings is null ? JsonSerializer.CreateDefault() : JsonSerializer.Create(settings)),
            _ => null
        };
    }

    public static IEventData? GetEventData(this EventType type, JsonPayloadBuffer? metaData,
        JsonSerializerSettings? settings = null)
    {
        return type switch
        {
            EventType.InstanceLog => PrivateGetEventData<InstanceLogEventData>(metaData,
                settings is null ? JsonSerializer.CreateDefault() : JsonSerializer.Create(settings)),
            EventType.DaemonReport => PrivateGetEventData<DaemonReportEventData>(metaData,
                settings is null ? JsonSerializer.CreateDefault() : JsonSerializer.Create(settings)),
            _ => null
        };
    }

    private static IEventData? PrivateGetEventData<TEventData>(JsonPayloadBuffer? token, JsonSerializer serializer)
        where TEventData : class, IEventData
    {
        if (token is null) return null;

        if (token.Value.IsExplicitJsonNull)
        {
            throw new ArgumentException("event data payload is explicit json null");
        }

        return DeserializeWithNewtonsoft<TEventData>(token.Value, serializer);
    }

    private static TEventMeta? PrivateGetEventMeta<TEventMeta>(JsonPayloadBuffer? token, JsonSerializer serializer)
        where TEventMeta : class, IEventMeta
    {
        if (token is null) return null;

        if (token.Value.IsExplicitJsonNull)
        {
            throw new ArgumentException("event meta payload is explicit json null");
        }

        return DeserializeWithNewtonsoft<TEventMeta>(token.Value, serializer);
    }

    private static T? DeserializeWithNewtonsoft<T>(JsonPayloadBuffer buffer, JsonSerializer serializer)
    {
        var view = new JsonPayloadBufferView(buffer);
        var rawJson = JsonPayloadBufferAdapters.GetRawJson(view);
        using var textReader = new StringReader(rawJson);
        using var jsonReader = new JsonTextReader(textReader);
        return serializer.Deserialize<T>(jsonReader);
    }
}
