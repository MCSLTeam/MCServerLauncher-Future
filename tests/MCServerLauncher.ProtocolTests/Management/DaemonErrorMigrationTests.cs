using System.Collections.Immutable;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Storage;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests;

public sealed class DaemonErrorMigrationTests
{
    [Fact]
    public void ValidateConfig_MissingMinecraftVersion_ReturnsValidationError()
    {
        var config = new InstanceConfig
        {
            Uuid = Guid.NewGuid(),
            InstanceType = InstanceType.MCFabric,
            Version = string.Empty,
            TargetType = TargetType.Jar,
            JavaPath = "java",
            Target = "server.jar"
        };

        var result = config.ValidateConfig();

        Assert.True(result.IsErr(out var error));
        var validation = Assert.IsType<ValidationDaemonError>(error);
        Assert.Equal("instance.version.required", validation.Code);
    }

    [Fact]
    public void ValidateSetting_MissingFileSource_ReturnsNotFoundError()
    {
        var configuration = new InstanceConfiguration(
            Guid.NewGuid(),
            "missing-source",
            "server.exe",
            InstanceType.Universal,
            TargetType.Executable,
            string.Empty,
            "utf-8",
            "utf-8",
            string.Empty,
            ImmutableArray<string>.Empty,
            ImmutableDictionary<string, string>.Empty,
            JsonSerializer.SerializeToElement(Array.Empty<object>()));
        var source = new Uri(Path.Combine(
            FileManager.UploadRoot,
            $"mcsl-missing-{Guid.NewGuid():N}.jar")).AbsoluteUri;
        var setting = new InstanceFactoryConfiguration(
            configuration,
            source,
            SourceType.Core,
            InstanceFactoryMirror.None,
            false);

        var result = setting.ValidateSetting();

        Assert.True(result.IsErr(out var error));
        var notFound = Assert.IsType<NotFoundDaemonError>(error);
        Assert.Equal("instance.source.not_found", notFound.Code);
    }

    [Fact]
    public void ValidateSetting_OutsideSourcePath_ReturnsValidationError()
    {
        var configuration = new InstanceConfiguration(
            Guid.NewGuid(),
            "outside-source",
            "server.exe",
            InstanceType.Universal,
            TargetType.Executable,
            string.Empty,
            "utf-8",
            "utf-8",
            string.Empty,
            ImmutableArray<string>.Empty,
            ImmutableDictionary<string, string>.Empty,
            JsonSerializer.SerializeToElement(Array.Empty<object>()));
        var setting = new InstanceFactoryConfiguration(
            configuration,
            "../outside.jar",
            SourceType.Core,
            InstanceFactoryMirror.None,
            false);

        var result = setting.ValidateSetting();

        Assert.True(result.IsErr(out var error));
        var validation = Assert.IsType<ValidationDaemonError>(error);
        Assert.Equal("instance.source.invalid", validation.Code);
    }

    [Fact]
    public async Task ValidateSetting_OutsideFileUri_ReturnsValidationError()
    {
        var outsidePath = Path.Combine(Path.GetTempPath(), $"mcsl-outside-{Guid.NewGuid():N}.jar");
        await File.WriteAllTextAsync(outsidePath, "outside");
        var configuration = new InstanceConfiguration(
            Guid.NewGuid(),
            "outside-file-uri",
            "server.exe",
            InstanceType.Universal,
            TargetType.Executable,
            string.Empty,
            "utf-8",
            "utf-8",
            string.Empty,
            ImmutableArray<string>.Empty,
            ImmutableDictionary<string, string>.Empty,
            JsonSerializer.SerializeToElement(Array.Empty<object>()));
        var setting = new InstanceFactoryConfiguration(
            configuration,
            new Uri(outsidePath).AbsoluteUri,
            SourceType.Core,
            InstanceFactoryMirror.None,
            false);

        try
        {
            var result = setting.ValidateSetting();

            Assert.True(result.IsErr(out var error));
            var validation = Assert.IsType<ValidationDaemonError>(error);
            Assert.Equal("instance.source.invalid", validation.Code);
        }
        finally
        {
            if (File.Exists(outsidePath))
                File.Delete(outsidePath);
        }
    }

    [Fact]
    public async Task GetInstanceSettings_MissingInstance_ReturnsNotFoundError()
    {
        var manager = new InstanceManager();

        var result = await manager.GetInstanceSettings(Guid.NewGuid());

        Assert.True(result.IsErr(out var error));
        var notFound = Assert.IsType<NotFoundDaemonError>(error);
        Assert.Equal("instance.not_found", notFound.Code);
    }

    [Fact]
    public void TryGetStartInfo_InvalidTarget_ReturnsInternalError()
    {
        var config = new InstanceConfig
        {
            Uuid = Guid.NewGuid(),
            InstanceType = InstanceType.Universal,
            TargetType = TargetType.Executable,
            JavaPath = "java",
            Target = "../outside.exe"
        };

        var result = config.TryGetStartInfo();

        Assert.True(result.IsErr(out var error));
        var internalError = Assert.IsType<InternalDaemonError>(error);
        Assert.Equal("instance.start_info.failed", internalError.Code);
    }
}
