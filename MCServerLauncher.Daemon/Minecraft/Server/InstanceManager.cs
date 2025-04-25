using System.Collections.Concurrent;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Minecraft.Extensions;
using MCServerLauncher.Daemon.Storage;
using Serilog;

namespace MCServerLauncher.Daemon.Minecraft.Server;

public class InstanceManager : IInstanceManager
{
    public ConcurrentDictionary<Guid, Instance> Instances { get; } = new();
    public ConcurrentDictionary<Guid, Instance> RunningInstances { get; } = new();

    // TODO 改用异常抛出异常信息
    public async Task<InstanceConfig?> TryAddInstance(InstanceFactorySetting setting)
    {
        if (Instances.ContainsKey(setting.Uuid))
            Log.Error("[InstanceManager] Add new instance failed: Instance '{0}' already exists.", setting.Uuid);

        var instanceRoot = setting.GetWorkingDirectory();
        // validate dir name
        try
        {
            Directory.CreateDirectory(instanceRoot);
        }
        catch (Exception e)
        {
            Log.Error("[InstanceManager] Failed to create instance directory '{0}': {1}", instanceRoot, e);
            FileManager.TryRemove(instanceRoot); // rollback
            return null;
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
                return null; // we need not to rollback here, because the new instance has been correctly installed
            }

            return config;
        }
        catch (Exception e)
        {
            Log.Error("[InstanceManager] Failed to create instance '{0}': \n{1}", setting.Name, e);
            FileManager.TryRemove(instanceRoot); // rollback
            return null;
        }
    }

    // TODO 当用户尝试移除实例时，不应删除正在运行的实例
    public bool TryRemoveInstance(Guid instanceId)
    {
        if (RunningInstances.ContainsKey(instanceId)) return false;

        if (!Instances.TryRemove(instanceId, out var instance)) return false;
        instance.Dispose();

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

    public async Task<Instance?> TryStartInstance(Guid instanceId)
    {
        if (RunningInstances.ContainsKey(instanceId)) return null;

        var target = Instances.GetValueOrDefault(instanceId);
        if (target is null) return null;

        try
        {
            target.OnStatusChanged -= OnInstanceStatusChangedHandler;
            target.OnStatusChanged += OnInstanceStatusChangedHandler;
            if (await target.StartAsync())

            {
                if (!RunningInstances.TryAdd(instanceId, target))
                {
                    Log.Warning("[InstanceManager] Cannot start a already running instance(Uuid={0})",
                        target.Config.Uuid);
                    return null;
                }

                return target;
            }

            target.OnStatusChanged -= OnInstanceStatusChangedHandler;
            return null;
        }
        catch (Exception e)
        {
            target.KillProcess();
            Log.Error("[InstanceManager] Error occurred when starting instance '{0}': {1}", target.Config.Name, e);
            return null;
        }
    }

    public bool TryStopInstance(Guid instanceId)
    {
        if (!RunningInstances.TryRemove(instanceId, out var instance)) return false;
        instance.WriteLine("stop");
        // 不等待服务器退出
        return true;
    }

    public bool SendToInstance(Guid instanceId, string message)
    {
        if (!RunningInstances.TryGetValue(instanceId, out var instance)) return false;
        instance.WriteLine(message);
        return true;
    }

    public void KillInstance(Guid instanceId)
    {
        if (RunningInstances.TryGetValue(instanceId, out var instance)) instance.KillProcess();
    }

    public Task<InstanceReport> GetInstanceReport(Guid instanceId)
    {
        if (!Instances.TryGetValue(instanceId, out var instance))
            throw new ArgumentException("Instance not found.");
        return instance.GetReportAsync();
    }

    public async Task<Dictionary<Guid, InstanceReport>> GetAllReports()
    {
        var tasks = Instances.ToDictionary(kv => kv.Key, kv => kv.Value.GetReportAsync());
        await Task.WhenAll(tasks.Values);
        return tasks.ToDictionary(kv => kv.Key, kv => kv.Value.Result);
    }

    public Task StopAllInstances(CancellationToken ct = default)
    {
        foreach (var instance in RunningInstances.Values) instance.WriteLine("stop");

        var tasks = RunningInstances.Values.Select(instance => instance.WaitForExitAsync(ct));
        return Task.WhenAll(tasks);
    }

    private void OnInstanceStatusChangedHandler(Guid instanceId, InstanceStatus status)
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