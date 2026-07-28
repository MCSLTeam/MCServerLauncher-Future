using System.Collections.Immutable;
using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.Common.Contracts.Monitoring;

/// <summary>
/// One instance as observed by a metrics sample: identity, lifecycle status, and cached process
/// counters. Sampling never queries the game server itself.
/// </summary>
public sealed record MonitoringInstanceSample(
    Guid InstanceId,
    string Name,
    InstanceStatus Status,
    double CpuPercent,
    long MemoryBytes);

/// <summary>
/// One retained metrics point. A <see cref="Gap" /> record marks a hole in the history (daemon
/// downtime) instead of leaving silence; its metric fields carry no meaning.
/// </summary>
public sealed record MonitoringSample(
    DateTimeOffset Timestamp,
    bool Gap,
    double SystemCpuPercent,
    ulong MemoryUsedKilobytes,
    ulong MemoryTotalKilobytes,
    ImmutableArray<MonitoringInstanceSample> Instances);

/// <summary>
/// The newest sample, or null before the first sampling tick. <see cref="DroppedRecords" />
/// surfaces history records lost to write failures.
/// </summary>
public sealed record MonitoringCurrentResult(MonitoringSample? Sample, long DroppedRecords);

public sealed record MonitoringQuery(
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    int? MaximumPoints = null);

public sealed record MonitoringQueryResult(
    ImmutableArray<MonitoringSample> Samples,
    long DroppedRecords);
