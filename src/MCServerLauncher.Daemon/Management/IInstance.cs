using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Communicate;

namespace MCServerLauncher.Daemon.Management;

public interface IInstance : IDisposable
{
    InstanceConfig Config { get; }
    InstanceProcess? Process { get; }
    InstanceStatus Status { get; }
    int ServerProcessId { get; }

    event Func<Guid, string, CancellationToken, Task>? OnLog;
    event Func<Guid, InstanceStatus, CancellationToken, Task>? OnStatusChanged;

    Task<InstanceReport> GetReportAsync(CancellationToken ct = default);

    Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default);

    void Stop();

    /// <summary>
    /// Hard-kill the managed process and clear the process handle for a clean restart.
    /// </summary>
    void ForceKillAndClear();

    IReadOnlyList<string> GetLogHistory();
}
