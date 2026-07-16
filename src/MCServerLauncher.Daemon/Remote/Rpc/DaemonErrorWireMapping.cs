using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;

namespace MCServerLauncher.Daemon.Remote.Rpc;

internal static class DaemonErrorWireMapping
{
    internal static DaemonErrorWireKind ToWireKind(DaemonErrorKind kind) => kind switch
    {
        DaemonErrorKind.Validation => DaemonErrorWireKind.Validation,
        DaemonErrorKind.NotFound => DaemonErrorWireKind.NotFound,
        DaemonErrorKind.Conflict => DaemonErrorWireKind.Conflict,
        DaemonErrorKind.Permission => DaemonErrorWireKind.Permission,
        DaemonErrorKind.Storage => DaemonErrorWireKind.Storage,
        DaemonErrorKind.Transport => DaemonErrorWireKind.Transport,
        DaemonErrorKind.Internal => DaemonErrorWireKind.Internal,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
