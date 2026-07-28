using System.Text.Json.Serialization;

namespace MCServerLauncher.Daemon.ApplicationCore.Audit;

/// <summary>
/// Cold-start configuration for the bounded daemon audit history.
/// </summary>
public sealed class DaemonAuditConfig
{
    internal const int DefaultRetentionDays = 30;
    internal const long DefaultMaximumBytes = 67_108_864;

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
