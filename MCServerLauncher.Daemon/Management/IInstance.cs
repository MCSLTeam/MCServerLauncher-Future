using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Communicate;

namespace MCServerLauncher.Daemon.Management;

public interface IInstance : IDisposable
{
    InstanceConfig Config { get; }
    InstanceProcess? Process { get; }
    InstanceStatus Status { get; }
    int ServerProcessId { get; }

    event Action<Guid, string>? OnLog;
    event Action<Guid, InstanceStatus>? OnStatusChanged;

    Task<InstanceReport> GetReportAsync(CancellationToken ct = default);

    Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default);

    void Stop();

    IReadOnlyList<string> GetLogHistory();
}
