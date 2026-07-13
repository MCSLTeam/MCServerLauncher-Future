using System.Threading;
using System.Threading.Tasks;
using System.Collections.Immutable;

namespace MCServerLauncher.DaemonClient.Connection.V2;

internal interface IV2ClientWireTransport
{
    ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken);
}

internal enum V2ClientDiagnosticKind
{
    UnknownResponse,
    UnknownNotification,
    ProtocolFault,
    ConsumerFault
}

internal readonly record struct V2ClientDiagnostic(V2ClientDiagnosticKind Kind, string Message);
