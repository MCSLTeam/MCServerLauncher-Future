using System.Collections.Concurrent;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Minecraft.Server.Factory;
using MCServerLauncher.Daemon.Storage;
using Serilog;

namespace MCServerLauncher.Daemon.Minecraft.Server;

public class InstanceManager : IInstanceManager
{
    private ConcurrentDictionary<Guid, Instance> Instances { get; } = new();
    private ConcurrentDictionary<Guid, Instance> RunningInstances { get; } = new();

    // TODO 改用异常抛出异常信息
    public async Task<bool> TryAddInstance(InstanceFactorySetting setting)
    {
        if (Instances.ContainsKey(setting.Uuid))
            Log.Error("[InstanceManager] Add new instance failed: Instance '{0}' already exists.", setting.Uuid);

        var instanceRoot = Path.Combine(FileManager.InstancesRoot, setting.Uuid.ToString());
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

        // var instanceFactory = setting.GetInstanceFactory();

        // async run
        try
        {
            Log.Information("[InstanceManager] Running InstanceFactory({0}) for instance '{1}'",
                setting.InstanceType.ToString(), setting.Name);
            var config = await setting.ApplyInstanceFactory();
            FileManager.WriteJsonAndBackup(Path.Combine(instanceRoot, InstanceConfig.FileName),
                config);

            var instance = new Instance(config);
            // TODO 重写apply post processor逻辑，maybe继续用attribute?
            // if (setting.UsePostProcess)
            // {
            //     var processors = instanceFactory.GetPostProcessors();
            //     for (var i = 0; i < processors.Length; i++)
            //     {
            //         Log.Information("[InstanceManager] Running PostProcessor({0}/{1}) for instance '{2}'", i,
            //             processors.Length, setting.Name);
            //         await processors[i].Invoke(instance);
            //     }
            // }

            if (!Instances.TryAdd(config.Uuid, instance))
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

    // TODO 当用户尝试移除实例时，不应删除正在运行的实例
    public async Task<bool> TryRemoveInstance(Guid instanceId)
    {
        if (!Instances.TryRemove(instanceId, out var config)) return false;
        if (RunningInstances.ContainsKey(instanceId))
        {
            if (!RunningInstances.TryRemove(instanceId, out var instance)) return false;
            await instance.KillProcess();
        }

        // remove server directory
        try
        {
            Directory.Delete(Path.Combine(FileManager.InstancesRoot, instanceId.ToString()), true);
            Log.Information("[InstanceManager] Removed instance '{0}'", instanceId);
            return true;
        }
        catch (Exception exception)
        {
            Log.Error("[InstanceManager] Failed to remove instance '{0}': {1}", instanceId, exception);
            return false;
        }
    }

    public bool TryStartInstance(Guid instanceId, out Instance? instance)
    {
        instance = null;
        if (RunningInstances.ContainsKey(instanceId)) return false;

        var target = Instances.GetValueOrDefault(instanceId);
        if (target == null) return false;

        try
        {
            if (RunningInstances.TryAdd(instanceId, target))
            {
                instance = target;
                instance.OnStatusChanged -= OnInstanceStatusChangedHandler;
                instance.OnStatusChanged += OnInstanceStatusChangedHandler;
                target.Start();
                return true;
            }

            return false;
        }
        catch (Exception e)
        {
            target.KillProcess().ConfigureAwait(false).GetAwaiter().GetResult();
            Log.Error("[InstanceManager] Error occurred when starting instance '{0}': {1}", target.Config.Name, e);
            return false;
        }
    }

    public bool TryStopInstance(Guid instanceId)
    {
        if (!RunningInstances.TryRemove(instanceId, out var instance)) return false;
        instance.WriteLine("stop");
        // 不等待服务器退出
        return true;
    }

    public void SendToInstance(Guid instanceId, string message)
    {
        if (!RunningInstances.TryGetValue(instanceId, out var instance))
            throw new ArgumentException("Instance not found.");
        instance.WriteLine(message);
    }

    public Task KillInstance(Guid instanceId)
    {
        return RunningInstances.TryGetValue(instanceId, out var instance) ? instance.KillProcess() : Task.CompletedTask;
    }

    public Task<InstanceStatus> GetInstanceStatus(Guid instanceId)
    {
        if (!Instances.TryGetValue(instanceId, out var instance))
            throw new ArgumentException("Instance not found.");
        return instance.GetStatusAsync();
    }

    public async Task<Dictionary<Guid, InstanceStatus>> GetAllStatus()
    {
        var tasks = Instances.ToDictionary(kv => kv.Key, kv => kv.Value.GetStatusAsync());
        await Task.WhenAll(tasks.Values);
        return tasks.ToDictionary(kv => kv.Key, kv => kv.Value.Result);
    }

    public Task StopAllInstances(CancellationToken ct = default)
    {
        foreach (var instance in RunningInstances.Values)
        {
            instance.WriteLine("stop");
        }

        var tasks = RunningInstances.Values.Select(instance => instance.WaitForExitAsync(ct));
        return Task.WhenAll(tasks);
    }

    private void OnInstanceStatusChangedHandler(Guid instanceId,ServerStatus status)
    {
        Log.Debug("[InstanceManager] Instance '{0}' status changed to {1}", instanceId,
            status.ToString());
        if (status.IsStoppedOrCrashed()) RunningInstances.TryRemove(instanceId, out _);
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
                instanceManager.Instances.TryAdd(config!.Uuid, new Instance(config));
                Log.Debug("[InstanceManager] Loaded instance '{0}'({1})", config.Name, config.Uuid);
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

        Log.Debug("[InstanceManager] Loaded {0} instances.", instanceManager.Instances.Count);

        return instanceManager;
    }
}