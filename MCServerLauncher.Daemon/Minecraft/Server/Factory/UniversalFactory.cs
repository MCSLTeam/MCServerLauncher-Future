using System.IO.Compression;
using System.Text;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Minecraft.Extensions;

namespace MCServerLauncher.Daemon.Minecraft.Server.Factory;

/// <summary>
///     原版服务器实例的安装工厂
/// </summary>
[InstanceFactory(InstanceType.Vanilla)]
[InstanceFactory(InstanceType.Spigot)]
[InstanceFactory(InstanceType
    .Fabric)] // make sure that the core file(*.jar) is downloaded from: https://fabricmc.net/use/server/
[InstanceFactory(InstanceType.Forge, SourceType.Archive, "1.5.2")]
[InstanceFactory(InstanceType.NeoForge, SourceType.Archive, "1.20.2")]
[InstanceFactory(InstanceType.Cleanroom, SourceType.Archive, "1.12.2", "1.12.2")]
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
                if (await instance.StartAsync())
                {
                    instance.WriteLine("stop");
                    await instance.WaitForExitAsync();
                }
            }
        }.ToArray();
    }
}