using System.Collections.Generic;
using MCServerLauncher.Common.ProtoType.Event;

namespace MCServerLauncher.DaemonClient;

public class SubscribedEvents
{
    internal readonly HashSet<(EventType Type, IEventMeta? Meta)> EventSet = new();

    public HashSet<(EventType Type, IEventMeta? Meta)> Events => new(EventSet);
}