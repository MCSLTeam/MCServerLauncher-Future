using System.Net;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.Plugins.Configuration;
using RustyOptions;

namespace MCServerLauncher.Daemon.Plugins;

/// <summary>
/// Validates and registers plugin HTTP bindings. Loopback is the default policy:
/// non-loopback and wildcard binds are rejected in Preview-1. Port exclusivity is
/// enforced against the daemon and other plugins.
/// </summary>
internal sealed class PluginHttpEndpointPolicy : IPluginHttpEndpointPolicy
{
    private readonly string _pluginId;
    private readonly PluginHttpEndpointRegistry _registry;
    private readonly PluginErrorFactory _errors;
    private readonly object _gate = new();
    private readonly HashSet<IPEndPoint> _ownedEndpoints = [];

    internal PluginHttpEndpointPolicy(
        string pluginId,
        PluginHttpEndpointRegistry registry,
        PluginErrorFactory errors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        _pluginId = pluginId;
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
    }

    public Result<Unit, DaemonError> ValidateAndRegister(string address, int port)
    {
        if (!TryParseEndpoint(address, port, out var endpoint, out var parseError))
            return Result.Err<Unit, DaemonError>(parseError!);

        if (!IPAddress.IsLoopback(endpoint.Address))
        {
            return Result.Err<Unit, DaemonError>(_errors.Create(
                "plugin_http_bind_not_loopback",
                "Plugin HTTP listeners must bind loopback in Preview-1. Public exposure requires an external reverse proxy."));
        }

        lock (_gate)
        {
            if (!_registry.TryRegister(_pluginId, endpoint, out var conflictOwner))
            {
                return Result.Err<Unit, DaemonError>(_errors.Create(
                    "plugin_http_port_conflict",
                    $"HTTP endpoint {endpoint} is already owned by '{conflictOwner}'."));
            }

            _ownedEndpoints.Add(endpoint);
        }
        return Result.Ok<Unit, DaemonError>(Unit.Default);
    }

    public void Release(string address, int port)
    {
        if (!TryParseEndpoint(address, port, out var endpoint, out _))
            return;
        lock (_gate)
        {
            if (_ownedEndpoints.Remove(endpoint))
                _registry.Release(_pluginId, endpoint);
        }
    }

    internal void ReleaseAll()
    {
        lock (_gate)
        {
            if (_ownedEndpoints.Count == 0)
                return;
            _ownedEndpoints.Clear();
            _registry.ReleaseAll(_pluginId);
        }
    }

    private bool TryParseEndpoint(string address, int port, out IPEndPoint endpoint, out DaemonError? error)
    {
        endpoint = null!;
        error = null;
        if (string.IsNullOrWhiteSpace(address) || !IPAddress.TryParse(address, out var ip))
        {
            error = _errors.Create("plugin_http_address_invalid", "Plugin HTTP listener address is invalid.");
            return false;
        }

        if (port is <= 0 or > 65535)
        {
            error = _errors.Create("plugin_http_port_invalid", "Plugin HTTP listener port is out of range.");
            return false;
        }

        endpoint = PluginHttpEndpointRegistry.Normalize(new IPEndPoint(ip, port));
        return true;
    }
}
