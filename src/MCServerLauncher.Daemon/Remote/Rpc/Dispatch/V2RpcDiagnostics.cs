using Microsoft.Extensions.Logging;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;

namespace MCServerLauncher.Daemon.Remote.Rpc.Dispatch;

internal enum V2RpcNotificationSuppressionReason
{
    InvalidRequest,
    UnknownMethod,
    NotificationNotAllowed,
    PermissionDenied,
    InvalidParams,
    ExpectedDaemonError
}

internal sealed class V2RpcUnexpectedDiagnostic
{
    internal V2RpcUnexpectedDiagnostic(
        string correlationId,
        string method,
        ProtocolExecutionOwner? owner,
        Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(exception);
        CorrelationId = correlationId;
        Method = method;
        Owner = owner;
        Exception = exception;
    }

    internal string CorrelationId { get; }

    internal string Method { get; }

    internal ProtocolExecutionOwner? Owner { get; }

    internal Exception Exception { get; }
}

internal sealed class V2RpcNotificationSuppressionDiagnostic
{
    internal V2RpcNotificationSuppressionDiagnostic(
        string method,
        ProtocolExecutionOwner? owner,
        V2RpcNotificationSuppressionReason reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        Method = method;
        Owner = owner;
        Reason = reason;
    }

    internal string Method { get; }

    internal ProtocolExecutionOwner? Owner { get; }

    internal V2RpcNotificationSuppressionReason Reason { get; }
}

internal interface IV2RpcDiagnosticSink
{
    void RecordUnexpected(V2RpcUnexpectedDiagnostic diagnostic);

    void RecordNotificationSuppressed(V2RpcNotificationSuppressionDiagnostic diagnostic);
}

internal sealed class V2RpcLoggingDiagnosticSink(ILogger<V2RpcLoggingDiagnosticSink> logger)
    : IV2RpcDiagnosticSink
{
    private readonly ILogger<V2RpcLoggingDiagnosticSink> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public void RecordUnexpected(V2RpcUnexpectedDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        _logger.LogError(
            diagnostic.Exception,
            "V2 RPC {Method} failed unexpectedly. CorrelationId={CorrelationId}, OwnerKind={OwnerKind}, OwnerId={OwnerId}, OwnerVersion={OwnerVersion}",
            diagnostic.Method,
            diagnostic.CorrelationId,
            diagnostic.Owner?.Kind,
            diagnostic.Owner?.Plugin?.Id,
            diagnostic.Owner?.Plugin?.Version);
    }

    public void RecordNotificationSuppressed(V2RpcNotificationSuppressionDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        _logger.LogDebug(
            "V2 RPC notification {Method} was suppressed. Reason={Reason}, OwnerKind={OwnerKind}, OwnerId={OwnerId}, OwnerVersion={OwnerVersion}",
            diagnostic.Method,
            diagnostic.Reason,
            diagnostic.Owner?.Kind,
            diagnostic.Owner?.Plugin?.Id,
            diagnostic.Owner?.Plugin?.Version);
    }
}
