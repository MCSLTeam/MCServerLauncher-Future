using MCServerLauncher.Daemon.API.Application;

namespace MCServerLauncher.Daemon.API.Plugins;

/// <summary>
/// Narrow system query surface for plugins that declare <c>system.query</c>.
/// </summary>
public interface ISystemQueryApplication
{
    ISystemApplication System { get; }
}
