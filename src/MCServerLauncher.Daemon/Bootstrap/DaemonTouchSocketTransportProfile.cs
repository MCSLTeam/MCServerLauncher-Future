using MCServerLauncher.Daemon;
using MCServerLauncher.Daemon.Remote.Action;
using Microsoft.Extensions.DependencyInjection;
using TouchSocket.Core;
using TouchSocket.Core.AspNetCore;
using TouchSocket.Http;
using TouchSocket.Sockets;

namespace MCServerLauncher.Daemon.Bootstrap;

internal static class DaemonTouchSocketTransportProfile
{
    internal static DaemonTouchSocketTransportConfiguration CreateConfig(
        IServiceCollection collection,
        HttpService httpService,
        ActionHandlerRegistrySnapshot selectedRegistry) =>
        CreateConfig(collection, httpService, selectedRegistry, new IPHost(AppConfig.Get().Port));

    internal static DaemonTouchSocketTransportConfiguration CreateConfig(
        IServiceCollection collection,
        HttpService httpService,
        ActionHandlerRegistrySnapshot selectedRegistry,
        IPHost listenHost)
    {
        ArgumentNullException.ThrowIfNull(listenHost);
        var legacyEventQueueControl = new LegacyEventQueueControl();
        var container = new AspNetCoreContainer(collection);
        var config = new TouchSocketConfig()
            .SetListenIPHosts(listenHost)
            .SetRegistrator(container)
            .ConfigureContainer(a => DaemonServiceComposition.ConfigureContainer(
                a,
                collection,
                httpService,
                selectedRegistry,
                legacyEventQueueControl))
            .ConfigurePlugins(a => DaemonServiceComposition.ConfigurePlugins(a, legacyEventQueueControl));
        return new DaemonTouchSocketTransportConfiguration(config, container);
    }
}

internal sealed record DaemonTouchSocketTransportConfiguration(
    TouchSocketConfig Config,
    AspNetCoreContainer Container);
