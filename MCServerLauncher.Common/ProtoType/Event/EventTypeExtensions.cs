using System.Text.Json;
using MCServerLauncher.Common.ProtoType.Serialization;

namespace MCServerLauncher.Common.ProtoType.Event;

public static class EventTypeExtensions
{
    /// <summary>
    ///     获取事件元数据, 元数据是事件的一个属性, 有的具有(额外)元数据, 有的没有(仅需事件类型就可以区分)。
    ///     约定: null(缺失meta) => 返回null; 显式JSON null(meta:null) => 抛出异常。
    ///     Uses Common-owned STJ deserialization as the canonical wire path.
    /// </summary>
    /// <param name="type">事件类型</param>
    /// <param name="metaToken">元数据原始负载</param>
    /// <param name="options">STJ serializer options; defaults to Common's <see cref="StjResolver" /> options when null</param>
    /// <exception cref="ArgumentException"><see cref="metaToken" />是显式 JSON null 时抛出</exception>
    public static IEventMeta? GetEventMeta(this EventType type, JsonPayloadBuffer? metaToken,
        JsonSerializerOptions? options = null)
    {
        return type switch
        {
            EventType.InstanceLog => PrivateGetEventMeta<InstanceLogEventMeta>(metaToken, options),
            _ => null
        };
    }

    /// <summary>
    ///     获取事件数据。
    ///     Uses Common-owned STJ deserialization as the canonical wire path.
    /// </summary>
    /// <param name="type">事件类型</param>
    /// <param name="metaData">数据原始负载</param>
    /// <param name="options">STJ serializer options; defaults to Common's <see cref="StjResolver" /> options when null</param>
    /// <exception cref="ArgumentException"><see cref="metaData" />是显式 JSON null 时抛出</exception>
    public static IEventData? GetEventData(this EventType type, JsonPayloadBuffer? metaData,
        JsonSerializerOptions? options = null)
    {
        return type switch
        {
            EventType.InstanceLog => PrivateGetEventData<InstanceLogEventData>(metaData, options),
            EventType.DaemonReport => PrivateGetEventData<DaemonReportEventData>(metaData, options),
            _ => null
        };
    }

    private static T? DeserializeWithStj<T>(JsonPayloadBuffer buffer, JsonSerializerOptions? options)
    {
        var effectiveOptions = options ?? StjResolver.CreateDefaultOptions();
        return JsonSerializer.Deserialize<T>(buffer.Value, effectiveOptions);
    }

    private static IEventData? PrivateGetEventData<TEventData>(JsonPayloadBuffer? token, JsonSerializerOptions? options)
        where TEventData : class, IEventData
    {
        if (token is null) return null;

        if (token.Value.IsExplicitJsonNull)
        {
            throw new ArgumentException("event data payload is explicit json null");
        }

        return DeserializeWithStj<TEventData>(token.Value, options);
    }

    private static TEventMeta? PrivateGetEventMeta<TEventMeta>(JsonPayloadBuffer? token, JsonSerializerOptions? options)
        where TEventMeta : class, IEventMeta
    {
        if (token is null) return null;

        if (token.Value.IsExplicitJsonNull)
        {
            throw new ArgumentException("event meta payload is explicit json null");
        }

        return DeserializeWithStj<TEventMeta>(token.Value, options);
    }
}
