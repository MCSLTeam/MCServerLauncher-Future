using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

[assembly: MCServerLauncher.Daemon.API.Plugins.GeneratedDaemonPluginMetadata(
    "fixture.metadata-imposter",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.MetadataImposter.MetadataImposterPlugin",
    "[2.0.0, 3.0.0)",
    "event.publish\ninstance.query\nrpc.register",
    "0000000000000000000000000000000000000000000000000000000000000000")]

namespace MCServerLauncher.Daemon.API.Plugins
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class GeneratedDaemonPluginMetadataAttribute : Attribute
    {
        public GeneratedDaemonPluginMetadataAttribute(
            string packageId,
            string packageVersion,
            string entryAssembly,
            string entryType,
            string apiRange,
            string features,
            string manifestDigest)
        {
            _ = packageId;
            _ = packageVersion;
            _ = entryAssembly;
            _ = entryType;
            _ = apiRange;
            _ = features;
            _ = manifestDigest;
        }
    }
}

namespace MCServerLauncher.PluginFixtures.MetadataImposter
{
    public sealed class MetadataImposterPlugin :
        global::MCServerLauncher.Daemon.API.Plugins.IGeneratedDaemonPluginAdapter
    {
        public Result<Unit, DaemonError> Configure(
            global::MCServerLauncher.Daemon.API.Plugins.IPluginContext context) =>
            global::MCServerLauncher.Daemon.API.Plugins.PluginResult.Ok();

        public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken) =>
            Task.FromResult(global::MCServerLauncher.Daemon.API.Plugins.PluginResult.Ok());

        public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
            Task.FromResult(global::MCServerLauncher.Daemon.API.Plugins.PluginResult.Ok());
    }
}
