using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using RustyOptions;

[assembly: GeneratedDaemonPluginMetadata(
    "fixture.returned-error",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.ReturnedError.ReturnedErrorPlugin",
    "[2.0.0, 3.0.0)",
    "event.publish\ninstance.query\nrpc.register",
    "3f44a09179669b1a9d61d2130a5c3cc3801f892509615784f3b4fea755d9df75")]
[assembly: GeneratedDaemonPluginMetadata(
    "fixture.metadata-identity-generated",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.ReturnedError.IdentityMetadataMismatchPlugin",
    "[2.0.0, 3.0.0)",
    "event.publish\ninstance.query\nrpc.register",
    "0000000000000000000000000000000000000000000000000000000000000000")]
[assembly: GeneratedDaemonPluginMetadata(
    "fixture.metadata-api",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.ReturnedError.ApiMetadataMismatchPlugin",
    "[1.0.0, 2.0.0)",
    "event.publish\ninstance.query\nrpc.register",
    "0000000000000000000000000000000000000000000000000000000000000000")]
[assembly: GeneratedDaemonPluginMetadata(
    "fixture.metadata-features",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.ReturnedError.FeatureMetadataMismatchPlugin",
    "[2.0.0, 3.0.0)",
    "event.publish\nrpc.register",
    "0000000000000000000000000000000000000000000000000000000000000000")]
[assembly: GeneratedDaemonPluginMetadata(
    "fixture.metadata-digest",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.ReturnedError.DigestMetadataMismatchPlugin",
    "[2.0.0, 3.0.0)",
    "event.publish\ninstance.query\nrpc.register",
    "0000000000000000000000000000000000000000000000000000000000000000")]
[assembly: GeneratedDaemonPluginMetadata(
    "fixture.metadata-duplicate",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.ReturnedError.DuplicateMetadataPlugin",
    "[2.0.0, 3.0.0)",
    "event.publish\ninstance.query\nrpc.register",
    "1111111111111111111111111111111111111111111111111111111111111111")]
[assembly: GeneratedDaemonPluginMetadata(
    "fixture.metadata-duplicate",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.ReturnedError.DuplicateMetadataPlugin",
    "[2.0.0, 3.0.0)",
    "event.publish\ninstance.query\nrpc.register",
    "0000000000000000000000000000000000000000000000000000000000000000")]

namespace MCServerLauncher.PluginFixtures.ReturnedError;

public sealed class ReturnedErrorPlugin : IGeneratedDaemonPluginAdapter
{
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
    static IdentityMetadataMismatchPlugin() => WriteSentinel("type-initializer");

    public static string Metadata
    {
        get
        {
            WriteSentinel("static-getter");
            return "must-not-run";
        }
    }

    private static void WriteSentinel(string value)
    {
        var sentinelPath = Environment.GetEnvironmentVariable("MCSL_PLUGIN_METADATA_PROBE_PATH");
        if (!string.IsNullOrWhiteSpace(sentinelPath))
            File.WriteAllText(sentinelPath, value);
    }
}

public sealed class ApiMetadataMismatchPlugin : MetadataConstructorProbePlugin, IGeneratedDaemonPluginAdapter
{
}

public sealed class FeatureMetadataMismatchPlugin : MetadataConstructorProbePlugin, IGeneratedDaemonPluginAdapter
{
}

public sealed class DigestMetadataMismatchPlugin : MetadataConstructorProbePlugin, IGeneratedDaemonPluginAdapter
{
}

public sealed class DuplicateMetadataPlugin : MetadataConstructorProbePlugin, IGeneratedDaemonPluginAdapter
{
}

public sealed class ManualMetadataProbePlugin : MetadataConstructorProbePlugin, IDaemonPlugin
{
}
