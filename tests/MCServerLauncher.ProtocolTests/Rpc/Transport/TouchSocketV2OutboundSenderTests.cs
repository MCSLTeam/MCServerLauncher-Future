using System.Net.WebSockets;
using System.Reflection;
using MCServerLauncher.Daemon.Remote.Rpc.Transport;
using TouchSocket.Core;
using TouchSocket.Http.WebSockets;

namespace MCServerLauncher.ProtocolTests.Rpc.Transport;

public sealed class TouchSocketV2OutboundSenderTests
{
    [Fact]
    public async Task SendAsync_MapsOpcodePayloadAndCancellation()
    {
        WSDataFrame? sent = null;
        bool? endOfMessage = null;
        CancellationToken observed = default;
        var socket = CreateSocket((method, args) =>
        {
            if (method.Name == nameof(IWebSocket.SendAsync))
            {
                sent = Assert.IsType<WSDataFrame>(args![0]);
                endOfMessage = Assert.IsType<bool>(args[1]);
                observed = Assert.IsType<CancellationToken>(args[2]);
                return Task.FromResult(Result.FromSuccess("sent"));
            }
            return Default(method.ReturnType);
        });
        using var cancellation = new CancellationTokenSource();
        var sender = new TouchSocketV2OutboundSender(socket);

        await sender.SendAsync(V2OutboundFrame.CopyBinary([1, 2, 3]), cancellation.Token);

        Assert.Equal(WSDataType.Binary, sent!.Opcode);
        Assert.Equal(new byte[] { 1, 2, 3 }, sent.PayloadData.ToArray());
        Assert.True(endOfMessage);
        Assert.Equal(cancellation.Token, observed);
    }

    [Theory]
    [InlineData((int)V2ConnectionCloseReason.Graceful, WebSocketCloseStatus.NormalClosure)]
    [InlineData((int)V2ConnectionCloseReason.SlowConsumer, WebSocketCloseStatus.PolicyViolation)]
    [InlineData((int)V2ConnectionCloseReason.SendFailure, WebSocketCloseStatus.InternalServerError)]
    [InlineData((int)V2ConnectionCloseReason.Abort, WebSocketCloseStatus.InternalServerError)]
    public async Task CloseAsync_MapsCloseReason(int reasonValue, WebSocketCloseStatus expected)
    {
        WebSocketCloseStatus? status = null;
        var calls = 0;
        var socket = CreateSocket((method, args) =>
        {
            if (method.Name == nameof(IWebSocket.CloseAsync))
            {
                calls++;
                status = Assert.IsType<WebSocketCloseStatus>(args![0]);
                return Task.FromResult(Result.FromSuccess("closed"));
            }
            return Default(method.ReturnType);
        });
        var sender = new TouchSocketV2OutboundSender(socket);

        await sender.CloseAsync((V2ConnectionCloseReason)reasonValue, CancellationToken.None);
        Assert.Equal(expected, status);
        Assert.Equal(1, calls);

        await sender.CloseAsync(V2ConnectionCloseReason.Peer, CancellationToken.None);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CloseAsync_FailedTouchSocketResultIsNotSilentlyAccepted()
    {
        var socket = CreateSocket((method, _) => method.Name == nameof(IWebSocket.CloseAsync)
            ? Task.FromResult(Result.FromError("physical close failed"))
            : Default(method.ReturnType));
        var sender = new TouchSocketV2OutboundSender(socket);

        var error = await Assert.ThrowsAsync<IOException>(
            () => sender.CloseAsync(V2ConnectionCloseReason.Abort, CancellationToken.None).AsTask());
        Assert.Contains("physical close failed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectionClose_FailedTouchSocketResultIsRetriedAndReported()
    {
        var calls = 0;
        var socket = CreateSocket((method, _) =>
        {
            if (method.Name != nameof(IWebSocket.CloseAsync))
                return Default(method.ReturnType);
            calls++;
            return Task.FromResult(Result.FromError("reject close failed"));
        });

        var error = await Assert.ThrowsAsync<IOException>(() =>
            TouchSocketV2TransportPlugin.CloseDirectAsync(
                socket, WebSocketCloseStatus.PolicyViolation, "invalid token"));

        Assert.Equal(2, calls);
        Assert.Contains("after retry", error.Message, StringComparison.Ordinal);
    }

    private static IWebSocket CreateSocket(Func<MethodInfo, object?[]?, object?> handler)
    {
        var socket = DispatchProxy.Create<IWebSocket, SocketProxy>();
        ((SocketProxy)(object)socket).Handler = handler;
        return socket;
    }

    private static object? Default(Type type) => type == typeof(Task) ? Task.CompletedTask :
        type == typeof(ValueTask) ? ValueTask.CompletedTask : type.IsValueType ? Activator.CreateInstance(type) : null;

    private class SocketProxy : DispatchProxy
    {
        internal Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }
}
