using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.DaemonClient.Connection.V2;
using MCServerLauncher.DaemonClient.State;
using TouchSocket.Http.WebSockets;

namespace MCServerLauncher.ProtocolTests.DaemonClient.V2;

public sealed class TouchSocketV2ClientConnectionSessionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly Uri Endpoint = new("ws://127.0.0.1:11452/api/v2");

    [Fact]
    public void Factory_RequiresExactCredentialFreeV2Endpoint()
    {
        _ = new TouchSocketV2ClientConnectionSessionFactory(Endpoint, "token");

        Assert.Throws<ArgumentException>(() =>
            new TouchSocketV2ClientConnectionSessionFactory(
                new Uri("http://127.0.0.1:11452/api/v2"),
                "token"));
        Assert.Throws<ArgumentException>(() =>
            new TouchSocketV2ClientConnectionSessionFactory(
                new Uri("ws://127.0.0.1:11452/api/v1"),
                "token"));
        Assert.Throws<ArgumentException>(() =>
            new TouchSocketV2ClientConnectionSessionFactory(
                new Uri("ws://127.0.0.1:11452/api/v2?token=embedded"),
                "token"));
    }

    [Fact]
    public void AuthenticatedEndpoint_EscapesTokenExactlyOnce()
    {
        var endpoint = TouchSocketV2ClientConnectionSession.BuildAuthenticatedEndpoint(
            Endpoint,
            "space + slash/?% value");

        Assert.Equal(
            "ws://127.0.0.1:11452/api/v2?token=space%20%2B%20slash%2F%3F%25%20value",
            endpoint);
    }

    [Fact]
    public void MessageAssembler_RoutesFragmentedTextAsOneOwnedMessage()
    {
        var assembler = new TouchSocketV2MessageAssembler();
        var first = new WSDataFrame(Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\","))
        {
            Opcode = WSDataType.Text,
            FIN = false
        };
        var last = new WSDataFrame(Encoding.UTF8.GetBytes("\"id\":1,\"result\":{}}"))
        {
            Opcode = WSDataType.Cont,
            FIN = true
        };

        Assert.False(assembler.TryAssemble(first, out _));
        Assert.True(assembler.TryAssemble(last, out var message));
        using (message)
        {
            Assert.Equal(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}",
                Encoding.UTF8.GetString(message.PayloadData.ToArray()));
        }
    }

    [Fact]
    public void MessageAssembler_RejectsUnexpectedOrOversizedDataAndRecovers()
    {
        var assembler = new TouchSocketV2MessageAssembler();
        Assert.Throws<InvalidDataException>(() => assembler.TryAssemble(
            new WSDataFrame(new byte[] { 1 }) { Opcode = WSDataType.Binary, FIN = true },
            out _));
        Assert.Throws<InvalidDataException>(() => assembler.TryAssemble(
            new WSDataFrame(new byte[TouchSocketV2MessageAssembler.MaxTextMessageSize + 1])
            {
                Opcode = WSDataType.Text,
                FIN = true
            },
            out _));

        Assert.True(assembler.TryAssemble(
            new WSDataFrame(Encoding.UTF8.GetBytes("{}"))
            {
                Opcode = WSDataType.Text,
                FIN = true
            },
            out var recovered));
        recovered.Dispose();
    }

    [Theory]
    [InlineData(WSDataType.Binary, "protocol.binary_not_supported")]
    [InlineData(WSDataType.Cont, "protocol.websocket_message_invalid")]
    public async Task UnexpectedData_CompletesSessionWithTypedError(
        WSDataType opcode,
        string expectedCode)
    {
        await using var session = CreateSession();

        await session.HandleFrameAsync(
            new WSDataFrame(new byte[] { 1 }) { Opcode = opcode, FIN = true });

        var error = await session.Completion.WaitAsync(Timeout);
        Assert.Equal(expectedCode, error.Code);
        Assert.Equal(Daemon.API.Errors.DaemonErrorKind.Transport, error.Kind);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 126)]
    public async Task InvalidCloseFrame_CompletesOnceWithProtocolError(
        bool final,
        int payloadLength)
    {
        await using var session = CreateSession();
        var completion = session.Completion;

        await session.HandleFrameAsync(new WSDataFrame(new byte[payloadLength])
        {
            Opcode = WSDataType.Close,
            FIN = final
        });

        var error = await completion.WaitAsync(Timeout);
        Assert.Equal("protocol.websocket_message_invalid", error.Code);
        await session.HandleClosedAsync();
        Assert.Same(completion, session.Completion);
        Assert.Same(error, await session.Completion);
        Assert.NotEqual("transport.peer_closed", error.Code);
    }

    [Fact]
    public async Task ValidCloseFrame_CompletesWithPeerClosed()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = new Uri($"ws://127.0.0.1:{LocalPort(listener)}/api/v2");
        await using var session = CreateSession(endpoint);
        var connecting = session.ConnectAsync(CancellationToken.None);
        using var accepted = await listener.AcceptTcpClientAsync().WaitAsync(Timeout);
        var headers = await ReadHandshakeAsync(accepted);
        await CompleteHandshakeAsync(accepted, headers);
        Assert.True((await connecting.WaitAsync(Timeout)).IsOk(out _));

        await session.HandleFrameAsync(new WSDataFrame(Array.Empty<byte>())
        {
            Opcode = WSDataType.Close,
            FIN = true
        });

        Assert.Equal("transport.peer_closed", (await session.Completion.WaitAsync(Timeout)).Code);
    }

    [Fact]
    public async Task ClosedCallbackDuringHandshake_CannotOverrideConnectFailure()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = new Uri($"ws://127.0.0.1:{LocalPort(listener)}/api/v2");
        await using var session = CreateSession(endpoint);
        var connecting = session.ConnectAsync(CancellationToken.None);
        using var accepted = await listener.AcceptTcpClientAsync().WaitAsync(Timeout);
        _ = await ReadHandshakeAsync(accepted);

        await session.HandleClosedAsync();
        Assert.False(session.Completion.IsCompleted);
        accepted.Client.LingerState = new LingerOption(true, 0);
        accepted.Close();

        var result = await connecting.WaitAsync(Timeout);
        Assert.True(result.IsErr(out var connectError));
        Assert.Equal("transport.connect_failed", connectError!.Code);
        Assert.Same(connectError, await session.Completion.WaitAsync(Timeout));
    }

    [Fact]
    public async Task LocalCloseDuringGenericHandshakeFailure_ReturnsClosedTerminal()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var failureEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var endpoint = new Uri($"ws://127.0.0.1:{LocalPort(listener)}/api/v2");
        await using var session = CreateSession(
            endpoint,
            beforeConnectFailureArbitration: async () =>
            {
                failureEntered.TrySetResult();
                await releaseFailure.Task;
            });
        var connecting = session.ConnectAsync(CancellationToken.None);
        using var accepted = await listener.AcceptTcpClientAsync().WaitAsync(Timeout);
        _ = await ReadHandshakeAsync(accepted);
        accepted.Client.LingerState = new LingerOption(true, 0);
        accepted.Close();
        await failureEntered.Task.WaitAsync(Timeout);

        var closing = session.CloseAsync();
        var completionError = await session.Completion.WaitAsync(Timeout);
        releaseFailure.TrySetResult();
        var connectResult = await connecting.WaitAsync(Timeout);
        await closing.WaitAsync(Timeout);

        Assert.True(connectResult.IsErr(out var connectError));
        Assert.Equal("connection.closed", connectError!.Code);
        Assert.Equal("connection.closed", completionError.Code);
        Assert.NotEqual("transport.connect_failed", connectError.Code);
        Assert.NotEqual("transport.connect_failed", completionError.Code);
    }

    [Fact]
    public async Task CallerCancellationDuringGenericHandshakeFailure_PreservesCallerToken()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var failureEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var endpoint = new Uri($"ws://127.0.0.1:{LocalPort(listener)}/api/v2");
        await using var session = CreateSession(
            endpoint,
            beforeConnectFailureArbitration: async () =>
            {
                failureEntered.TrySetResult();
                await releaseFailure.Task;
            });
        using var cancellation = new CancellationTokenSource();
        var connecting = session.ConnectAsync(cancellation.Token);
        using var accepted = await listener.AcceptTcpClientAsync().WaitAsync(Timeout);
        _ = await ReadHandshakeAsync(accepted);
        accepted.Client.LingerState = new LingerOption(true, 0);
        accepted.Close();
        await failureEntered.Task.WaitAsync(Timeout);

        cancellation.Cancel();
        releaseFailure.TrySetResult();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => connecting.WaitAsync(Timeout));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task MalformedJson_CompletesSessionWithProtocolError()
    {
        await using var session = CreateSession();

        await session.HandleFrameAsync(new WSDataFrame(Encoding.UTF8.GetBytes("{not-json"))
        {
            Opcode = WSDataType.Text,
            FIN = true
        });

        var error = await session.Completion.WaitAsync(Timeout);
        Assert.Equal("protocol.envelope_invalid", error.Code);
    }

    [Fact]
    public async Task ThrowingDiagnostic_CannotPreventProtocolTermination()
    {
        await using var session = CreateSession(
            static _ => throw new InvalidOperationException("consumer failed"));

        await session.HandleFrameAsync(new WSDataFrame(Encoding.UTF8.GetBytes("[]"))
        {
            Opcode = WSDataType.Text,
            FIN = true
        });

        Assert.Equal("protocol.envelope_invalid", (await session.Completion.WaitAsync(Timeout)).Code);
    }

    [Fact]
    public async Task CloseAsync_MakesCompletionTerminalBeforeReturning()
    {
        await using var session = CreateSession();

        await session.CloseAsync().WaitAsync(Timeout);

        Assert.True(session.Completion.IsCompletedSuccessfully);
        Assert.Equal("connection.closed", (await session.Completion).Code);
    }

    [Fact]
    public async Task CallerCancellation_IsNotConvertedToDaemonError()
    {
        await using var session = CreateSession();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => session.ConnectAsync(cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.False(session.Completion.IsCompleted);
    }

    private static TouchSocketV2ClientConnectionSession CreateSession(
        Action<V2ClientDiagnostic>? diagnostic = null) =>
        CreateSession(Endpoint, diagnostic);

    private static TouchSocketV2ClientConnectionSession CreateSession(
        Uri endpoint,
        Action<V2ClientDiagnostic>? diagnostic = null,
        Func<Task>? beforeConnectFailureArbitration = null) =>
        new(
            endpoint,
            "token",
            new RemoteInstanceCatalogMirror(),
            static (_, _) => { },
            TimeProvider.System,
            TimeSpan.FromSeconds(5),
            diagnostic,
            beforeConnectFailureArbitration);

    private static int LocalPort(TcpListener listener) =>
        ((IPEndPoint)listener.LocalEndpoint).Port;

    private static async Task<string> ReadHandshakeAsync(TcpClient client)
    {
        var buffer = new byte[4096];
        var text = new StringBuilder();
        using var timeout = new CancellationTokenSource(Timeout);
        while (!text.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            var read = await client.GetStream().ReadAsync(buffer, timeout.Token);
            if (read == 0)
                throw new IOException("The V2 client closed before sending its WebSocket handshake.");
            text.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }
        return text.ToString();
    }

    private static async Task CompleteHandshakeAsync(TcpClient client, string requestHeaders)
    {
        var key = requestHeaders.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
            .Split(':', 2)[1]
            .Trim();
        var acceptBytes = SHA1.HashData(Encoding.ASCII.GetBytes(
            $"{key}258EAFA5-E914-47DA-95CA-C5AB0DC85B11"));
        var response = "HTTP/1.1 101 Switching Protocols\r\n" +
                       "Connection: Upgrade\r\n" +
                       "Upgrade: websocket\r\n" +
                       $"Sec-WebSocket-Accept: {Convert.ToBase64String(acceptBytes)}\r\n\r\n";
        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(response));
        await client.GetStream().FlushAsync();
    }
}
