using System.Text.Json;
using System.Text.Json.Serialization;
using MCServerLauncher.Common.ProtoType.EventTrigger;

namespace MCServerLauncher.Daemon.ApplicationCore.Events;

internal static class EventRuleDocumentCodec
{
    public static JsonElement SerializeToElement(IEnumerable<EventRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var document = rules as List<EventRule> ?? rules.ToList();
        return JsonSerializer.SerializeToElement(document, EventRuleDocumentJsonContext.Default.EventRuleList);
    }

    public static List<EventRule>? Deserialize(JsonElement document)
    {
        return JsonSerializer.Deserialize(document, EventRuleDocumentJsonContext.Default.EventRuleList);
    }

    public static List<EventRule> DeserializeRequired(JsonElement document)
    {
        return Deserialize(document) ?? throw new JsonException("Instance event rules must be an array.");
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(List<EventRule>), TypeInfoPropertyName = "EventRuleList")]
internal partial class EventRuleDocumentJsonContext : JsonSerializerContext;
