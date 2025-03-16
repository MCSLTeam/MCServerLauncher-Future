using MCServerLauncher.Daemon.Storage;

namespace MCServerLauncher.Daemon.Minecraft.Server.Factory;

/// <summary>
///     原版服务器实例的安装工厂
/// </summary>
[InstanceFactory(InstanceType.Vanilla)]
[InstanceFactory(InstanceType.Spigot)]
public class UniversalFactory : ICoreInstanceFactory
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

    public Func<Instance, Task>[] GetPostProcessors()
    {
        return new List<Func<Instance, Task>>
        {
            async instance =>
            {
                instance.Start();
                instance.WriteLine("stop");
                await instance.WaitForExitAsync();
            }
        }.ToArray();
    }
}