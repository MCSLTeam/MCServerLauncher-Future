using System.Collections.Concurrent;
using MCServerLauncher.Daemon.Minecraft.Server.Factory;
using MCServerLauncher.Daemon.Storage;
using Serilog;

namespace MCServerLauncher.Daemon.Minecraft.Server;

public class InstanceManager : IInstanceManager
{
    private ConcurrentDictionary<string, InstanceConfig> Instances { get; } = new();
    private ConcurrentDictionary<string, Instance> RunningInstances { get; } = new();

    public async Task<bool> TryAddInstance(InstanceFactorySetting setting,
        IInstanceFactory serverFactory)
    {
        var instanceRoot = Path.Combine(FileManager.InstancesRoot, setting.Name);
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
        try
        {
            var config = await serverFactory.CreateInstance(setting);
            FileManager.WriteJsonAndBackup(Path.Combine(instanceRoot, InstanceConfig.FileName), config);
            if (!Instances.TryAdd(config.Name, config))
            {
                Log.Error("[InstanceManager] Failed to add instance '{0}' to manager.", config.Name);
                return false; // we need not to rollback here, because the new instance has been correctly installed
            }

            return true;
        }
        catch (Exception e)
        {
            Log.Error("[InstanceManager] Failed to create instance '{0}': \n{1}", setting.Name, e);
            FileManager.TryRemove(instanceRoot); // rollback
            return false;
        }
    }

    public async Task<bool> TryRemoveInstance(string instanceName)
    {
        if (!Instances.TryRemove(instanceName, out var config)) return false;
        if (RunningInstances.ContainsKey(instanceName))
        {
            if (!RunningInstances.TryRemove(instanceName, out var instance)) return false;
            instance.ServerProcess?.Kill();

            // wait for process exit
            await (instance.ServerProcess?.WaitForExitAsync() ?? Task.CompletedTask);
        }

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

    public bool TryStartInstance(string instanceName, out Instance? instance)
    {
        instance = default;
        if (RunningInstances.ContainsKey(instanceName)) return false;

        var config = Instances.GetValueOrDefault(instanceName);
        if (config == null) return false;

        var target = new Instance(config);

        try
        {
            target.Start();
            if (RunningInstances.TryAdd(instanceName, target))
            {
                instance = target;
                return true;
            }

            target.ServerProcess?.Kill();
            return false;
        }
        catch (Exception e)
        {
            target.ServerProcess?.Kill();
            Log.Error("[InstanceManager] Error occurred when starting instance '{0}': {1}", config.Name, e);
            return false;
        }
    }

    public bool TryStopInstance(string instanceName)
    {
        if (!RunningInstances.TryRemove(instanceName, out var instance)) return false;
        instance.ServerProcess?.StandardInput.WriteLine("stop");
        // 不等待服务器退出
        return true;
    }

    public void SendToInstance(string instanceName, string message)
    {
        if (!RunningInstances.TryGetValue(instanceName, out var instance))
            throw new ArgumentException("Instance not found.");
        instance.ServerProcess?.StandardInput.WriteLine(message);
    }

    public void KillInstance(string instanceName)
    {
        if (!RunningInstances.TryRemove(instanceName, out var instance)) return;
        instance.ServerProcess?.Kill();
    }

    public InstanceStatus GetInstanceStatus(string instanceName)
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
            var serverConfig = dir.GetFiles(InstanceConfig.FileName, SearchOption.TopDirectoryOnly).FirstOrDefault();

            if (serverConfig == null) continue;

            try
            {
                var config = FileManager.ReadJson<InstanceConfig>(serverConfig.FullName);
                instanceManager.Instances.TryAdd(config!.Name, config);
            }
            catch (Exception e)
            {
                Log.Error(
                    "[InstanceManager] Failed to load instance at '{0}', ignored: {1}",
                    Path.Combine(FileManager.InstancesRoot, directory),
                    e
                );
            }
        }

        return instanceManager;
    }
}