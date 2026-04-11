using MCServerLauncher.Daemon;
using MCServerLauncher.Daemon.Remote.Action;
using Microsoft.Extensions.DependencyInjection;
using TouchSocket.Core;
using TouchSocket.Http;

namespace MCServerLauncher.Daemon.Bootstrap;

internal static class DaemonTouchSocketTransportProfile
{
    internal static TouchSocketConfig CreateConfig(
        IServiceCollection collection,
        HttpService httpService,
        ActionHandlerRegistrySnapshot selectedRegistry)
    {
        return new TouchSocketConfig()
            .SetListenIPHosts(AppConfig.Get().Port)
            .UseAspNetCoreContainer(collection)
            .ConfigureContainer(a => DaemonServiceComposition.ConfigureContainer(a, collection, httpService, selectedRegistry))
            .ConfigurePlugins(a => DaemonServiceComposition.ConfigurePlugins(a));
    }
}
