using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Plugins;

namespace MCServerLauncher.Daemon.Plugins;

/// <summary>
/// Host facade for plugins that declare <c>system.query</c>.
/// </summary>
internal sealed class PluginSystemQueryApplication(ISystemApplication system) : ISystemQueryApplication
{
    public ISystemApplication System { get; } = system ?? throw new ArgumentNullException(nameof(system));
}
