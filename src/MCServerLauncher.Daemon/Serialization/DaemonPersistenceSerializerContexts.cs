using System.Text.Json.Serialization;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management;

namespace MCServerLauncher.Daemon.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(InstanceConfig))]
[JsonSerializable(typeof(InstanceInstallMetadataDocument))]
[JsonSerializable(typeof(List<EventRule>))]
internal partial class DaemonPersistenceSerializerContext : JsonSerializerContext
{
}
