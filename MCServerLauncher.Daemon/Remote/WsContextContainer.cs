using System.Collections;
using System.Collections.Concurrent;

namespace MCServerLauncher.Daemon.Remote;

public class WsContextContainer(GracefulShutdown? gracefulShutdown = null) : IEnumerable<KeyValuePair<string, WsContext>>
{
    private readonly ConcurrentDictionary<string, WsContext> _contexts = new();

    public IEnumerator<KeyValuePair<string, WsContext>> GetEnumerator()
    {
        return _contexts.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _contexts.GetEnumerator();
    }

    public WsContext GetContext(string clientId)
    {
        return _contexts[clientId];
    }

    public WsContext CreateContext(string clientId, Guid jti, string? permissions, DateTime expiredTo)
    {
        var shutdownToken = gracefulShutdown?.CancellationToken ?? CancellationToken.None;
        var context = new WsContext(clientId, jti, permissions, expiredTo, shutdownToken);
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