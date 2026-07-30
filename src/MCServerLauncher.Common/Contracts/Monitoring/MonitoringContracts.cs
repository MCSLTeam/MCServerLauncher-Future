using System.Collections.Immutable;
using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.Common.Contracts.Monitoring;

/// <summary>
/// What a recorded significant event was. Lifecycle only: full console logs stay out of the
/// metrics history, so log content never becomes an event.
/// </summary>
public enum MonitoringEventKind
{
    StatusChanged = 0,
    ReadyTimeout = 1,
}

/// <summary>
/// One significant instance observation between two sampling ticks. <see cref="PreviousStatus" />
/// is null for a <see cref="MonitoringEventKind.ReadyTimeout" />, which reports that an instance
/// overran its ready deadline without moving it out of <see cref="InstanceStatus.Starting" />.
/// </summary>
/// <remarks>
/// <see cref="At" /> is when the transition actually happened, which is not the timestamp of the
/// sample carrying it: events are recorded where the catalog commits them and persisted by the next
/// sample, so several can share one sample and each keeps its own instant. It is null on a record
/// written before the field existed, and on one whose event was derived by comparing samples rather
/// than observed at its commit.
/// </remarks>
public sealed record MonitoringInstanceEvent(
    Guid InstanceId,
    MonitoringEventKind Kind,
    InstanceStatus Status,
    InstanceStatus? PreviousStatus,
    DateTimeOffset? At = null);

/// <summary>
/// One instance as observed by a metrics sample: identity, lifecycle status, and cached process
/// counters. Sampling never queries the game server itself.
/// <para>
/// <see cref="SilentSeconds" /> is the passive responsiveness signal: how long the instance has
/// gone without producing an output line. It is null whenever it was not measured — a record
/// written before the field existed, an instance that is not Running (silence means nothing for a
/// stopped process), or a running instance that has produced no output to date. Null is "not
/// collected", never "responded just now".
/// </para>
/// </summary>
public sealed record MonitoringInstanceSample(
    Guid InstanceId,
    string Name,
    InstanceStatus Status,
    double CpuPercent,
    long MemoryBytes,
    double? SilentSeconds = null);

/// <summary>
/// One retained metrics point. A <see cref="Gap" /> record marks a hole in the history (daemon
/// downtime) instead of leaving silence; its metric fields carry no meaning.
/// <para>
/// The fields added after the first release are nullable and default to null on purpose. A record
/// written before they existed has no value for them, and a reader that judges a sustained
/// condition must be able to tell "never measured" from a measured zero — a zero-byte free disk
/// and an unmeasured disk are opposite verdicts. Gap records carry null for the same reason.
/// </para>
/// </summary>
public sealed record MonitoringSample(
    DateTimeOffset Timestamp,
    bool Gap,
    double SystemCpuPercent,
    ulong MemoryUsedKilobytes,
    ulong MemoryTotalKilobytes,
    ImmutableArray<MonitoringInstanceSample> Instances,
    ulong? DiskTotalBytes = null,
    ulong? DiskFreeBytes = null,
    ImmutableArray<MonitoringInstanceEvent>? Events = null,
    // Non-zero means lifecycle transitions occurred that this sample's Events could not carry,
    // because the pending buffer was full. Null is "the question was never asked", the same
    // distinction the metric fields above draw.
    int? DroppedEvents = null);

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
