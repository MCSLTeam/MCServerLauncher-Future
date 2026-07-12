using Microsoft.Extensions.Logging;
using MCServerLauncher.Common.Contracts.Protocol;

namespace MCServerLauncher.Daemon.Remote.Rpc.Transport;

internal sealed class V2InboundLoggingDiagnosticSink(ILogger<V2InboundLoggingDiagnosticSink> logger)
    : IV2InboundDiagnosticSink
{
    private readonly ILogger<V2InboundLoggingDiagnosticSink> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public void RecordUnexpected(string correlationId, Exception exception) =>
        _logger.LogError(exception, "V2 inbound processing failed. CorrelationId={CorrelationId}", correlationId);

    public void RecordBinaryFault(BinaryFrameReadResult readResult) =>
        _logger.LogWarning("Invalid V2 binary frame. Failure={Failure}", readResult.Error);
}

internal sealed class V2InboundNoOpDiagnosticSink : IV2InboundDiagnosticSink
{
    internal static V2InboundNoOpDiagnosticSink Instance { get; } = new();
    public void RecordUnexpected(string correlationId, Exception exception) { }
    public void RecordBinaryFault(BinaryFrameReadResult readResult) { }
}
