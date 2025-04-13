using MCServerLauncher.Daemon.Storage;
using Serilog;

namespace MCServerLauncher.Daemon.Minecraft.Server;

public class InstanceFileSystemWatcher : IDisposable
{
    private readonly IInstanceManager _instanceManager;
    private readonly FileSystemWatcher _watcher;
    private bool _disposed;

    public InstanceFileSystemWatcher(IInstanceManager instanceManager)
    {
        _instanceManager = instanceManager;
        _watcher = new FileSystemWatcher
        {
            Path = FileManager.InstancesRoot,
            NotifyFilter = NotifyFilters.DirectoryName,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };

        _watcher.Deleted += OnInstanceDelete;
        _watcher.Error += OnError;
        Log.Debug("[InstancesWatchService] Start watching instances");
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void OnInstanceDelete(object sender, FileSystemEventArgs e)
    {
        if (Guid.TryParse(e.Name, out var id))
            if (_instanceManager.Instances.TryRemove(id, out var instance))
                Log.Information("[InstancesWatchService] Delete instance(Uuid={0}) from InstanceDictionary",
                    instance.Config.Uuid);
    }

    private static void OnError(object sender, ErrorEventArgs e)
    {
        Log.Error("[InstancesWatchService] Error occurred: {0}", e.GetException().Message);
    }

    ~InstanceFileSystemWatcher()
    {
        Dispose(false);
    }

    private void Dispose(bool dispose)
    {
        if (_disposed) return;
        if (dispose) _watcher.Dispose();

        _disposed = true;
    }
}