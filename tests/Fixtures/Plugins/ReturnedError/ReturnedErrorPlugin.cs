using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using RustyOptions;

[assembly: GeneratedDaemonPluginMetadata(
    "fixture.returned-error",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.ReturnedError.ReturnedErrorPlugin",
        "[1.0.0, 2.0.0)",
        "event.publish\ninstance.query\nrpc.register",
        "92f76771e2ace82456c00a4eef75e2ae34c7f9dcb7df26071c659916da7f5144")]
[assembly: GeneratedDaemonPluginMetadata(
    "fixture.metadata-identity-generated",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.ReturnedError.IdentityMetadataMismatchPlugin",
        "[1.0.0, 2.0.0)",
        "event.publish\ninstance.query\nrpc.register",
        "bd092b09fb111f8ef6321ac20fb7f1532f5f2f519dfc983bc591a16ea4710dd3")]
[assembly: GeneratedDaemonPluginMetadata(
    "fixture.metadata-api",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.ReturnedError.ApiMetadataMismatchPlugin",
    "[1.1.0, 2.0.0)",
        "event.publish\ninstance.query\nrpc.register",
        "4aaacf7d0883e55c750bf25a092cbb8074fc4955797396425d4331035e593792")]
[assembly: GeneratedDaemonPluginMetadata(
    "fixture.metadata-features",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.ReturnedError.FeatureMetadataMismatchPlugin",
        "[1.0.0, 2.0.0)",
        "event.publish\nrpc.register",
        "b578ad0ef8d55eb439a18ac9f572305dc7b98ea808b4a620c12c017d4dfc1a3a")]
[assembly: GeneratedDaemonPluginMetadata(
    "fixture.metadata-digest",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.ReturnedError.DigestMetadataMismatchPlugin",
        "[1.0.0, 2.0.0)",
        "event.publish\ninstance.query\nrpc.register",
        "7448c66bba12547767b38bcd750f94b3bee2ff31508d00e3eee19f3699267c6b")]
[assembly: GeneratedDaemonPluginMetadata(
    "fixture.metadata-duplicate",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.ReturnedError.DuplicateMetadataPlugin",
        "[1.0.0, 2.0.0)",
        "event.publish\ninstance.query\nrpc.register",
        "b36d7222974737e0e38a4cf01d70d2a848da4d19144e3d26b16df0fbf4cf6e87")]
[assembly: GeneratedDaemonPluginMetadata(
    "fixture.metadata-duplicate",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.ReturnedError.DuplicateMetadataPlugin",
        "[1.0.0, 2.0.0)",
        "event.publish\ninstance.query\nrpc.register",
        "c36d7222974737e0e38a4cf01d70d2a848da4d19144e3d26b16df0fbf4cf6e87")]

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
