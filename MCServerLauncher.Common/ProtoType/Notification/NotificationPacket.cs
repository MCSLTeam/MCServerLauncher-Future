using System.Text.Json.Serialization;

namespace MCServerLauncher.Common.ProtoType.Notification;

public record NotificationPacket
{
    [JsonPropertyName("notification")]
    [JsonRequired]
    public string Notification { get; init; } = "client";

    [JsonPropertyName("title")]
    [JsonRequired]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    [JsonRequired]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("severity")]
    [JsonRequired]
    public string Severity { get; init; } = "Info";

    [JsonPropertyName("source_instance_id")]
    public Guid? SourceInstanceId { get; init; }

    [JsonPropertyName("rule_id")]
    public Guid? RuleId { get; init; }

    [JsonPropertyName("time")]
    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
