using System.Text;
using System.Runtime.ExceptionServices;
using Downloader;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Detection;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Management.Factory;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using Serilog;

namespace MCServerLauncher.Daemon.Management;

public static class InstanceFactoryConfigurationExtensions
{
    public static async Task<Result<Unit, DaemonError>> CopyAndRenameTarget(
        this InstanceFactoryConfiguration setting,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var config = setting.Configuration;
        var workingDirectory = config.GetWorkingDirectory();
        if (!InstanceTargetPathValidator.TryResolveTargetFile(
                workingDirectory,
                config.Target,
                out var targetPath,
                out var targetError))
        {
            return ResultExt.Err<Unit>(targetError!);
        }

        var destinationName = Path.GetFileName(setting.Source);
        var destinationPath = Path.Combine(workingDirectory, destinationName);

        if (Uri.TryCreate(setting.Source, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile)
            {
                var sourceResult = ResolveLocalSourcePath(setting.Source);
                if (sourceResult.IsErr(out var sourceError))
                    return ResultExt.Err<Unit>(sourceError!);

                var sourcePath = sourceResult.Unwrap();
                if (!PathsEqual(sourcePath, destinationPath))
                {
                    var copy = ResultExt.Try(() => File.Copy(sourcePath, destinationPath, true))
                        .MapErr(exception => MapStorageFailure(
                            exception,
                            "instance.source.copy_failed",
                            "Failed to copy the instance source.",
                            "copying the instance source",
                            cancellationToken));
                    if (copy.IsErr(out var copyError))
                    {
                        DeleteFileIfExists(destinationPath);
                        return ResultExt.Err<Unit>(copyError!);
                    }
                }
            }
            else if (uri.Scheme == Uri.UriSchemeFtp || uri.Scheme == Uri.UriSchemeFtps ||
                     uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            {
                var download = DownloadBuilder
                    .New()
                    .WithUrl(setting.Source)
                    .WithFileLocation(destinationPath)
                    .WithConfiguration(new DownloadConfiguration
                    {
                        ChunkCount = 8,
                        ParallelDownload = true
                    }).Build()!;
                try
                {
                    await download.StartAsync(cancellationToken);
                }
                catch (Exception exception)
                {
                    DeleteFileIfExists(destinationPath);
                    return ResultExt.Err<Unit>(MapStorageFailure(
                        exception,
                        "instance.source.download_failed",
                        "Failed to download the instance source.",
                        "downloading the instance source",
                        cancellationToken));
                }

                if (download.Status == DownloadStatus.Failed)
                {
                    DeleteFileIfExists(destinationPath);
                    return Result.Err<Unit, DaemonError>(new StorageDaemonError(
                        "instance.source.download_failed",
                        "Failed to download the instance source."));
                }
            }
            else
            {
                return ResultExt.Err<Unit>(new ValidationDaemonError(
                    "instance.source.invalid",
                    "The instance source URI is not supported."));
            }
        }
        else
        {
            var sourceResult = ResolveLocalSourcePath(setting.Source);
            if (sourceResult.IsErr(out var sourceError))
                return ResultExt.Err<Unit>(sourceError!);

            var resolvedSource = sourceResult.Unwrap();
            cancellationToken.ThrowIfCancellationRequested();
            Result<Unit, DaemonError> copy;
            if (PathsEqual(resolvedSource, destinationPath))
            {
                copy = ResultExt.Ok();
            }
            else
            {
                copy = ResultExt.Try(() => File.Copy(resolvedSource, destinationPath, true))
                    .MapErr(exception => MapStorageFailure(
                        exception,
                        "instance.source.copy_failed",
                        "Failed to copy the instance source.",
                        "copying the instance source",
                        cancellationToken));
            }
            if (copy.IsErr(out var error))
            {
                DeleteFileIfExists(destinationPath);
                return Result.Err<Unit, DaemonError>(error!);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return !PathsEqual(destinationPath, targetPath)
            ? ResultExt.Try(() => File.Move(destinationPath, targetPath))
                .MapErr(exception => MapStorageFailure(
                    exception,
                    "instance.factory.copy_failed",
                    "Failed to copy the instance source.",
                    "moving the instance source",
                    cancellationToken))
            : ResultExt.Ok(Unit.Default);
    }

    public static async Task<Result<InstanceConfiguration, DaemonError>> ApplyInstanceFactory(
        this InstanceFactoryConfiguration setting,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var config = setting.Configuration;
        if (!InstanceTargetPathValidator.TryResolveTargetFile(
                config.GetWorkingDirectory(),
                config.Target,
                out _,
                out var targetError))
        {
            return ResultExt.Err<InstanceConfiguration>(targetError!);
        }

        InstanceFactoryConfiguration reconciledSetting;
        try
        {
            reconciledSetting = InstanceVersionDetector.Reconcile(
                setting,
                source => FileManager.ResolveAndValidatePath(source));
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[InstanceManager] Failed to reconcile instance factory configuration.");
            return ResultExt.Err<InstanceConfiguration>(new InternalDaemonError(
                "instance.factory.failed",
                "The instance factory could not create the instance."));
        }

        Func<InstanceFactoryConfiguration, CancellationToken, Task<Result<InstanceConfiguration, DaemonError>>>
            instanceFactory;
        try
        {
            instanceFactory = InstanceFactoryRegistry.GetInstanceFactory(reconciledSetting);
        }
        catch (NotImplementedException exception)
        {
            Log.Warning(exception, "[InstanceManager] No factory is available for {InstanceType} {Version}.",
                setting.Configuration.InstanceType, setting.Configuration.Version);
            return ResultExt.Err<InstanceConfiguration>(new ValidationDaemonError(
                "instance.factory.unsupported",
                "No instance factory supports the requested instance configuration."));
        }

        Log.Information(
            "[InstanceManager] Running InstanceFactory for instance '{0}' as {1}",
            reconciledSetting.Configuration.Name,
            reconciledSetting.Configuration.InstanceType);
        try
        {
            return await instanceFactory.Invoke(reconciledSetting, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[InstanceManager] Instance factory execution failed.");
            return ResultExt.Err<InstanceConfiguration>(new InternalDaemonError(
                "instance.factory.failed",
                "The instance factory could not create the instance."));
        }
    }

    public static Result<Unit, DaemonError> ValidateSetting(this InstanceFactoryConfiguration setting)
    {
        if (setting.SourceType is SourceType.None)
            return ResultExt.Err<Unit>(new ValidationDaemonError(
                "instance.source_type.invalid",
                "source_type could not be none."));

        if (Uri.TryCreate(setting.Source, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            if (uri.Scheme != Uri.UriSchemeFtp &&
                uri.Scheme != Uri.UriSchemeFtps &&
                uri.Scheme != Uri.UriSchemeHttp &&
                uri.Scheme != Uri.UriSchemeHttps)
            {
                return ResultExt.Err<Unit>(new ValidationDaemonError(
                    "instance.source.invalid",
                    "The instance source URI is not supported."));
            }
        }
        else
        {
            var sourceResult = ResolveLocalSourcePath(setting.Source);
            if (sourceResult.IsErr(out var sourceError))
                return ResultExt.Err<Unit>(sourceError!);
        }

        var reconciledSetting = InstanceVersionDetector.Reconcile(
            setting,
            source => FileManager.ResolveAndValidatePath(source));
        return reconciledSetting.Configuration.ValidateConfig();
    }

    public static async Task<Result<Unit, DaemonError>> FixEula(
        this InstanceFactoryConfiguration setting,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var eulaPath = Path.Combine(setting.Configuration.GetWorkingDirectory(), "eula.txt");

        var eula = await ReadOrGenerateEula(eulaPath, cancellationToken);
        if (eula.IsErr(out var readError) && readError is not null)
            return ResultExt.Err<Unit>(readError);

        return await WriteEula(eulaPath, eula.Unwrap(), cancellationToken);
    }

    private static string[] GenerateEula()
    {
        return
        [
            "#By changing the setting below to TRUE you are indicating your agreement to our EULA (https://aka.ms/MinecraftEULA).",
            "#" + DateTime.Now.ToString("ddd MMM dd HH:mm:ss zzz yyyy"),
            "eula=true"
        ];
    }

    private static Result<string, DaemonError> ResolveLocalSourcePath(string source)
    {
        try
        {
            var candidate = Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile
                ? uri.LocalPath
                : source;
            var resolvedPath = FileManager.ResolveAndValidatePath(candidate);
            return File.Exists(resolvedPath)
                ? ResultExt.Ok<string>(resolvedPath)
                : ResultExt.Err<string>(new NotFoundDaemonError(
                    "instance.source.not_found",
                    $"Source not found at {source}."));
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "[InstanceFactory] Rejected source path {Source}.", source);
            return ResultExt.Err<string>(new ValidationDaemonError(
                "instance.source.invalid",
                "The instance source path is invalid."));
        }
    }

    private static async Task<Result<string[], DaemonError>> ReadOrGenerateEula(
        string eulaPath,
        CancellationToken cancellationToken)
    {
        var result = await ResultExt.TryAsync(async path =>
        {
            return File.Exists(path)
                ? (await File.ReadAllLinesAsync(path, cancellationToken))
                .Select(static line => line.Trim().StartsWith("eula") ? "eula=true" : line)
                .ToArray()
                : GenerateEula();
        }, eulaPath);

        return result.MapErr(exception => MapStorageFailure(
            exception,
            "instance.eula.read_failed",
            "Failed to read EULA.",
            "reading EULA",
            cancellationToken));
    }

    private static async Task<Result<Unit, DaemonError>> WriteEula(
        string eulaPath,
        string[] text,
        CancellationToken cancellationToken)
    {
        var result = await ResultExt.TryAsync(async state =>
        {
            var (path, lines) = state;
            await File.WriteAllLinesAsync(path, lines, Encoding.UTF8, cancellationToken);
        }, (eulaPath, text));

        return result.MapErr(exception => MapStorageFailure(
            exception,
            "instance.eula.write_failed",
            "Failed to write EULA.",
            "writing EULA",
            cancellationToken));
    }

    private static DaemonError MapStorageFailure(
        Exception exception,
        string code,
        string message,
        string operation,
        CancellationToken cancellationToken = default)
    {
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            ExceptionDispatchInfo.Capture(exception).Throw();

        Log.Error(exception, "[InstanceFactory] Failed while {Operation}.", operation);
        return new StorageDaemonError(code, message);
    }

    private static void DeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "[InstanceFactory] Failed to clean up partial file {Path}.", path);
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
