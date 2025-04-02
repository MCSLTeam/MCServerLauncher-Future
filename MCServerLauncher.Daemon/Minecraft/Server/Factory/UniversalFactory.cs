using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.Daemon.Minecraft.Server.Factory;

/// <summary>
///     原版服务器实例的安装工厂
/// </summary>
[InstanceFactory(InstanceType.Vanilla)]
[InstanceFactory(InstanceType.Spigot)]
[InstanceFactory(InstanceType.Fabric, SourceType.Archive)]
[InstanceFactory(InstanceType.Forge, SourceType.Archive)]
public class UniversalFactory : ICoreInstanceFactory, IArchiveInstanceFactory
{
    public Task<InstanceConfig> CreateInstanceFromArchive(InstanceFactorySetting setting)
    {
        throw new NotImplementedException();
    }

    public async Task<InstanceConfig> CreateInstanceFromCore(InstanceFactorySetting setting)
    {
        var workingDirectory = setting.GetWorkingDirectory();
        Directory.CreateDirectory(workingDirectory);

        await setting.CopyAndRenameTarget();
        setting = setting with { TargetType = TargetType.Jar };
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