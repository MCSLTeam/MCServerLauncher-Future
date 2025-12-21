using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Installer.MinecraftForge;
using MCServerLauncher.Daemon.Management.Minecraft;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;

namespace MCServerLauncher.Daemon.Management.Factory;

[InstanceFactory(InstanceType.MCForge, minVersion: "1.5.2")]
[InstanceFactory(InstanceType.MCNeoForge, minVersion: "1.20.2")] // TODO BMCLAPI加速
[InstanceFactory(InstanceType.MCCleanroom, minVersion: "1.12.2", maxVersion: "1.12.2")]
public class MCForgeFactory : ICoreInstanceFactory
{
    public async Task<Result<InstanceConfig, Error>> CreateInstanceFromCore(InstanceFactorySetting setting)
    {
        var copyAndRenameTarget = await setting.CopyAndRenameTarget();
        if (copyAndRenameTarget.IsErr(out var error))
            return ResultExt.Err<InstanceConfig>("Forge factory could not create instance from core", error);

        var installerPath = Path.Combine(setting.GetWorkingDirectory(), setting.Target);

        var mcVersion = McVersion.Of(setting.McVersion); // 可以直接转换因为已经检查过了


        ForgeInstallerBase? forgeInstaller = null;
        if (mcVersion.Between("1.5.2", "1.12.1"))
            forgeInstaller = ForgeInstallerV1.Create(installerPath, setting.JavaPath, setting.Mirror);
        else if (mcVersion.Between(McVersion.Of("1.12.2"), McVersion.Max))
            forgeInstaller = ForgeInstallerV2.Create(installerPath, setting.JavaPath, setting.Mirror);
        if (forgeInstaller is null)
            return ResultExt.Err<InstanceConfig>(
                $"Forge factory failed to create forge installer (mc version not supported: {setting.McVersion})");

        var result = await forgeInstaller.Run(setting);
        if (result.IsErr(out error))
            return ResultExt.Err<InstanceConfig>(
                $"Forge factory failed to run forge installer({forgeInstaller.GetType().Name})", error);

        var fixEula = await setting.FixEula();
        if (fixEula.IsErr(out error))
            return ResultExt.Err<InstanceConfig>("Forge factory failed to overwrite eula.txt", error);

        var config = setting.GetInstanceConfig();
        // 处理启动参数
        if (mcVersion.Between(McVersion.Of("1.17"), McVersion.Max))
        {
            var extractResult = await ResultExt.TryAsync(async Task () =>
            {
                var serverLauncher = await ContainedFiles.EnsureContained(ContainedFiles.NeoForgeServerLauncher);
                File.Copy(
                    serverLauncher,
                    Path.Combine(setting.GetWorkingDirectory(), ContainedFiles.NeoForgeServerLauncher),
                    true
                );
            });
            return extractResult.MapErr(Error.FromException).Map(_ =>
                config with
                {
                    TargetType = TargetType.Jar,
                    Target = ContainedFiles.NeoForgeServerLauncher
                }
            );
        }

        // 确定最低支持版本(1.5.2)：再低就没Forge Installer了！
        if (mcVersion.Between(McVersion.Of("1.5.2"), McVersion.Of("1.16.5")))
        {
            var profile = forgeInstaller.Install; // 不为空,应为已经安装过了且无问题
            return ResultExt.Ok(config with
            {
                TargetType = TargetType.Jar,
                Target = forgeInstaller is ForgeInstallerV2 ? profile.Path!.Filename : profile.FilePath!
            });
        }

        return ResultExt.Err<InstanceConfig>("Forge factory's unreachable code here");
    }
}