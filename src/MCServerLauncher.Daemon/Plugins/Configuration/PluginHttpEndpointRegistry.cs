using System.Net;

namespace MCServerLauncher.Daemon.Plugins.Configuration;

/// <summary>
/// Tracks IP:port ownership for plugin HTTP listeners so two plugins (or a plugin and the
/// daemon's own /api/v2 port) never bind the same endpoint. Validate+register only; the plugin
/// still opens its own listener. IP:port is the conflict key, not the path.
/// </summary>
internal sealed class PluginHttpEndpointRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _endpoints = new(StringComparer.Ordinal);

    internal bool TryRegister(string pluginId, IPEndPoint endpoint, out string conflictOwner)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        ArgumentNullException.ThrowIfNull(endpoint);
        conflictOwner = string.Empty;
        var key = Key(endpoint);
        lock (_gate)
        {
            if (_endpoints.TryGetValue(key, out var owner))
            {
                conflictOwner = owner;
                return false;
            }

            _endpoints[key] = pluginId;
            return true;
        }
    }

    internal void Release(string pluginId)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        lock (_gate)
        {
            var keys = _endpoints
                .Where(pair => StringComparer.Ordinal.Equals(pair.Value, pluginId))
                .Select(static pair => pair.Key)
                .ToArray();
            foreach (var key in keys)
                _endpoints.Remove(key);
        }
    }

    private static string Key(IPEndPoint endpoint) => $"{endpoint.Address}:{endpoint.Port}";
}
