using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

namespace MCServerLauncher.Daemon.API.Plugins;

/// <summary>
/// Validates and registers plugin-owned HTTP bindings. Feature:
/// <c>network.http.listen</c>. Does not open the socket; the plugin still
/// hosts its own listener after a successful registration.
/// Bind is expressed as address + port so Daemon.API stays free of System.Net types.
/// </summary>
public interface IPluginHttpEndpointPolicy
{
    /// <summary>
    /// Validates the bind address/port against daemon policy (loopback default,
    /// port exclusivity vs daemon and other plugins) and records ownership.
    /// </summary>
    Result<Unit, DaemonError> ValidateAndRegister(string address, int port);

    /// <summary>
    /// Releases a previously registered endpoint for this plugin.
    /// </summary>
    void Release(string address, int port);
}
