using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Management.Factory;
using MCServerLauncher.Daemon.Management.Installer;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Remote.Action.Handlers;

namespace MCServerLauncher.ProtocolTests;

public sealed class FactoryAndLegacyErrorMappingTests
{
    [Fact]
    public async Task FixEula_WhenEulaPathIsDirectory_ReturnsStableStorageError()
    {
        var setting = CreateSetting();
        var workingDirectory = setting.Configuration.GetWorkingDirectory();
        Directory.CreateDirectory(workingDirectory);
        Directory.CreateDirectory(Path.Combine(workingDirectory, "eula.txt"));

        try
        {
            var result = await setting.FixEula();

            Assert.True(result.IsErr(out var error));
            var storageError = Assert.IsType<StorageDaemonError>(error);
            Assert.Equal("instance.eula.write_failed", storageError.Code);
            Assert.Equal("Failed to write EULA.", storageError.Message);
            Assert.Null(storageError.Details);
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory, true);
        }
    }

    [Fact]
    public async Task CreateInstanceFromArchive_WhenEulaWriteFails_PropagatesTypedStorageError()
    {
        var setting = CreateSetting();
        var sourceArchive = Path.Combine(Path.GetTempPath(), $"mcsl-factory-{Guid.NewGuid():N}.zip");
        var workingDirectory = setting.Configuration.GetWorkingDirectory();
        Directory.CreateDirectory(workingDirectory);
        Directory.CreateDirectory(Path.Combine(workingDirectory, "eula.txt"));
        using (var archive = ZipFile.Open(sourceArchive, ZipArchiveMode.Create))
            archive.CreateEntry("server.properties");

        try
        {
            var result = await new MCUniversalFactory().CreateInstanceFromArchive(setting with
            {
                Source = sourceArchive,
                SourceType = SourceType.Archive
            });

            Assert.True(result.IsErr(out var error));
            Assert.Equal("instance.eula.write_failed", error!.Code);
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory, true);
            if (File.Exists(sourceArchive))
                File.Delete(sourceArchive);
        }
    }

    [Fact]
    public async Task CopyAndRenameTarget_OutsideFileUri_ReturnsTypedValidationError()
    {
        var outsidePath = Path.Combine(Path.GetTempPath(), $"mcsl-outside-copy-{Guid.NewGuid():N}.jar");
        await File.WriteAllTextAsync(outsidePath, "outside");
        var setting = CreateSetting() with { Source = new Uri(outsidePath).AbsoluteUri };
        var workingDirectory = setting.Configuration.GetWorkingDirectory();
        Directory.CreateDirectory(workingDirectory);

        try
        {
            var result = await setting.CopyAndRenameTarget();

            Assert.True(result.IsErr(out var error));
            var validation = Assert.IsType<ValidationDaemonError>(error);
            Assert.Equal("instance.source.invalid", validation.Code);
            Assert.False(File.Exists(Path.Combine(workingDirectory, setting.Configuration.Target)));
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory, true);
            if (File.Exists(outsidePath))
                File.Delete(outsidePath);
        }
    }

    [Fact]
    public async Task CopyAndRenameTarget_FileUriCopyFailure_ReturnsTypedStorageError()
    {
        var sourcePath = Path.Combine(FileManager.UploadRoot, $"mcsl-copy-failure-{Guid.NewGuid():N}.jar");
        var setting = CreateSetting() with { Source = new Uri(sourcePath).AbsoluteUri };
        var workingDirectory = setting.Configuration.GetWorkingDirectory();
        var destinationPath = Path.Combine(workingDirectory, Path.GetFileName(setting.Source));
        Directory.CreateDirectory(FileManager.UploadRoot);
        Directory.CreateDirectory(workingDirectory);
        Directory.CreateDirectory(destinationPath);
        await File.WriteAllTextAsync(sourcePath, "source");

        try
        {
            var result = await setting.CopyAndRenameTarget();

            Assert.True(result.IsErr(out var error));
            var storage = Assert.IsType<StorageDaemonError>(error);
            Assert.Equal("instance.source.copy_failed", storage.Code);
            Assert.Equal("Failed to copy the instance source.", storage.Message);
            Assert.Null(storage.Details);
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory, true);
            if (File.Exists(sourcePath))
                File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task CopyAndRenameTarget_SourceBasenameMatchesTarget_SkipsSelfMove()
    {
        var fileName = $"same-source-target-{Guid.NewGuid():N}.jar";
        var sourcePath = Path.Combine(FileManager.UploadRoot, fileName);
        var baseSetting = CreateSetting();
        var setting = baseSetting with
        {
            Source = sourcePath,
            Configuration = InstanceConfigurationMapper.WithTarget(
                baseSetting.Configuration,
                fileName,
                TargetType.Jar)
        };
        var workingDirectory = setting.Configuration.GetWorkingDirectory();
        var targetPath = Path.Combine(workingDirectory, setting.Configuration.Target);
        Directory.CreateDirectory(FileManager.UploadRoot);
        Directory.CreateDirectory(workingDirectory);
        await File.WriteAllTextAsync(sourcePath, "source-core");

        try
        {
            var result = await setting.CopyAndRenameTarget();

            Assert.True(result.IsOk(out _));
            Assert.Equal("source-core", await File.ReadAllTextAsync(targetPath));
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory, true);
            if (File.Exists(sourcePath))
                File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task PassthroughInstaller_CanceledToken_PropagatesCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PassthroughInstaller.Instance.Run(CreateSetting(), cancellationSource.Token));
    }

    [Fact]
    public void ForgeResolver_InvalidInstaller_ReturnsTypedStorageError()
    {
        var setting = CreateSetting() with
        {
            Configuration = new InstanceConfiguration(
                Guid.NewGuid(),
                "forge-resolver-test",
                "installer.jar",
                InstanceType.MCForge,
                TargetType.Jar,
                "1.20.1",
                "utf-8",
                "utf-8",
                "java",
                ImmutableArray<string>.Empty,
                ImmutableDictionary<string, string>.Empty,
                JsonSerializer.SerializeToElement(Array.Empty<object>()))
        };

        var result = InstanceInstallerResolver.Resolve(
            setting,
            Path.Combine(setting.Configuration.GetWorkingDirectory(), setting.Configuration.Target));

        Assert.True(result.IsErr(out var error));
        var storage = Assert.IsType<StorageDaemonError>(error);
        Assert.Equal("instance.installer.resolve_failed", storage.Code);
    }

    [Fact]
    public async Task CreateInstanceFromCore_MetadataWriteFailure_ReturnsTypedStorageError()
    {
        var setting = CreateSetting() with
        {
            Source = Path.Combine(FileManager.UploadRoot, $"mcsl-core-{Guid.NewGuid():N}.jar"),
            SourceType = SourceType.Core,
            Configuration = new InstanceConfiguration(
                Guid.NewGuid(),
                "metadata-write-test",
                "server.jar",
                InstanceType.MCVanilla,
                TargetType.Jar,
                string.Empty,
                "utf-8",
                "utf-8",
                "java",
                ImmutableArray<string>.Empty,
                ImmutableDictionary<string, string>.Empty,
                JsonSerializer.SerializeToElement(Array.Empty<object>()))
        };
        var workingDirectory = setting.Configuration.GetWorkingDirectory();
        Directory.CreateDirectory(FileManager.UploadRoot);
        Directory.CreateDirectory(workingDirectory);
        await File.WriteAllTextAsync(setting.Source, "server-core");
        Directory.CreateDirectory(InstanceInstallMetadataStore.GetPath(workingDirectory));

        try
        {
            var result = await new MCUniversalFactory().CreateInstanceFromCore(setting);

            Assert.True(result.IsErr(out var error));
            var storage = Assert.IsType<StorageDaemonError>(error);
            Assert.Equal("instance.install_metadata.write_failed", storage.Code);
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory, true);
            if (File.Exists(setting.Source))
                File.Delete(setting.Source);
        }
    }

    [Theory]
    [InlineData("instance.not_found", 30001)]
    [InlineData("instance.running", 30003)]
    [InlineData("instance.invalid", 10001)]
    [InlineData("unexpected", 31003)]
    public void LegacyActionErrorMapper_MapsTypedErrorsToV1Retcodes(string errorCode, int expectedRetcode)
    {
        var error = new InternalDaemonError(errorCode, "stable message");

        var actionError = LegacyActionErrorMapper.ToActionError(error, ActionRetcode.ProcessError);

        Assert.Equal(expectedRetcode, actionError.Retcode.Code);
        Assert.Contains("stable message", actionError.Cause);
        Assert.Null(actionError.CausedException);
    }

    [Theory]
    [InlineData(DaemonErrorKind.Validation, 10006)]
    [InlineData(DaemonErrorKind.NotFound, 21001)]
    [InlineData(DaemonErrorKind.Conflict, 21101)]
    [InlineData(DaemonErrorKind.Permission, 21006)]
    [InlineData(DaemonErrorKind.Storage, 21000)]
    [InlineData(DaemonErrorKind.Internal, 21000)]
    public void LegacyFileActionAdapter_MapsTypedErrorsToV1Retcodes(
        DaemonErrorKind kind,
        int expectedRetcode)
    {
        var error = CreateError(kind);

        var actionError = LegacyFileActionAdapter.ToActionError(error);

        Assert.Equal(expectedRetcode, actionError.Retcode.Code);
        Assert.Contains("stable message", actionError.Cause);
        Assert.Null(actionError.CausedException);
    }

    private static InstanceFactoryConfiguration CreateSetting()
    {
        var configuration = new InstanceConfiguration(
            Guid.NewGuid(),
            "factory-error-test",
            "server.jar",
            InstanceType.MCVanilla,
            TargetType.Jar,
            "1.20.1",
            "utf-8",
            "utf-8",
            "java",
            ImmutableArray<string>.Empty,
            ImmutableDictionary<string, string>.Empty,
            JsonSerializer.SerializeToElement(Array.Empty<object>()));

        return new InstanceFactoryConfiguration(
            configuration,
            "source.jar",
            SourceType.Core,
            InstanceFactoryMirror.None,
            false);
    }

    private static DaemonError CreateError(DaemonErrorKind kind) => kind switch
    {
        DaemonErrorKind.Validation => new ValidationDaemonError("test.validation", "stable message"),
        DaemonErrorKind.NotFound => new NotFoundDaemonError("test.not_found", "stable message"),
        DaemonErrorKind.Conflict => new ConflictDaemonError("test.conflict", "stable message"),
        DaemonErrorKind.Permission => new PermissionDaemonError("test.permission", "stable message"),
        DaemonErrorKind.Storage => new StorageDaemonError("test.storage", "stable message"),
        DaemonErrorKind.Transport => new TransportDaemonError("test.transport", "stable message"),
        DaemonErrorKind.Internal => new InternalDaemonError("test.internal", "stable message"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
