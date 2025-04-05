using System.Runtime.InteropServices;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Minecraft.Server.Installer.Forge;

namespace MCServerLauncher.Daemon.Minecraft.Server.Factory;

[InstanceFactory(InstanceType.Forge, minVersion: "1.12")]
public class ForgeFactory : ICoreInstanceFactory
{
    public async Task<InstanceConfig> CreateInstanceFromCore(InstanceFactorySetting setting)
    {
        if (!await setting.CopyAndRenameTarget())
            throw new InstanceFactoryException(setting, "Failed to download target source");

        var installerPath = Path.Combine(setting.GetWorkingDirectory(), setting.Target);


        // TODO 更多报错
        var forgeInstaller = ForgeInstaller.Create(installerPath, setting.JavaPath);
        if (forgeInstaller is null)
            throw new InstanceFactoryException(setting, "Failed to create forge installer");

        // TODO 更多报错
        // 安装
        if (!await forgeInstaller.Run(setting.GetWorkingDirectory()))
            throw new InstanceFactoryException(setting, "Failed to install forge");
        await setting.FixEula();

        // 处理启动参数
        var mcVersion = McVersion.Of(setting.McVersion); // 可以直接转换因为已经检查过了

        if (mcVersion.Between(McVersion.Of("1.17"), McVersion.Max))
        {
            // TODO 自动处理启动脚本,将其转换为核心 + jvm arg, 方便统一管理
            var config = setting.GetInstanceConfig();
            return config with
            {
                TargetType = TargetType.Script,
                Target = "run" + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".bat" : ".sh")
            };
        }

        if (mcVersion.Between("1.12", "1.17")) // TODO 明确InstallV1支持的最低版本
        {
            var profile = ForgeInstaller.GetInstallerProfile(setting.Target)!; // 不为空,应为已经安装过了且无问题
            var config = setting.GetInstanceConfig();
            return config with
            {
                Target = profile.Path!.Filename
            };
        }

        throw new NotImplementedException();
    }
}