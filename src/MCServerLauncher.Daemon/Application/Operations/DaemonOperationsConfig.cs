using System.Text.Json.Serialization;

namespace MCServerLauncher.Daemon.ApplicationCore.Operations;

/// <summary>
/// Cold-start configuration for daemon-owned retained operation records.
/// </summary>
public sealed class DaemonOperationsConfig
{
    internal const int DefaultRetentionDays = 7;
    internal const long DefaultMaximumBytes = 268_435_456;

    [JsonPropertyName("retention_days")]
    public int RetentionDays { get; set; } = DefaultRetentionDays;

    [JsonPropertyName("maximum_bytes")]
    public long MaximumBytes { get; set; } = DefaultMaximumBytes;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(RetentionDays, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumBytes, 2);
    }
}
