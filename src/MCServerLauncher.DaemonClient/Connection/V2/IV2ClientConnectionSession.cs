using System;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.DaemonClient.State;
using RustyOptions;

namespace MCServerLauncher.DaemonClient.Connection.V2;

/// <summary>
/// Couples one authenticated physical transport session to its V2 coordinator.
/// </summary>
internal interface IV2ClientConnectionSession : IAsyncDisposable
{
    /// <summary>
    /// Gets the stable, non-throwing coordinator for this physical session.
    /// </summary>
    V2ClientConnectionCoordinator Coordinator { get; }

    Task<Result<Unit, DaemonError>> ConnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Completes exactly once when the physical session terminates. Implementations must make
    /// this task terminal before <see cref="CloseAsync"/> completes.
    /// </summary>
    Task<DaemonError> Completion { get; }

    /// <summary>
    /// Stops the physical session and converges any in-flight <see cref="ConnectAsync"/> call.
    /// </summary>
    Task CloseAsync();
}

internal interface IV2ClientConnectionSessionFactory
{
    IV2ClientConnectionSession Create(
        RemoteInstanceCatalogMirror mirror,
        Action<V2ClientConnectionCoordinator, JsonRpcRemoteEventNotification> routeEvent,
        Action<V2ClientDiagnostic>? diagnostic = null);
}
