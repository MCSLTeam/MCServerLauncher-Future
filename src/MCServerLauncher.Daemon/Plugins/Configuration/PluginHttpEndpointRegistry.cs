using System.Net;

namespace MCServerLauncher.Daemon.Plugins.Configuration;

/// <summary>
/// Tracks port ownership for plugin HTTP listeners so two plugins (or a plugin and the
/// daemon's own /api/v2 port) never bind the same port. The conflict key is the PORT, not the
/// IP address: a daemon binding 0.0.0.0:11452 occupies port 11452 on every address, so a plugin
/// binding 127.0.0.1:11452 must be rejected too. Validate+register only; the plugin still opens
/// its own listener.
/// </summary>
internal sealed class PluginHttpEndpointRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<int, (string Owner, IPEndPoint Endpoint)> _ports = new();

    internal bool TryRegister(string pluginId, IPEndPoint endpoint, out string conflictOwner)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        ArgumentNullException.ThrowIfNull(endpoint);
        conflictOwner = string.Empty;
        lock (_gate)
        {
            if (_ports.TryGetValue(endpoint.Port, out var existing))
            {
                conflictOwner = existing.Owner;
                return false;
            }

            _ports[endpoint.Port] = (pluginId, endpoint);
            return true;
        }
    }

    internal void Release(string pluginId)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        lock (_gate)
        {
            var ports = _ports
                .Where(pair => StringComparer.Ordinal.Equals(pair.Value.Owner, pluginId))
                .Select(static pair => pair.Key)
                .ToArray();
            foreach (var port in ports)
                _ports.Remove(port);
        }
    }
}
