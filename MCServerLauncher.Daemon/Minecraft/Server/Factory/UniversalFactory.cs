using System.IO.Compression;
using System.Text;
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
    public async Task<InstanceConfig> CreateInstanceFromArchive(InstanceFactorySetting setting)
    {
        var workingDirectory = setting.GetWorkingDirectory();
        ZipFile.ExtractToDirectory(setting.Source, workingDirectory, Encoding.UTF8, true);
        await setting.FixEula();

        return setting.GetInstanceConfig();
    }

    public async Task<InstanceConfig> CreateInstanceFromCore(InstanceFactorySetting setting)
    {
        if (!await setting.CopyAndRenameTarget())
            throw new InstanceFactoryException(setting, "Failed to download target source");
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