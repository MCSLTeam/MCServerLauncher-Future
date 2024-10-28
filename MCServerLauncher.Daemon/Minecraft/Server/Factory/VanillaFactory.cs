using MCServerLauncher.Daemon.Storage;

namespace MCServerLauncher.Daemon.Minecraft.Server.Factory;

/// <summary>
///     原版服务器实例的安装工厂
/// </summary>
public class VanillaFactory : ICoreInstanceFactory
{
    public async Task<InstanceConfig> CreateInstanceFromCore(InstanceFactorySetting setting)
    {
        setting.WorkingDirectory = Path.Combine(FileManager.InstancesRoot, setting.Name);
        Directory.CreateDirectory(setting.WorkingDirectory);

        await InstanceFactoryExtensions.CopyAndRenameTarget(setting);
        setting.TargetType = TargetType.Jar;
        await InstanceFactoryExtensions.FixEula(setting);

        return setting.GetInstanceConfig();
    }
}