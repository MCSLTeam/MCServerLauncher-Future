using System.Collections.Concurrent;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using RustyOptions.Async;
using Serilog;

namespace MCServerLauncher.Daemon.Management;

public class InstanceManager : IInstanceManager
{
    private Func<IEnumerable<Guid>> InstanceKeysSupplier => () => Instances.Keys;
    public ConcurrentDictionary<Guid, IInstance> Instances { get; } = new();
    public ConcurrentDictionary<Guid, IInstance> RunningInstances { get; } = new();

    public async Task<Result<InstanceConfig, Error>> TryAddInstance(InstanceFactorySetting setting)
    {
        if (Instances.ContainsKey(setting.Uuid))
            Log.Warning(
                "[InstanceManager] Add new instance failed: Instance '{0}' already exists, we will allocate a new uuid for it",
                setting.Uuid);

        var instanceRoot = setting.GetWorkingDirectory();

        var createDirResult = ResultExt.Try(Directory.CreateDirectory, instanceRoot).MapErr(ex =>
            new Error("Instance manager failed to create instance directory").CauseBy(ex));

        Log.Information(
            "[InstanceManager] Running InstanceFactory({0}) for instance '{1}'",
            setting.InstanceType.ToString(),
            setting.Name
        );

        var appliedFactoryResult = (await createDirResult.MapAsync(_ => setting.ApplyInstanceFactory()))
            .Flatten()
            .MapErr(err => new Error("Instance manager failed to run instance factory").WithInner(err));

        var saveInstanceResult = appliedFactoryResult.Map((state, config) =>
            {
                var (root, keysSupplier) = state;
                config.AllocateNewUuid(keysSupplier);

                return ResultExt.Try(innerState =>
                {
                    var (innerRoot, innerConfig) = innerState;
                    FileManager.WriteJsonAndBackup(
                        Path.Combine(innerRoot, InstanceConfig.FileName),
                        innerConfig
                    );
                    return innerConfig;
                }, (root, config)).MapErr(Error.FromException);
            }
            , (instanceRoot, KeysSupplier: InstanceKeysSupplier)).Flatten();

        if (saveInstanceResult.IsErr(out var error))
        {
            Log.Error("[InstanceManager] Failed to create instance '{0}': \n{1}", setting.Name, error);
            FileManager.TryRemove(instanceRoot); // rollback
        }

        return saveInstanceResult;
    }

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

    public async Task<IInstance?> TryStartInstance(Guid instanceId)
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
            target.Process?.KillProcess();
            Log.Error("[InstanceManager] Error occurred when starting instance '{0}': {1}", target.Config.Name, e);
            return null;
        }
    }

    public bool TryStopInstance(Guid instanceId)
    {
        if (!RunningInstances.TryRemove(instanceId, out var instance)) return false;
        instance.Stop();
        // 不等待服务器退出
        return true;
    }

    public bool SendToInstance(Guid instanceId, string message)
    {
        if (!RunningInstances.TryGetValue(instanceId, out var instance)) return false;
        instance.Process!.WriteLine(message);
        return true;
    }

    public void KillInstance(Guid instanceId)
    {
        if (RunningInstances.TryGetValue(instanceId, out var instance)) instance.Process!.KillProcess();
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
        foreach (var instance in RunningInstances.Values) instance.Process?.WriteLine("stop");

        var tasks = RunningInstances.Values.Select(instance =>
            instance.Process?.WaitForExitAsync(ct) ?? Task.CompletedTask);
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
                instanceManager.Instances.TryAdd(config!.Uuid, config.CreateInstance());
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