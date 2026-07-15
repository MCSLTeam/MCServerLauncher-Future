using System.Text.Json;
using MCServerLauncher.Daemon.API.Errors;

namespace MCServerLauncher.Daemon.API.Plugins;

/// <summary>
/// A plugin-originated failure whose owner is authenticated by the host.
/// </summary>
public sealed class PluginError : DaemonError
{
    internal PluginError(
        PluginIdentity identity,
        string code,
        string message,
        JsonElement? details = null)
        : base(code, message, DaemonErrorKind.Internal, details)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
    }

    public PluginIdentity Identity { get; }
}

public interface IPluginErrorFactory
{
    PluginError Create(string code, string message, JsonElement? details = null);
}
