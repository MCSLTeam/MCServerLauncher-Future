using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Common.Minecraft;
using MCServerLauncher.Daemon.Management.Installer;
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
[InstanceFactory(InstanceType.MCCanvas)]
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
[InstanceFactory(InstanceType.MCSpongeVanilla)]
[InstanceFactory(InstanceType.MCSpongeForge)]
[InstanceFactory(InstanceType.MCSpongeNeo)]
public class MCUniversalFactory : ICoreInstanceFactory, IArchiveInstanceFactory
{
    public async Task<Result<InstanceConfiguration, Error>> CreateInstanceFromArchive(InstanceFactoryConfiguration setting)
    {
        var workingDirectory = setting.Configuration.GetWorkingDirectory();

        var result = await ResultExt
            .Try(() => ZipFile.ExtractToDirectory(setting.Source, workingDirectory, Encoding.UTF8, true)
            ).MapAsync(_ => setting.FixEula());

        if (result.MapErr(Error.FromException).IsErr(out var error))
            return ResultExt.Err<InstanceConfiguration>("Universal factory could not create instance from archive", error);

        return ResultExt.Ok(setting.Configuration);
    }

    public async Task<Result<InstanceConfiguration, Error>> CreateInstanceFromCore(InstanceFactoryConfiguration setting)
    {
        setting = setting with
        {
            Configuration = InstanceConfigurationMapper.WithTarget(
                setting.Configuration,
                setting.Configuration.Target,
                TargetType.Jar)
        };

        var copyAndRenameTarget = await setting.CopyAndRenameTarget();
        var installer = InstanceInstallerResolver.Resolve(
            setting,
            Path.Combine(setting.Configuration.GetWorkingDirectory(), setting.Configuration.Target));
        var install = await copyAndRenameTarget.MapAsync(_ => installer.Run(setting));
        var fixEula = await install.MapAsync(_ => setting.FixEula());

        return fixEula.Map(_ =>
        {
            var config = setting.Configuration;
            InstanceInstallMetadataStore.Write(
                config.GetWorkingDirectory(),
                new InstanceInstallMetadata(
                    config.InstanceType.ToString(),
                    setting.Source,
                    ImmutableArray<string>.Empty,
                    config.Target,
                    DateTimeOffset.UtcNow));
            return config;
        });
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
