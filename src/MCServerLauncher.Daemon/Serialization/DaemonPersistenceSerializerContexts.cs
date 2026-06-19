using System.Text.Json.Serialization;

namespace MCServerLauncher.Daemon.Serialization;

/// <summary>
/// Daemon persistence-specific STJ context placeholder.
/// Intentionally empty in T5; persistence currently relies on Common contexts.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(AppConfig))]
internal partial class DaemonPersistenceSerializerContext : JsonSerializerContext
{
}
