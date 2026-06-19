using System.Text.Json;
using System.Text.Json.Serialization;

namespace MCServerLauncher.Common.ProtoType.Relay;

public record RelayPacket
{
    [JsonPropertyName("relay")]
    [JsonRequired]
    public string Relay { get; init; } = string.Empty;

    [JsonPropertyName("target")]
    public string? Target { get; init; }

    [JsonPropertyName("data")]
    [JsonRequired]
    public JsonElement? Data { get; init; }

    [JsonPropertyName("id")]
    public Guid Id { get; init; } = Guid.NewGuid();

    [JsonPropertyName("time")]
    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
