using MCServerLauncher.Daemon.Storage;

namespace MCServerLauncher.Daemon.Minecraft.Server.Factory;

/// <summary>
///     原版服务器实例的安装工厂
/// </summary>
public class UniversalFactory : ICoreInstanceFactory, IInstancePostProcessor
{
    public async Task<InstanceConfig> CreateInstanceFromCore(InstanceFactorySetting setting)
    {
        setting.WorkingDirectory = Path.Combine(FileManager.InstancesRoot, setting.Uuid.ToString());
        Directory.CreateDirectory(setting.WorkingDirectory);

        await setting.CopyAndRenameTarget();
        setting.TargetType = TargetType.Jar;
        await setting.FixEula();

        return setting.GetInstanceConfig();
    }

    public Task PostProcess(Instance instance)
    {
        instance.Start();
        instance.WriteLine("stop");
        return instance.WaitForExitAsync();
    }
}