using System.Text.Json.Serialization;

namespace MCServerLauncher.Daemon.ApplicationCore.Backups;

/// <summary>
/// Cold-start configuration for daemon-owned cold backup archives and their retention caps.
/// </summary>
public sealed class DaemonBackupConfig
{
    internal const int DefaultRetentionDays = 30;
    internal const int DefaultMaximumCount = 16;
    internal const long DefaultMaximumBytes = 8_589_934_592;

    [JsonPropertyName("retention_days")]
    public int RetentionDays { get; set; } = DefaultRetentionDays;

    [JsonPropertyName("maximum_count")]
    public int MaximumCount { get; set; } = DefaultMaximumCount;

    [JsonPropertyName("maximum_bytes")]
    public long MaximumBytes { get; set; } = DefaultMaximumBytes;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(RetentionDays, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumBytes, 1);
    }
}
