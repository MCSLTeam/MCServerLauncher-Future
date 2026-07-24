using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Communicate;

namespace MCServerLauncher.Daemon.Management;

public interface IInstance : IDisposable
{
    InstanceConfig Config { get; }
    InstanceProcess? Process { get; }
    InstanceStatus Status { get; }
    bool ReadyTimedOut => false;
    int ServerProcessId { get; }

    event Func<Guid, string, CancellationToken, Task>? OnLog;
    event Func<Guid, InstanceStatus, CancellationToken, Task>? OnStatusChanged;

    Task<InstanceReport> GetReportAsync(CancellationToken ct = default);

    Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default);

    Task<bool> StopAsync(CancellationToken ct = default);

    /// <summary>
    /// Hard-kill the managed process and clear the process handle for a clean restart.
    /// </summary>
    Task ForceKillAndClearAsync(CancellationToken ct = default);

    IReadOnlyList<string> GetLogHistory();
}

internal readonly record struct InstanceReportFact(
    InstanceStatus Status,
    bool ReadyTimedOut);

internal interface IInstanceReportFactSource
{
    event Func<IInstance, InstanceReportFact, CancellationToken, Task>? ReportFactChanged;
}

internal interface IInstanceProcessGenerationSource
{
    long CurrentProcessGeneration { get; }

    event Func<IInstance, long, string, CancellationToken, Task>? ProcessLogReceived;

    event Func<IInstance, long, InstanceStatus, CancellationToken, Task>? ProcessStatusChanged;

    event Func<IInstance, long, InstanceReportFact, CancellationToken, Task>? ProcessReportFactChanged;
}
