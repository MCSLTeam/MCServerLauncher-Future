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
    /// <summary>
    ///     是否在线(websocket是否在线)
    /// </summary>
    bool Online { get; }

    /// <summary>
    ///     连接是否丢失
    /// </summary>
    bool IsConnectionLost { get; }

    /// <summary>
    ///     最近一次心跳检查的时间
    /// </summary>
    DateTime LastPong { get; }

    /// <summary>
    ///     已订阅的Daemon事件的集合
    /// </summary>
    SubscribedEvents SubscribedEvents { get; }

    /// <summary>
    ///     事件: 断线重连成功
    /// </summary>
    event Action? Reconnected;

    /// <summary>
    ///     事件: 连接丢失(心跳检查超过固定次数未响应时触发)
    /// </summary>
    event Action? ConnectionLost;

    /// <summary>
    ///     事件: 连接关闭
    /// </summary>
    event Action? ConnectionClosed;

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