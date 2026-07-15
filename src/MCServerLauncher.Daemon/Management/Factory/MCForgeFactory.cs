using System.Diagnostics.CodeAnalysis;
using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Management.Installer;
using MCServerLauncher.Daemon.Management.Installer.MinecraftForge;
using MCServerLauncher.Common.Minecraft;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using Serilog;

namespace MCServerLauncher.Daemon.Management.Factory;

[InstanceFactory(InstanceType.MCForge, minVersion: "1.5.2")]
[InstanceFactory(InstanceType.MCNeoForge, minVersion: "1.20.2")]
[InstanceFactory(InstanceType.MCCleanroom, minVersion: "1.12.2", maxVersion: "1.12.2")]
public class MCForgeFactory : ICoreInstanceFactory
{
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Forge installer selection intentionally enters localized trim-incompatible installer profile parsing boundaries for third-party Forge metadata.")]
    public async Task<Result<InstanceConfiguration, DaemonError>> CreateInstanceFromCore(
        InstanceFactoryConfiguration setting,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var config = setting.Configuration;
        var copyAndRenameTarget = await setting.CopyAndRenameTarget(cancellationToken);
        if (copyAndRenameTarget.IsErr(out var error))
            return ResultExt.Err<InstanceConfiguration>(error!);

        var installerPath = Path.Combine(config.GetWorkingDirectory(), config.Target);

        var mcVersion = McVersion.Of(config.Version); // 可以直接转换因为已经检查过了

        var installerResult = InstanceInstallerResolver.Resolve(setting, installerPath, cancellationToken);
        if (installerResult.IsErr(out error))
            return ResultExt.Err<InstanceConfiguration>(error!);

        var installer = installerResult.Unwrap();
        if (installer is not ForgeInstallerBase forgeInstaller)
            return ResultExt.Err<InstanceConfiguration>(
                new InternalDaemonError(
                    "instance.installer.resolve_failed",
                    $"Forge factory failed to resolve forge installer for {config.InstanceType} {config.Version}."));

        var result = await forgeInstaller.Run(setting, cancellationToken);
        if (result.IsErr(out error))
            return ResultExt.Err<InstanceConfiguration>(error!);

        var fixEula = await setting.FixEula(cancellationToken);
        if (fixEula.IsErr(out error))
            return ResultExt.Err<InstanceConfiguration>(error!);

        // 处理启动参数
        if (mcVersion.Between(McVersion.Of("1.17"), McVersion.Max))
        {
            var extractResult = await ResultExt.TryAsync(async Task () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var serverLauncher = await ContainedFiles.EnsureContained(ContainedFiles.NeoForgeServerLauncher);
                cancellationToken.ThrowIfCancellationRequested();
                File.Copy(
                    serverLauncher,
                    Path.Combine(config.GetWorkingDirectory(), ContainedFiles.NeoForgeServerLauncher),
                    true
                );
            });
            if (extractResult.IsErr(out var extractException))
                return ResultExt.Err<InstanceConfiguration>(MapStorageFailure(
                    extractException!,
                    "instance.launcher.extract_failed",
                    "Forge factory failed to extract the server launcher.",
                    "extracting the server launcher"));

            var updatedConfig = InstanceConfigurationMapper.WithTarget(
                config,
                ContainedFiles.NeoForgeServerLauncher,
                TargetType.Jar);
            var metadataResult = InstanceInstallMetadataStore.TryWrite(
                config.GetWorkingDirectory(),
                new InstanceInstallMetadata(
                    config.InstanceType.ToString(),
                    installerPath,
                    ImmutableArray.Create("libraries", "server.jar"),
                    updatedConfig.Target,
                    DateTimeOffset.UtcNow),
                cancellationToken);
            if (metadataResult.IsErr(out var metadataError))
                return ResultExt.Err<InstanceConfiguration>(metadataError!);
            return ResultExt.Ok(updatedConfig);
        }

        // 确定最低支持版本(1.5.2)：再低就没Forge Installer了！
        if (mcVersion.Between(McVersion.Of("1.5.2"), McVersion.Of("1.16.5")))
        {
            var profile = forgeInstaller.Install; // 不为空,应为已经安装过了且无问题
            var updatedConfig = InstanceConfigurationMapper.WithTarget(
                config,
                forgeInstaller is ForgeInstallerV2 ? profile.Path!.Filename : profile.FilePath!,
                TargetType.Jar);
            var metadataResult = InstanceInstallMetadataStore.TryWrite(
                config.GetWorkingDirectory(),
                new InstanceInstallMetadata(
                    config.InstanceType.ToString(),
                    installerPath,
                    ImmutableArray.Create("libraries"),
                    updatedConfig.Target,
                    DateTimeOffset.UtcNow),
                cancellationToken);
            if (metadataResult.IsErr(out var metadataError))
                return ResultExt.Err<InstanceConfiguration>(metadataError!);
            return ResultExt.Ok(updatedConfig);
        }

        return ResultExt.Err<InstanceConfiguration>(new InternalDaemonError(
            "instance.factory.unreachable",
            "Forge factory reached an unsupported version branch."));
    }

    private static DaemonError MapStorageFailure(
        Exception exception,
        string code,
        string message,
        string operation)
    {
        if (exception is OperationCanceledException)
            ExceptionDispatchInfo.Capture(exception).Throw();

        Log.Error(exception, "[MCForgeFactory] Failed while {Operation}.", operation);
        return new StorageDaemonError(code, message);
    }

}
