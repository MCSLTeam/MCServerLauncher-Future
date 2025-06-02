using System.Collections.Generic;
using MCServerLauncher.Common.ProtoType.Event;

namespace MCServerLauncher.DaemonClient;

/// <summary>
///     保存Daemon客户端已经订阅的Daemon事件的类, 用于外部获取已订阅的事件以及客户端内部在断线重连后自动恢复已订阅的事件
/// </summary>
public class SubscribedEvents
{
    internal readonly HashSet<(EventType Type, IEventMeta? Meta)> EventSet = new();

    /// <summary>
    ///     获取已经订阅的事件
    /// </summary>
    public HashSet<(EventType Type, IEventMeta? Meta)> Events => new(EventSet);
}