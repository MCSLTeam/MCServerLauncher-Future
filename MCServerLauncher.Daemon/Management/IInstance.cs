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

    Task<InstanceReport> GetReportAsync();

    Task<bool> StartAsync(int delayToCheck = 500);

    void Stop();
}