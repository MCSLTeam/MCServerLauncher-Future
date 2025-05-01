using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using Serilog;

namespace MCServerLauncher.Daemon.Management;

public class InstancesManagerFsWatcher : DisposableObject
{
    private readonly IInstanceManager _instanceManager;
    private readonly FileSystemWatcher _watcher;

    public InstancesManagerFsWatcher(IInstanceManager instanceManager)
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


    private void OnInstanceDelete(object sender, FileSystemEventArgs e)
    {
        if (Guid.TryParse(e.Name, out var id)) _instanceManager.TryRemoveInstance(id);
    }

    private static void OnError(object sender, ErrorEventArgs e)
    {
        Log.Error("[InstancesWatchService] Error occurred: {0}", e.GetException().Message);
    }

    protected override void ProtectedDispose()
    {
        _watcher.Dispose();
    }
}