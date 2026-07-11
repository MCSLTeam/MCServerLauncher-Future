using System.Text.Json.Serialization;
using MCServerLauncher.Common.ProtoType.EventTrigger;

namespace MCServerLauncher.Daemon.ApplicationCore.Events;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(List<EventRule>), TypeInfoPropertyName = "EventRuleList")]
internal partial class EventRuleJsonContext : JsonSerializerContext;
