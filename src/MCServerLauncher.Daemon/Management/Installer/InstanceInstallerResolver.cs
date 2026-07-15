using System.Diagnostics.CodeAnalysis;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Minecraft;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Management.Installer.MinecraftForge;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using Serilog;

namespace MCServerLauncher.Daemon.Management.Installer;

public static class InstanceInstallerResolver
{
    public static Result<IInstanceInstaller, DaemonError> Resolve(
        InstanceFactoryConfiguration setting,
        string installerPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var installer = setting.Configuration.InstanceType switch
            {
                InstanceType.MCForge => ResolveForgeFamilyInstaller(setting, installerPath),
                InstanceType.MCNeoForge => ResolveForgeFamilyInstaller(setting, installerPath),
                InstanceType.MCCleanroom => ResolveForgeFamilyInstaller(setting, installerPath),
                InstanceType.MCFabric => PassthroughInstaller.Instance,
                _ => PassthroughInstaller.Instance
            };
            return ResultExt.Ok<IInstanceInstaller>(installer);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[InstanceInstallerResolver] Failed to resolve installer for {InstanceType} {Version}.",
                setting.Configuration.InstanceType, setting.Configuration.Version);
            return ResultExt.Err<IInstanceInstaller>(new StorageDaemonError(
                "instance.installer.resolve_failed",
                "The instance installer could not be resolved."));
        }
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
