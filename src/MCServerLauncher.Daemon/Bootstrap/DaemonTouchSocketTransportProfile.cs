using MCServerLauncher.Daemon;
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
        HttpService httpService) =>
        CreateConfig(collection, httpService, new IPHost(AppConfig.Get().Port));

    internal static DaemonTouchSocketTransportConfiguration CreateConfig(
        IServiceCollection collection,
        HttpService httpService,
        IPHost listenHost)
    {
        ArgumentNullException.ThrowIfNull(listenHost);
        var container = new AspNetCoreContainer(collection);
        var config = new TouchSocketConfig()
            .SetListenIPHosts(listenHost)
            .SetRegistrator(container)
            .ConfigureContainer(a => DaemonServiceComposition.ConfigureContainer(
                a,
                collection,
                httpService))
            .ConfigurePlugins(DaemonServiceComposition.ConfigurePlugins);
        return new DaemonTouchSocketTransportConfiguration(config, container);
    }
}

internal sealed record DaemonTouchSocketTransportConfiguration(
    TouchSocketConfig Config,
    AspNetCoreContainer Container);
