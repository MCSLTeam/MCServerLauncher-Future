using MCServerLauncher.Daemon.Management;
using Microsoft.Extensions.DependencyInjection;
using TouchSocket.Core;
using TouchSocket.Http;

namespace MCServerLauncher.Daemon.Remote;

public class FileSystemWatcherPlugin : PluginBase
{
    private readonly List<IDisposable> _watchers;

    public FileSystemWatcherPlugin(IHttpService httpService)
    {
        var resolver = httpService.Resolver;
        _watchers = new List<IDisposable>
        {
            new InstancesManagerFsWatcher(resolver.GetRequiredService<IInstanceManager>())
        };
    }

    protected override void Unloaded(IPluginManager pluginManager)
    {
        foreach (var watcher in _watchers) watcher.Dispose();

        _watchers.Clear();
        base.Unloaded(pluginManager);
    }
}