using System.Collections.Concurrent;

namespace MCServerLauncher.Daemon.Remote;

public class WsContextContainer
{
    private readonly ConcurrentDictionary<string, WsContext> _contexts = new();

    public WsContext? GetContext(string clientId)
    {
        return _contexts.GetValueOrDefault(clientId);
    }

    public WsContext CreateContext(string clientId, string jti, string? permissions, DateTime expiredTo)
    {
        var context = new WsContext(clientId, jti, permissions, expiredTo);
        _contexts.TryAdd(clientId, context);
        return context;
    }

    public WsContext RemoveContext(string clientId)
    {
        _contexts.TryRemove(clientId, out var rv);
        return rv!;
    }

    public IEnumerable<string> GetClientIds()
    {
        return _contexts.Keys;
    }
}