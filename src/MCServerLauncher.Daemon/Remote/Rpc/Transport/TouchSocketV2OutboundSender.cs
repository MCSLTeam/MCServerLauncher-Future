using System.Net.WebSockets;
using TouchSocket.Http.WebSockets;

namespace MCServerLauncher.Daemon.Remote.Rpc.Transport;

internal sealed class TouchSocketV2OutboundSender(IWebSocket webSocket) : IV2OutboundSender
{
    private readonly IWebSocket _webSocket = webSocket ?? throw new ArgumentNullException(nameof(webSocket));

    public async ValueTask SendAsync(V2OutboundFrame frame, CancellationToken cancellationToken)
    {
        var dataFrame = new WSDataFrame(frame.Payload.AsMemory())
        {
            Opcode = frame.Kind == V2OutboundFrameKind.Text ? WSDataType.Text : WSDataType.Binary
        };
        await _webSocket.SendAsync(dataFrame, true, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask CloseAsync(V2ConnectionCloseReason reason, CancellationToken cancellationToken)
    {
        if (reason == V2ConnectionCloseReason.Peer)
            return;

        var (status, description) = reason switch
        {
            V2ConnectionCloseReason.Graceful => (WebSocketCloseStatus.NormalClosure, "V2 connection completed"),
            V2ConnectionCloseReason.SlowConsumer => (WebSocketCloseStatus.PolicyViolation, "V2 slow consumer"),
            V2ConnectionCloseReason.SendFailure => (WebSocketCloseStatus.InternalServerError, "V2 send failure"),
            V2ConnectionCloseReason.Abort => (WebSocketCloseStatus.InternalServerError, "V2 connection aborted"),
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
        };
        var result = await _webSocket.CloseAsync(status, description, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
            throw new IOException($"TouchSocket WebSocket close failed: {result.Message}");
    }
}
