using System.Runtime.InteropServices;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Minecraft.Server.Installer.Forge;
using MCServerLauncher.Daemon.Storage;

namespace MCServerLauncher.Daemon.Minecraft.Server.Factory;

[InstanceFactory(InstanceType.Forge, minVersion: "1.5.2")]
[InstanceFactory(InstanceType.NeoForge, minVersion: "1.20.2")] // TODO BMCLAPI加速
[InstanceFactory(InstanceType.Cleanroom, minVersion: "1.12.2",maxVersion: "1.12.2")]
public class ForgeFactory : ICoreInstanceFactory
{
    public async Task<InstanceConfig> CreateInstanceFromCore(InstanceFactorySetting setting)
    {
        if (!await setting.CopyAndRenameTarget())
            throw new InstanceFactoryException(setting, "Failed to download target source");

        var installerPath = Path.Combine(setting.GetWorkingDirectory(), setting.Target);

        var mcVersion = McVersion.Of(setting.McVersion); // 可以直接转换因为已经检查过了

        // TODO 更多报错
        ForgeInstallerBase? forgeInstaller = null;

        if (mcVersion.Between("1.5.2", "1.12.1"))
            forgeInstaller = ForgeInstallerV1.Create(installerPath, setting.JavaPath);
        else if (mcVersion.Between(McVersion.Of("1.12.2"), McVersion.Max))
            forgeInstaller = ForgeInstallerV2.Create(installerPath, setting.JavaPath);

        if (forgeInstaller is null)
            throw new InstanceFactoryException(setting, "Failed to create forge installer");

        // TODO 更多报错
        // 安装
        if (!await forgeInstaller.Run(setting.GetWorkingDirectory()))
            throw new InstanceFactoryException(setting, "Failed to install forge");
        await setting.FixEula();

        // 处理启动参数
        if (mcVersion.Between(McVersion.Of("1.17"), McVersion.Max))
        {
            // TODO 自动处理启动脚本,将其转换为核心 + jvm arg, 方便统一管理
            var serverLauncher = await ContainedFiles.EnsureContained(ContainedFiles.NeoForgeServerLauncher);
            File.Copy(
                serverLauncher, 
                Path.Combine(setting.GetWorkingDirectory(), ContainedFiles.NeoForgeServerLauncher), 
                true
                );
            var config = setting.GetInstanceConfig();
            return config with
            {
                TargetType = TargetType.Jar,
                Target = ContainedFiles.NeoForgeServerLauncher
            };
        }

        // 确定最低支持版本(1.5.2)：再低就没Forge Installer了！
        if (mcVersion.Between(McVersion.Of("1.5.2"), McVersion.Of("1.16.5")))
        {
            var profile = forgeInstaller.Install; // 不为空,应为已经安装过了且无问题
            var config = setting.GetInstanceConfig();
            return config with
            {
                TargetType = TargetType.Jar,
                Target = forgeInstaller is ForgeInstallerV2 ? profile.Path!.Filename : profile.FilePath!
            };
        }

        throw new NotImplementedException();
    }
}