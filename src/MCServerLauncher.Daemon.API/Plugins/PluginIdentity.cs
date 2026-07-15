using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.Daemon.API.Plugins;

/// <summary>
/// Stable host-assigned identity for a startup plugin.
/// </summary>
public sealed record PluginIdentity
{
    internal PluginIdentity(string id, string version)
    {
        Id = ProtocolIdentifier.ValidatePluginId(id, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(version, nameof(version));
        Version = version;
    }

    public string Id { get; }

    public string Version { get; }
}
