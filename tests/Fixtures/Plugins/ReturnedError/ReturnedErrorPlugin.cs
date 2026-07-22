using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using RustyOptions;

namespace MCServerLauncher.PluginFixtures.ReturnedError;

public sealed class ReturnedErrorPlugin : IGeneratedDaemonPluginAdapter
{
    public static PluginAdapterMetadata Metadata { get; } = new(
        "fixture.returned-error",
        "1.0.0",
        "PluginEntry.dll",
        "MCServerLauncher.PluginFixtures.ReturnedError.ReturnedErrorPlugin",
        "[2.0.0, 3.0.0)",
        ["event.publish", "instance.query", "rpc.register"],
        "3f44a09179669b1a9d61d2130a5c3cc3801f892509615784f3b4fea755d9df75");

    public Result<Unit, DaemonError> Configure(IPluginContext context) =>
        context.Errors.Fail("fixture_returned_error", "The returned-error fixture rejects configuration.");

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}

public abstract class MetadataConstructorProbePlugin
{
    protected MetadataConstructorProbePlugin()
    {
        var sentinelPath = Environment.GetEnvironmentVariable("MCSL_PLUGIN_METADATA_PROBE_PATH");
        if (!string.IsNullOrWhiteSpace(sentinelPath))
            File.WriteAllText(sentinelPath, GetType().FullName ?? GetType().Name);
    }

    public Result<Unit, DaemonError> Configure(IPluginContext context) => PluginResult.Ok();

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}

public sealed class IdentityMetadataMismatchPlugin : MetadataConstructorProbePlugin, IGeneratedDaemonPluginAdapter
{
    public static PluginAdapterMetadata Metadata { get; } = new(
        "fixture.metadata-identity-generated",
        "1.0.0",
        "PluginEntry.dll",
        "MCServerLauncher.PluginFixtures.ReturnedError.IdentityMetadataMismatchPlugin",
        "[2.0.0, 3.0.0)",
        ["event.publish", "instance.query", "rpc.register"],
        new string('0', 64));
}

public sealed class ApiMetadataMismatchPlugin : MetadataConstructorProbePlugin, IGeneratedDaemonPluginAdapter
{
    public static PluginAdapterMetadata Metadata { get; } = new(
        "fixture.metadata-api",
        "1.0.0",
        "PluginEntry.dll",
        "MCServerLauncher.PluginFixtures.ReturnedError.ApiMetadataMismatchPlugin",
        "[1.0.0, 2.0.0)",
        ["event.publish", "instance.query", "rpc.register"],
        new string('0', 64));
}

public sealed class FeatureMetadataMismatchPlugin : MetadataConstructorProbePlugin, IGeneratedDaemonPluginAdapter
{
    public static PluginAdapterMetadata Metadata { get; } = new(
        "fixture.metadata-features",
        "1.0.0",
        "PluginEntry.dll",
        "MCServerLauncher.PluginFixtures.ReturnedError.FeatureMetadataMismatchPlugin",
        "[2.0.0, 3.0.0)",
        ["event.publish", "rpc.register"],
        new string('0', 64));
}

public sealed class DigestMetadataMismatchPlugin : MetadataConstructorProbePlugin, IGeneratedDaemonPluginAdapter
{
    public static PluginAdapterMetadata Metadata { get; } = new(
        "fixture.metadata-digest",
        "1.0.0",
        "PluginEntry.dll",
        "MCServerLauncher.PluginFixtures.ReturnedError.DigestMetadataMismatchPlugin",
        "[2.0.0, 3.0.0)",
        ["event.publish", "instance.query", "rpc.register"],
        new string('0', 64));
}

public sealed class ManualMetadataProbePlugin : MetadataConstructorProbePlugin, IDaemonPlugin
{
}
