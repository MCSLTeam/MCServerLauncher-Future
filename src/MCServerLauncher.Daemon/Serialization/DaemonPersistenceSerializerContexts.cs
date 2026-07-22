using System.Text.Json.Serialization;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.ApplicationCore.Operations;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Plugins.Configuration;

namespace MCServerLauncher.Daemon.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(DaemonSecurityConfig))]
[JsonSerializable(typeof(DaemonPluginsConfig))]
[JsonSerializable(typeof(DaemonOperationsConfig))]
[JsonSerializable(typeof(PluginStorageConfig))]
[JsonSerializable(typeof(PluginEntryConfig))]
[JsonSerializable(typeof(PluginAdmissionConfig))]
[JsonSerializable(typeof(InstanceConfig))]
[JsonSerializable(typeof(InstanceInstallMetadataDocument))]
[JsonSerializable(typeof(List<EventRule>))]
internal partial class DaemonPersistenceSerializerContext : JsonSerializerContext
{
}
