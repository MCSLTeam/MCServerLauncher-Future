using System.Text.Json.Serialization;

namespace MCServerLauncher.Daemon.ApplicationCore.Monitoring;

/// <summary>
/// Cold-start configuration for the bounded daemon metrics history.
/// </summary>
public sealed class DaemonMonitoringConfig
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
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumBytes, 1024);
    }
}
