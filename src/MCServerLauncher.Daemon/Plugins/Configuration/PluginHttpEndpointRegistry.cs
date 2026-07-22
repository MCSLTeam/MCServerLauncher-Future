using System.Net;
using System.Net.Sockets;

namespace MCServerLauncher.Daemon.Plugins.Configuration;

/// <summary>
/// Tracks normalized IP:port ownership for plugin HTTP listeners. Wildcard bindings overlap
/// every address in their family, so the daemon's 0.0.0.0 binding excludes matching IPv4
/// plugin endpoints. Validate+register only; the plugin still opens its own listener.
/// </summary>
internal sealed class PluginHttpEndpointRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<IPEndPoint, string> _endpoints = new();

    internal bool TryRegister(string pluginId, IPEndPoint endpoint, out string conflictOwner)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        ArgumentNullException.ThrowIfNull(endpoint);
        var normalized = Normalize(endpoint);
        conflictOwner = string.Empty;
        lock (_gate)
        {
            foreach (var existing in _endpoints)
            {
                if (!Overlaps(existing.Key, normalized))
                    continue;

                conflictOwner = existing.Value;
                return false;
            }

            _endpoints.Add(normalized, pluginId);
            return true;
        }
    }

    internal void Release(string pluginId, IPEndPoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        ArgumentNullException.ThrowIfNull(endpoint);
        var normalized = Normalize(endpoint);
        lock (_gate)
        {
            if (_endpoints.TryGetValue(normalized, out var owner) &&
                StringComparer.Ordinal.Equals(owner, pluginId))
            {
                _endpoints.Remove(normalized);
            }
        }
    }

    internal void ReleaseAll(string pluginId)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        lock (_gate)
        {
            var endpoints = _endpoints
                .Where(pair => StringComparer.Ordinal.Equals(pair.Value, pluginId))
                .Select(static pair => pair.Key)
                .ToArray();
            foreach (var endpoint in endpoints)
                _endpoints.Remove(endpoint);
        }
    }

    internal static IPEndPoint Normalize(IPEndPoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var address = endpoint.Address;
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        else if (address.AddressFamily == AddressFamily.InterNetworkV6)
            address = new IPAddress(address.GetAddressBytes(), address.ScopeId);
        else
            address = new IPAddress(address.GetAddressBytes());
        return new IPEndPoint(address, endpoint.Port);
    }

    private static bool Overlaps(IPEndPoint left, IPEndPoint right)
    {
        if (left.Port != right.Port)
            return false;
        if (left.Address.Equals(right.Address))
            return true;
        if (left.Address.AddressFamily != right.Address.AddressFamily)
            return false;
        return IsWildcard(left.Address) || IsWildcard(right.Address);
    }

    private static bool IsWildcard(IPAddress address) =>
        address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);
}
