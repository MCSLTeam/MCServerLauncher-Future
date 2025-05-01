using System.IO.Compression;
using System.Text;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Extensions;
using MCServerLauncher.Daemon.Management.Minecraft;

namespace MCServerLauncher.Daemon.Management.Factory;

/// <summary>
///     原版服务器实例的安装工厂
/// </summary>
[InstanceFactory(InstanceType.Universal)]
// make sure that the core file(*.jar) is downloaded from: https://fabricmc.net/use/server/
[InstanceFactory(InstanceType.Fabric)]
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

    public Func<MinecraftInstance, Task>[] GetPostProcessors()
    {
        return new List<Func<MinecraftInstance, Task>>
        {
            async instance =>
            {
                if (await instance.StartAsync())
                {
                    instance.Process!.WriteLine("stop");
                    await instance.Process!.WaitForExitAsync();
                }
            }
        }.ToArray();
    }
}