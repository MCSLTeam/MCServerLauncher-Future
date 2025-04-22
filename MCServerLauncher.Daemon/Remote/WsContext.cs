using System.Collections.Concurrent;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Daemon.Remote.Authentication;

namespace MCServerLauncher.Daemon.Remote;

/// <summary>
///     线程安全的ws服务上下文
/// </summary>
public class WsContext
{
    private readonly ConcurrentDictionary<EventType, HashSet<IEventMeta>> _subscribedEvents = new();

    public WsContext(string clientId, Guid jti, string? permissions, DateTime expiredTo)
    {
        ClientId = clientId;
        Permissions = permissions is null ? Permissions.Never : new Permissions(permissions);
        ExpiredTo = expiredTo;
        JTI = jti;
    }

    public Permissions Permissions { get; }
    public DateTime ExpiredTo { get; }
    public Guid JTI { get; }
    public string ClientId { get; }

    public void SubscribeEvent(EventType type, IEventMeta? meta)
    {
        if (!_subscribedEvents.TryGetValue(type, out var set))
        {
            set = new HashSet<IEventMeta>();
            _subscribedEvents.TryAdd(type, set);
        }

        if (meta != null) set.Add(meta);
    }

    public void UnsubscribeEvent(EventType type, IEventMeta? meta)
    {
        if (meta != null)
        {
            if (_subscribedEvents.TryGetValue(type, out var set))
            {
                if (set.Count > 1) set.Remove(meta);
                else _subscribedEvents.TryRemove(type, out _);
            }
        }
        else
        {
            _subscribedEvents.TryRemove(type, out _);
        }
    }

    public bool IsSubscribedEvent(EventType type, IEventMeta? meta)
    {
        return _subscribedEvents.TryGetValue(type, out var set) && (meta == null || set.Contains(meta));
    }

    public IEnumerable<IEventMeta> GetEventMetas(EventType type)
    {
        return _subscribedEvents.TryGetValue(type, out var set)
            ? new HashSet<IEventMeta>(set)
            : Enumerable.Empty<IEventMeta>();
    }

    public void UnsubscribeAllEvents()
    {
        _subscribedEvents.Clear();
    }
}