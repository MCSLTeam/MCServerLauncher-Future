using System.IO.Compression;
using System.Text;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Minecraft;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using RustyOptions.Async;

namespace MCServerLauncher.Daemon.Management.Factory;

/// <summary>
///     通用服务器实例的安装工厂
/// </summary>

[InstanceFactory(InstanceType.MCJava)]
[InstanceFactory(InstanceType.MCVanilla)]
[InstanceFactory(InstanceType.MCCraftBukkit)]
[InstanceFactory(InstanceType.MCSpigot)]
[InstanceFactory(InstanceType.MCPaper)]
[InstanceFactory(InstanceType.MCLeaf)]
[InstanceFactory(InstanceType.MCLeaves)]
[InstanceFactory(InstanceType.MCFolia)]
[InstanceFactory(InstanceType.MCPufferfish)]
[InstanceFactory(InstanceType.MCPurpur)]
[InstanceFactory(InstanceType.MCMohist)]
[InstanceFactory(InstanceType.MCBanner)]
[InstanceFactory(InstanceType.MCYouer)]
[InstanceFactory(InstanceType.MCThermos)]
[InstanceFactory(InstanceType.MCCrucible)]
[InstanceFactory(InstanceType.MCTaiyitist)]
[InstanceFactory(InstanceType.MCCatServer)]
[InstanceFactory(InstanceType.MCArclight)]
//make sure that the core file(*.jar) is downloaded from: https://fabricmc.net/use/server/
// the user shouldn't use any installer, but server jar. (MCFabric, MCLegacyFabric)
[InstanceFactory(InstanceType.MCFabric)]
//[InstanceFactory(InstanceType.MCLegacyFabric)]
[InstanceFactory(InstanceType.MCForge, SourceType.Archive, "1.5.2")]
[InstanceFactory(InstanceType.MCNeoForge, SourceType.Archive, "1.20.2")]
[InstanceFactory(InstanceType.MCCleanroom, SourceType.Archive, "1.12.2", "1.12.2")]
public class MCUniversalFactory : ICoreInstanceFactory, IArchiveInstanceFactory
{
    public async Task<Result<InstanceConfig, Error>> CreateInstanceFromArchive(InstanceFactorySetting setting)
    {
        var workingDirectory = setting.GetWorkingDirectory();

        var result = await ResultExt
            .Try(() => ZipFile.ExtractToDirectory(setting.Source, workingDirectory, Encoding.UTF8, true)
            ).MapAsync(_ => setting.FixEula());

        if (result.MapErr(Error.FromException).IsErr(out var error))
            return ResultExt.Err<InstanceConfig>("Universal factory could not create instance from archive", error);

        return ResultExt.Ok(setting.GetInstanceConfig());
    }

    public async Task<Result<InstanceConfig, Error>> CreateInstanceFromCore(InstanceFactorySetting setting)
    {
        setting = setting with { TargetType = TargetType.Jar };

        var copyAndRenameTarget = await setting.CopyAndRenameTarget();
        var fixEula = await copyAndRenameTarget.MapAsync(_ => setting.FixEula());

        return fixEula.Map(_ => setting.GetInstanceConfig());
    }

    public Func<MinecraftInstance, Task<Result<Unit, Error>>>[] GetPostProcessors()
    {
        return new List<Func<MinecraftInstance, Task<Result<Unit, Error>>>>
        {
            async instance =>
            {
                if (await instance.StartAsync())
                {
                    instance.Process!.WriteLine("stop");
                    await instance.Process!.WaitForExitAsync();
                }

                return ResultExt.Ok();
            }
        }.ToArray();
    }
}