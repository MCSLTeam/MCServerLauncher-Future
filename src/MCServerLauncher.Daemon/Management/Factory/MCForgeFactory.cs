using System.Diagnostics.CodeAnalysis;
using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Installer;
using MCServerLauncher.Daemon.Management.Installer.MinecraftForge;
using MCServerLauncher.Common.Minecraft;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;

namespace MCServerLauncher.Daemon.Management.Factory;

[InstanceFactory(InstanceType.MCForge, minVersion: "1.5.2")]
[InstanceFactory(InstanceType.MCNeoForge, minVersion: "1.20.2")]
[InstanceFactory(InstanceType.MCCleanroom, minVersion: "1.12.2", maxVersion: "1.12.2")]
public class MCForgeFactory : ICoreInstanceFactory
{
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Forge installer selection intentionally enters localized trim-incompatible installer profile parsing boundaries for third-party Forge metadata.")]
    public async Task<Result<InstanceConfiguration, Error>> CreateInstanceFromCore(InstanceFactoryConfiguration setting)
    {
        var config = setting.Configuration;
        var copyAndRenameTarget = await setting.CopyAndRenameTarget();
        if (copyAndRenameTarget.IsErr(out var error))
            return ResultExt.Err<InstanceConfiguration>("Forge factory could not create instance from core", error);

        var installerPath = Path.Combine(config.GetWorkingDirectory(), config.Target);

        var mcVersion = McVersion.Of(config.Version); // 可以直接转换因为已经检查过了

        var installer = InstanceInstallerResolver.Resolve(setting, installerPath);
        if (installer is not ForgeInstallerBase forgeInstaller)
            return ResultExt.Err<InstanceConfiguration>(
                $"Forge factory failed to resolve forge installer for {config.InstanceType} {config.Version}");

        var result = await forgeInstaller.Run(setting);
        if (result.IsErr(out error))
            return ResultExt.Err<InstanceConfiguration>(
                $"Forge factory failed to run forge installer({forgeInstaller.GetType().Name})", error);

        var fixEula = await setting.FixEula();
        if (fixEula.IsErr(out error))
            return ResultExt.Err<InstanceConfiguration>("Forge factory failed to overwrite eula.txt", error);

        // 处理启动参数
        if (mcVersion.Between(McVersion.Of("1.17"), McVersion.Max))
        {
            var extractResult = await ResultExt.TryAsync(async Task () =>
            {
                var serverLauncher = await ContainedFiles.EnsureContained(ContainedFiles.NeoForgeServerLauncher);
                File.Copy(
                    serverLauncher,
                    Path.Combine(config.GetWorkingDirectory(), ContainedFiles.NeoForgeServerLauncher),
                    true
                );
            });
            return extractResult.MapErr(Error.FromException).Map(_ =>
            {
                var updatedConfig = InstanceConfigurationMapper.WithTarget(
                    config,
                    ContainedFiles.NeoForgeServerLauncher,
                    TargetType.Jar);
                InstanceInstallMetadataStore.Write(
                    config.GetWorkingDirectory(),
                    new InstanceInstallMetadata(
                        config.InstanceType.ToString(),
                        installerPath,
                        ImmutableArray.Create("libraries", "server.jar"),
                        updatedConfig.Target,
                        DateTimeOffset.UtcNow));
                return updatedConfig;
            });
        }

        // 确定最低支持版本(1.5.2)：再低就没Forge Installer了！
        if (mcVersion.Between(McVersion.Of("1.5.2"), McVersion.Of("1.16.5")))
        {
            var profile = forgeInstaller.Install; // 不为空,应为已经安装过了且无问题
            var updatedConfig = InstanceConfigurationMapper.WithTarget(
                config,
                forgeInstaller is ForgeInstallerV2 ? profile.Path!.Filename : profile.FilePath!,
                TargetType.Jar);
            InstanceInstallMetadataStore.Write(
                config.GetWorkingDirectory(),
                new InstanceInstallMetadata(
                    config.InstanceType.ToString(),
                    installerPath,
                    ImmutableArray.Create("libraries"),
                    updatedConfig.Target,
                    DateTimeOffset.UtcNow));
            return ResultExt.Ok(updatedConfig);
        }

        return ResultExt.Err<InstanceConfiguration>("Forge factory's unreachable code here");
    }
}
