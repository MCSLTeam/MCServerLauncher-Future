using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using Microsoft.Extensions.Logging.Abstractions;
using TouchSocket.Http;
using TouchSocket.Http.WebSockets;
using TouchSocket.Sockets;
using DaemonApplication = MCServerLauncher.Daemon.Application;

namespace MCServerLauncher.ProtocolTests;

[Collection(ApplicationHttpServiceTestCollection.Name)]
public sealed class EventTriggerNotificationCharacterizationTests
{
    [Fact]
    [Trait("Category", "EventTriggerNotificationCharacterization")]
    public async Task SendNotificationProducer_ProducesExpectedTextFramePayload()
    {
        var httpServiceProperty = typeof(DaemonApplication).GetProperty(
                                      nameof(DaemonApplication.HttpService),
                                      BindingFlags.Public | BindingFlags.Static)
                                  ?? throw new MissingMemberException(
                                      typeof(DaemonApplication).FullName,
                                      nameof(DaemonApplication.HttpService));
        var originalHttpService = (HttpService?)httpServiceProperty.GetValue(null);
        var httpService = new HttpService();
        CapturingClient? capture = null;

        try
        {
            capture = RegisterCapturingClient(httpService, "notification-client");
            httpServiceProperty.SetValue(null, httpService);

            var contexts = new WsContextContainer();
            contexts.CreateContext(
                capture.ClientId,
                Guid.NewGuid(),
                "*",
                DateTime.UtcNow.AddMinutes(5));

            var instanceId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            var ruleId = Guid.Parse("11111111-2222-3333-4444-555555555555");
            var port = new DomainEventPort(NullLogger<DomainEventPort>.Instance);
            using var adapter = new LegacyDomainEventAdapter(
                port,
                new EventService(),
                contexts,
                NullLogger<LegacyDomainEventAdapter>.Instance);
            var notification = new ClientNotificationDomainEvent(
                "Backup complete",
                "World data is safe.",
                "Success",
                instanceId,
                ruleId,
                1_700_000_000_000);

            port.Publish(notification);

            var frame = await capture.ReadFrameAsync();
            Assert.True(frame.Fin);
            Assert.Equal(WSDataType.Text, frame.Opcode);

            using var payload = JsonDocument.Parse(frame.Payload);
            var root = payload.RootElement;
            Assert.Equal("client", root.GetProperty("notification").GetString());
            Assert.Equal(notification.Title, root.GetProperty("title").GetString());
            Assert.Equal(notification.Message, root.GetProperty("message").GetString());
            Assert.Equal(notification.Severity, root.GetProperty("severity").GetString());
            Assert.Equal(instanceId, root.GetProperty("source_instance_id").GetGuid());
            Assert.Equal(ruleId, root.GetProperty("rule_id").GetGuid());
            Assert.Equal(notification.Timestamp, root.GetProperty("time").GetInt64());
        }
        finally
        {
            httpServiceProperty.SetValue(null, originalHttpService);
            if (capture is not null)
            {
                try
                {
                    RemoveCapturingClient(httpService, capture.ClientId);
                }
                finally
                {
                    await capture.DisposeAsync();
                }
            }
        }
    }

    private static CapturingClient RegisterCapturingClient(HttpService httpService, string clientId)
    {
        var pipe = new Pipe();
        var writeLocker = new SemaphoreSlim(1, 1);
        var transportType = typeof(TcpSessionClientBase).Assembly.GetType(
            "TouchSocket.Sockets.TcpTransport",
            throwOnError: true)!;
        // The producer reaches IWebSocket through TouchSocket's concrete server client. Build only
        // the writer-side transport state needed to capture its real WSDataFrame without opening a port.
        var transport = RuntimeHelpers.GetUninitializedObject(transportType);
        SetPrivateField(transportType, transport, "m_writer", pipe.Writer);
        SetPrivateField(
            transportType.BaseType ?? throw new InvalidOperationException("TcpTransport base type was missing."),
            transport,
            "m_writeLocker",
            writeLocker);
        var client = new CapturingHttpSessionClient();

        SetPrivateField(typeof(TcpSessionClientBase), client, "m_id", clientId);
        SetPrivateField(typeof(TcpSessionClientBase), client, "m_transport", transport);

        var internalWebSocketType = typeof(IWebSocket).Assembly.GetType(
            "TouchSocket.Http.WebSockets.InternalWebSocket",
            throwOnError: true)!;
        var constructor = internalWebSocketType.GetConstructor(
                              BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                              binder: null,
                              [typeof(HttpSessionClient)],
                              modifiers: null)
                          ?? throw new MissingMethodException(
                              internalWebSocketType.FullName,
                              ".ctor(HttpSessionClient)");
        var webSocket = constructor.Invoke([client]);
        SetPrivateField(typeof(HttpSessionClient), client, "m_webSocket", webSocket);

        var clients = httpService.Clients;
        var tryAdd = clients.GetType().GetMethod(
                         "TryAdd",
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(clients.GetType().FullName, "TryAdd");
        Assert.True((bool)tryAdd.Invoke(clients, [client])!);

        return new CapturingClient(clientId, pipe, writeLocker);
    }

    private static void RemoveCapturingClient(HttpService httpService, string clientId)
    {
        var clients = httpService.Clients;
        var tryRemove = clients.GetType().GetMethod(
                            "TryRemoveClient",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?? throw new MissingMethodException(clients.GetType().FullName, "TryRemoveClient");
        var arguments = new object?[] { clientId, null };

        Assert.True((bool)tryRemove.Invoke(clients, arguments)!);
    }

    private static void SetPrivateField(Type declaringType, object target, string fieldName, object value)
    {
        var field = declaringType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new MissingFieldException(declaringType.FullName, fieldName);
        field.SetValue(target, value);
    }

    private static T CreateProxy<T>(Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, InterfaceDispatchProxy>();
        ((InterfaceDispatchProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private static object? GetDefaultReturnValue(Type returnType)
    {
        if (returnType == typeof(void))
            return null;
        if (returnType == typeof(Task))
            return Task.CompletedTask;
        if (returnType == typeof(ValueTask))
            return ValueTask.CompletedTask;

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var resultValue = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
            return typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, [resultValue]);
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var resultValue = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
            return Activator.CreateInstance(returnType, resultValue);
        }

        return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
    }

    private static CapturedWebSocketFrame DecodeFrame(ReadOnlySpan<byte> frame)
    {
        Assert.True(frame.Length >= 2);

        var fin = (frame[0] & 0x80) != 0;
        var opcode = (WSDataType)(frame[0] & 0x0f);
        var masked = (frame[1] & 0x80) != 0;
        Assert.False(masked);

        ulong payloadLength = (uint)(frame[1] & 0x7f);
        var payloadOffset = 2;
        if (payloadLength == 126)
        {
            Assert.True(frame.Length >= 4);
            payloadLength = BinaryPrimitives.ReadUInt16BigEndian(frame[2..4]);
            payloadOffset = 4;
        }
        else if (payloadLength == 127)
        {
            Assert.True(frame.Length >= 10);
            payloadLength = BinaryPrimitives.ReadUInt64BigEndian(frame[2..10]);
            payloadOffset = 10;
        }

        Assert.True(payloadLength <= int.MaxValue);
        Assert.Equal(payloadOffset + (int)payloadLength, frame.Length);
        return new CapturedWebSocketFrame(
            Encoding.UTF8.GetString(frame.Slice(payloadOffset, (int)payloadLength)),
            opcode,
            fin);
    }

    private sealed class CapturingHttpSessionClient : HttpSessionClient;

    private sealed class CapturingClient(
        string clientId,
        Pipe pipe,
        SemaphoreSlim writeLocker) : IAsyncDisposable
    {
        public string ClientId { get; } = clientId;

        public async Task<CapturedWebSocketFrame> ReadFrameAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var readResult = await pipe.Reader.ReadAsync(timeout.Token);
            var buffer = readResult.Buffer;
            try
            {
                var bytes = new byte[checked((int)buffer.Length)];
                buffer.CopyTo(bytes);
                return DecodeFrame(bytes);
            }
            finally
            {
                pipe.Reader.AdvanceTo(buffer.End);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await pipe.Writer.CompleteAsync();
            await pipe.Reader.CompleteAsync();
            writeLocker.Dispose();
        }
    }

    private readonly record struct CapturedWebSocketFrame(string Payload, WSDataType Opcode, bool Fin);

    private class InterfaceDispatchProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = (_, _) => null;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return Handler(
                targetMethod ?? throw new MissingMethodException("DispatchProxy target method was null."),
                args);
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ApplicationHttpServiceTestCollection
{
    public const string Name = "Application.HttpService isolated tests";
}
