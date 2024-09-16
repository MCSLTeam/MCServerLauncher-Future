using System.Collections.Concurrent;
using System.Diagnostics;
using MCServerLauncher.Daemon.Storage;
using Serilog;

namespace MCServerLauncher.Daemon.Minecraft.Server;

public class InstanceManager : IInstanceManager
{
    public ConcurrentDictionary<string, ServerConfig> Instances { get; } = new();
    public ConcurrentDictionary<string, ServerInstance> RunningInstances { get; } = new();

    public async Task<bool> TryAddServer(string instanceName, ServerConfig config, Action<ServerConfig> serverFactory)
    {
        var instanceRoot = Path.Combine(FileManager.InstancesRoot, instanceName);
        // validate dir name
        try
        {
            Directory.CreateDirectory(instanceRoot);
        }
        catch (Exception e)
        {
            Log.Error("[InstanceManager] Failed to create instance directory '{0}': {1}", instanceRoot, e);
            FileManager.TryRemove(instanceRoot); // rollback
            return false;
        }


        // async run
        var configTask = Task.Run(() => serverFactory.Invoke(config));
        await configTask;
        if (configTask.IsFaulted)
        {
            Log.Error("[InstanceManager] Failed to create server config: {0}", configTask.Exception?.InnerExceptions);
            FileManager.TryRemove(instanceRoot); // rollback
            return false;
        }

        try
        {
            FileManager.WriteJsonAndBackup(Path.Combine(instanceRoot, ServerConfig.FileName), config);
        }
        catch (Exception e)
        {
            Log.Error("[InstanceManager] Failed to write server config: {0}", e);
            FileManager.TryRemove(instanceRoot); // rollback
            return false;
        }

        return true;
    }

    public async Task<bool> TryRemoveServer(string instanceName)
    {
        if (!Instances.TryRemove(instanceName, out var config)) return false;
        if (!RunningInstances.TryRemove(instanceName, out var instance)) return false;

        instance.ServerProcess?.Kill();

        // wait for process exit
        await (instance.ServerProcess?.WaitForExitAsync() ?? Task.CompletedTask);

        // remove server directory
        try
        {
            Directory.Delete(Path.Combine(FileManager.InstancesRoot, instanceName), true);
            Log.Information("[InstanceManager] Removed instance '{0}'", instanceName);
            return true;
        }
        catch (Exception exception)
        {
            Log.Error("[InstanceManager] Failed to remove instance '{0}': {1}", instanceName, exception);
            return false;
        }
    }

    public bool TryStartServer(string instanceName, out Process? process)
    {
        process = null;
        if (RunningInstances.ContainsKey(instanceName)) return false;

        var config = Instances.GetValueOrDefault(instanceName);
        if (config == null) return false;

        var instance = new ServerInstance(config);

        try
        {
            instance.Start();
            if (RunningInstances.TryAdd(instanceName, instance))
            {
                process = instance.ServerProcess;
                return true;
            }

            instance.ServerProcess?.Kill();
            return false;
        }
        catch (Exception e)
        {
            instance.ServerProcess?.Kill();
            Log.Error("[InstanceManager] Error occurred when starting instance '{0}': {1}", config.Name, e);
            return false;
        }
    }

    public bool TryStopServer(string instanceName)
    {
        if (!RunningInstances.TryRemove(instanceName, out var instance)) return false;
        instance.ServerProcess?.StandardInput.WriteLine("stop");
        // 不等待服务器退出
        return true;
    }

    public void SendToServer(string instanceName, string message)
    {
        if (!RunningInstances.TryGetValue(instanceName, out var instance))
            throw new ArgumentException("Instance not found.");
        instance.ServerProcess?.StandardInput.WriteLine(message);
    }

    public void KillServer(string instanceName)
    {
        if (!RunningInstances.TryRemove(instanceName, out var instance)) return;
        instance.ServerProcess?.Kill();
    }

    public InstanceStatus GetServerStatus(string instanceName)
    {
        if (!RunningInstances.TryGetValue(instanceName, out var instance))
            throw new ArgumentException("Instance not found.");
        return instance.GetStatus();
    }

    public IDictionary<string, InstanceStatus> GetAllStatus()
    {
        return RunningInstances.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.GetStatus());
    }

    public static IInstanceManager Create()
    {
        var instanceManager = new InstanceManager();

        // load all instances
        foreach (var directory in Directory.GetDirectories(FileManager.InstancesRoot, "*",
                     SearchOption.TopDirectoryOnly))
        {
            var dir = new DirectoryInfo(directory);
            var serverConfig = dir.GetFiles(ServerConfig.FileName, SearchOption.TopDirectoryOnly).FirstOrDefault();

            if (serverConfig == null) continue;

            try
            {
                var config = FileManager.ReadJson<ServerConfig>(serverConfig.FullName);
                instanceManager.Instances.TryAdd(config.Name, config);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        return instanceManager;
    }
}