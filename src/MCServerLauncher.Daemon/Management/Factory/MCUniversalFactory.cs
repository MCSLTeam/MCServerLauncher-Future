using System.Collections.Immutable;
using System.IO.Compression;
using System.Runtime.ExceptionServices;
using System.Text;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Common.Minecraft;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Management.Installer;
using MCServerLauncher.Daemon.Management.Minecraft;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using Serilog;

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
    public async Task<Result<InstanceConfiguration, DaemonError>> CreateInstanceFromArchive(
        InstanceFactoryConfiguration setting,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var workingDirectory = setting.Configuration.GetWorkingDirectory();

        var extract = ResultExt.Try(() =>
            ZipFile.ExtractToDirectory(setting.Source, workingDirectory, Encoding.UTF8, true));
        if (extract.IsErr(out var exception))
        {
            return ResultExt.Err<InstanceConfiguration>(MapStorageFailure(
                exception!,
                "instance.archive.extract_failed",
                "Universal factory could not extract the instance archive.",
                "extracting the instance archive"));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var fixEula = await setting.FixEula(cancellationToken);
        if (fixEula.IsErr(out var error))
            return ResultExt.Err<InstanceConfiguration>(error!);

        return ResultExt.Ok(setting.Configuration);
    }

    public async Task<Result<InstanceConfiguration, DaemonError>> CreateInstanceFromCore(
        InstanceFactoryConfiguration setting,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        setting = setting with
        {
            Configuration = InstanceConfigurationMapper.WithTarget(
                setting.Configuration,
                setting.Configuration.Target,
                TargetType.Jar)
        };

        var copyAndRenameTarget = await setting.CopyAndRenameTarget(cancellationToken);
        if (copyAndRenameTarget.IsErr(out var error))
            return ResultExt.Err<InstanceConfiguration>(error!);

        var installerResult = InstanceInstallerResolver.Resolve(
            setting,
            Path.Combine(setting.Configuration.GetWorkingDirectory(), setting.Configuration.Target),
            cancellationToken);
        if (installerResult.IsErr(out error))
            return ResultExt.Err<InstanceConfiguration>(error!);

        var install = await installerResult.Unwrap().Run(setting, cancellationToken);
        if (install.IsErr(out error))
            return ResultExt.Err<InstanceConfiguration>(error!);

        var fixEula = await setting.FixEula(cancellationToken);
        if (fixEula.IsErr(out error))
            return ResultExt.Err<InstanceConfiguration>(error!);

        var config = setting.Configuration;
        var metadataResult = InstanceInstallMetadataStore.TryWrite(
            config.GetWorkingDirectory(),
            new InstanceInstallMetadata(
                config.InstanceType.ToString(),
                setting.Source,
                ImmutableArray<string>.Empty,
                config.Target,
                DateTimeOffset.UtcNow),
            cancellationToken);
        if (metadataResult.IsErr(out error))
            return ResultExt.Err<InstanceConfiguration>(error!);

        return ResultExt.Ok(config);
    }

    public Func<MinecraftInstance, Task<Result<Unit, DaemonError>>>[] GetPostProcessors()
    {
        return new List<Func<MinecraftInstance, Task<Result<Unit, DaemonError>>>>
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

    private static DaemonError MapStorageFailure(
        Exception exception,
        string code,
        string message,
        string operation)
    {
        if (exception is OperationCanceledException)
            ExceptionDispatchInfo.Capture(exception).Throw();

        Log.Error(exception, "[MCUniversalFactory] Failed while {Operation}.", operation);
        return new StorageDaemonError(code, message);
    }
}
