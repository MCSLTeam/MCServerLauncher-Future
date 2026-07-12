using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

namespace MCServerLauncher.Daemon.Remote.Rpc.Catalog;

internal static class BuiltInApplicationRpcExecution
{
    public static ProtocolRpcExecution<TResult> FromResult<TResult>(Result<TResult, DaemonError> result)
        where TResult : notnull =>
        result.IsOk(out var value)
            ? ProtocolRpcExecution<TResult>.Ok(value)
            : ProtocolRpcExecution<TResult>.Err(result.UnwrapErr());

    public static ProtocolRpcExecution<UnitResult> FromUnit(Result<Unit, DaemonError> result) =>
        result.IsOk(out _)
            ? ProtocolRpcExecution<UnitResult>.Ok(new UnitResult())
            : ProtocolRpcExecution<UnitResult>.Err(result.UnwrapErr());
}
