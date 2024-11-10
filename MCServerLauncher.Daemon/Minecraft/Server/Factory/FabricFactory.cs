using MCServerLauncher.Daemon.Storage;

namespace MCServerLauncher.Daemon.Minecraft.Server.Factory;

/// <summary>
///     Fabric 服务器实例的安装工厂
/// </summary>
public class FabricFactory : ICoreInstanceFactory
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
}