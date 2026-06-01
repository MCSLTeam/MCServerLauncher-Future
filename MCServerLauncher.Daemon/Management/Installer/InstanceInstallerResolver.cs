using System.Diagnostics.CodeAnalysis;
using MCServerLauncher.Common.Minecraft;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Installer.MinecraftForge;

namespace MCServerLauncher.Daemon.Management.Installer;

public static class InstanceInstallerResolver
{
    public static IInstanceInstaller Resolve(InstanceFactorySetting setting, string installerPath)
    {
        return setting.InstanceType switch
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
    private static IInstanceInstaller ResolveForgeFamilyInstaller(InstanceFactorySetting setting, string installerPath)
    {
        var mcVersion = McVersion.Of(setting.McVersion);

        if (mcVersion.Between("1.5.2", "1.12.1"))
            return ForgeInstallerV1.Create(installerPath, setting.JavaPath, setting.Mirror)
                   ?? throw new InvalidOperationException($"Failed to create ForgeInstallerV1 for {setting.InstanceType} {setting.McVersion}");

        if (mcVersion.Between(McVersion.Of("1.12.2"), McVersion.Max))
            return ForgeInstallerV2.Create(installerPath, setting.JavaPath, setting.Mirror)
                   ?? throw new InvalidOperationException($"Failed to create ForgeInstallerV2 for {setting.InstanceType} {setting.McVersion}");

        throw new InvalidOperationException($"No installer available for {setting.InstanceType} on Minecraft {setting.McVersion}");
    }
}
