using System;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Status;

namespace MCServerLauncher.DaemonClient;

/// <summary>
///     Daemon Rpc Interface
/// </summary>
public interface IDaemon : IDisposable
{
    bool Online { get; }
    bool PingLost { get; }
    DateTime LastPing { get; }
    SubscribedEvents SubscribedEvents { get; }

    /// <summary>
    ///     Instance Log Event(InstanceId, Text)
    /// </summary>
    event Action<Guid, string>? InstanceLogEvent;

    /// <summary>
    ///     Daemon Report Event(Report, Latency ms)
    /// </summary>
    event Action<DaemonReport, long>? DaemonReportEvent;

    Task RequestAsync(
        ActionType actionType,
        IActionParameter? parameter,
        int timeout = -1,
        CancellationToken cancellationToken = default
    );

    Task<TResult> RequestAsync<TResult>(
        ActionType actionType,
        IActionParameter? parameter,
        int timeout = -1,
        CancellationToken cancellationToken = default
    ) where TResult : class, IActionResult;

    /// <summary>
    ///     关闭连接
    /// </summary>
    /// <returns></returns>
    Task CloseAsync();
}