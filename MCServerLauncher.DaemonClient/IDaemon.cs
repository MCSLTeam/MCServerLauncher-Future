using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.DaemonClient.Connection;

namespace MCServerLauncher.DaemonClient;

/// <summary>
///     Daemon Rpc Interface
/// </summary>
public interface IDaemon
{
    WebSocketState State { get; }
    bool PingLost { get; }
    DateTime LastPing { get; }
    ClientConnection? Connection { get; }

    event Action<Guid, string>? InstanceLogEvent;

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