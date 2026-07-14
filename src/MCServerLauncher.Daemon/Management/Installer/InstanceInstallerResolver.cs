using System.Diagnostics.CodeAnalysis;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Minecraft;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Installer.MinecraftForge;

namespace MCServerLauncher.Daemon.Management.Installer;

public static class InstanceInstallerResolver
{
    public static IInstanceInstaller Resolve(InstanceFactoryConfiguration setting, string installerPath)
    {
        return setting.Configuration.InstanceType switch
        {
            InstanceType.MCForge => ResolveForgeFamilyInstaller(setting, installerPath),
            InstanceType.MCNeoForge => ResolveForgeFamilyInstaller(setting, installerPath),
            InstanceType.MCCleanroom => ResolveForgeFamilyInstaller(setting, installerPath),
            InstanceType.MCFabric => PassthroughInstaller.Instance,
            _ => PassthroughInstaller.Instance
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Forge-family installer selection intentionally crosses trim-incompatible Forge installer parsing boundaries.")]
    private static IInstanceInstaller ResolveForgeFamilyInstaller(InstanceFactoryConfiguration setting, string installerPath)
    {
        var config = setting.Configuration;
        var mcVersion = McVersion.Of(config.Version);

        if (mcVersion.Between("1.5.2", "1.12.1"))
            return ForgeInstallerV1.Create(installerPath, config.JavaPath, setting.Mirror)
                   ?? throw new InvalidOperationException($"Failed to create ForgeInstallerV1 for {config.InstanceType} {config.Version}");

        if (mcVersion.Between(McVersion.Of("1.12.2"), McVersion.Max))
            return ForgeInstallerV2.Create(installerPath, config.JavaPath, setting.Mirror)
                   ?? throw new InvalidOperationException($"Failed to create ForgeInstallerV2 for {config.InstanceType} {config.Version}");

        throw new InvalidOperationException($"No installer available for {config.InstanceType} on Minecraft {config.Version}");
    }
}
