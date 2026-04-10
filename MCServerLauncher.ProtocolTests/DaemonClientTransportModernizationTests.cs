using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.DaemonClient.Connection;
using MCServerLauncher.DaemonClient.Serialization;
using StjJsonSerializer = System.Text.Json.JsonSerializer;

namespace MCServerLauncher.ProtocolTests;

public class DaemonClientTransportModernizationTests
{
    [Fact]
    [Trait("Category", "DaemonClientTransportModernization")]
    public async Task SendAndRequestFacades_WhenSocketIsOffline_RejectImmediately()
    {
        var connection = CreateOfflineConnection();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await connection.SendAsync(ActionType.Ping, null, CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await connection.RequestAsync<PingResult>(ActionType.Ping, null, timeout: 250, ct: CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "DaemonClientTransportModernization")]
    public void SendFacade_UsesCurrentActionRequestTransportShape_ForConcreteParameters()
    {
        var instanceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var request = new ActionRequest
        {
            ActionType = ActionType.SubscribeEvent,
            Parameter = InvokePrivateSerializeParameterForTransport(new SubscribeEventParameter
            {
                Type = EventType.InstanceLog,
                Meta = StjJsonSerializer.SerializeToElement(new InstanceLogEventMeta { InstanceId = instanceId }, DaemonClientRpcJsonBoundary.StjOptions)
            }),
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111")
        };

        var payload = ParseJsonElement(Encoding.UTF8.GetString(ClientConnection.SerializeActionRequestForTransport(request)));

        Assert.Equal("subscribe_event", payload.GetProperty("action").GetString());
        Assert.Equal(request.Id, payload.GetProperty("id").GetGuid());

        var parameters = payload.GetProperty("params");
        Assert.Equal("instance_log", parameters.GetProperty("type").GetString());
        Assert.Equal(instanceId, parameters.GetProperty("meta").GetProperty("instance_id").GetGuid());
    }

    [Fact]
    [Trait("Category", "DaemonClientTransportModernization")]
    public void SendFacade_UsesCurrentActionRequestTransportShape_ForNullMetaParameters()
    {
        var request = new ActionRequest
        {
            ActionType = ActionType.SubscribeEvent,
            Parameter = InvokePrivateSerializeParameterForTransport(new SubscribeEventParameter
            {
                Type = EventType.InstanceLog,
                Meta = null
            }),
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111")
        };

        var payload = ParseJsonElement(Encoding.UTF8.GetString(ClientConnection.SerializeActionRequestForTransport(request)));

        Assert.Equal("subscribe_event", payload.GetProperty("action").GetString());
        Assert.Equal(request.Id, payload.GetProperty("id").GetGuid());

        var parameters = payload.GetProperty("params");
        Assert.Equal("instance_log", parameters.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, parameters.GetProperty("meta").ValueKind);
    }

    [Fact]
    [Trait("Category", "DaemonClientTransportModernization")]
    public void TransportConfig_WhenIsSecureFalse_UsesWsEndpoint()
    {
        var endpoint = InvokeBuildServerEndpoint("127.0.0.1", 24444, "plain-token", isSecure: false);

        Assert.Equal("ws://127.0.0.1:24444/api/v1?token=plain-token", endpoint);
    }

    [Fact]
    [Trait("Category", "DaemonClientTransportModernization")]
    public void TransportConfig_WhenIsSecureTrue_UsesWssEndpoint()
    {
        var endpoint = InvokeBuildServerEndpoint("example.com", 443, "secure-token", isSecure: true);

        Assert.Equal("wss://example.com:443/api/v1?token=secure-token", endpoint);
    }

    [Fact]
    [Trait("Category", "DaemonClientTransportModernization")]
    public async Task RequestAsyncOfT_SuccessPath_DeserializesTypedResult()
    {
        var connection = CreateOfflineConnection();
        var requestId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var tcs = new TaskCompletionSource<ActionResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        tcs.SetResult(new ActionResponse
        {
            RequestStatus = ActionRequestStatus.Ok,
            Retcode = ActionRetcode.Ok.Code,
            Message = ActionRetcode.Ok.Message,
            Data = StjJsonSerializer.SerializeToElement(new PingResult { Time = 1717171717171L }, DaemonClientRpcJsonBoundary.StjOptions),
            Id = requestId
        });

        var result = await InvokePrivateEndRequestAsync<PingResult>(connection, tcs, requestId, timeout: 1000, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1717171717171L, result.Time);
    }

    [Fact]
    [Trait("Category", "DaemonClientTransportModernization")]
    public async Task RequestAsyncOfT_ErrorPath_TranslatesToDaemonRequestException()
    {
        var connection = CreateOfflineConnection();
        var requestId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var tcs = new TaskCompletionSource<ActionResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        tcs.SetResult(new ActionResponse
        {
            RequestStatus = ActionRequestStatus.Error,
            Retcode = ActionRetcode.BadRequest.Code,
            Message = "Bad Request: probe failure",
            Data = null,
            Id = requestId
        });

        var exception = await Assert.ThrowsAsync<DaemonRequestException>(async () =>
            await InvokePrivateEndRequestAsync<PingResult>(connection, tcs, requestId, timeout: 1000, CancellationToken.None));

        Assert.Equal(ActionRetcode.BadRequest.Code, exception.Retcode.Code);
        Assert.Equal("Bad Request: probe failure", exception.Message);
    }

    [Fact]
    [Trait("Category", "DaemonClientTransportModernization")]
    public async Task RequestAsyncOfT_Timeout_RemovesPendingRequestBeforeThrowing()
    {
        var connection = CreateOfflineConnection();
        var requestId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var pendingRequests = GetPendingRequests(connection);
        var pendingResponses = GetPendingResponses(connection);
        var tcs = new TaskCompletionSource<ActionResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(await pendingRequests.AddPendingAsync(requestId, tcs, timeout: 1000));
        Assert.True(pendingResponses.TryAdd(requestId, tcs));

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await InvokePrivateEndRequestAsync<PingResult>(connection, tcs, requestId, timeout: 50, CancellationToken.None));

        Assert.False(pendingRequests.TryGetPending(requestId, out _));
    }

    [Fact]
    [Trait("Category", "DaemonClientTransportModernization")]
    public async Task RequestAsyncOfT_Cancellation_RemovesPendingRequestAndPropagatesToken()
    {
        var connection = CreateOfflineConnection();
        var requestId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var pendingRequests = GetPendingRequests(connection);
        var pendingResponses = GetPendingResponses(connection);
        var tcs = new TaskCompletionSource<ActionResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(await pendingRequests.AddPendingAsync(requestId, tcs, timeout: 1000));
        Assert.True(pendingResponses.TryAdd(requestId, tcs));

        using var cts = new CancellationTokenSource();
        var requestTask = InvokePrivateEndRequestAsync<PingResult>(connection, tcs, requestId, timeout: 5000, cts.Token);
        cts.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await requestTask);
        Assert.True(exception.CancellationToken.CanBeCanceled);
        Assert.False(pendingRequests.TryGetPending(requestId, out _));
    }

    [Fact]
    [Trait("Category", "DaemonClientTransportModernization")]
    public async Task ResponseCorrelation_HandleActionResponse_CompletesOnlyMatchingPendingRequest()
    {
        var connection = CreateOfflineConnection(new ClientConnectionConfig
        {
            HeartBeat = false,
            HeartBeatTick = TimeSpan.FromMilliseconds(50),
            MaxFailCount = 1,
            PendingRequestCapacity = 2,
            PingTimeout = 100
        });
        var pendingSlots = GetPendingRequests(connection);
        var pendingResponses = GetPendingResponses(connection);
        var targetId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var otherId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var targetTcs = new TaskCompletionSource<ActionResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherTcs = new TaskCompletionSource<ActionResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(await pendingSlots.AddPendingAsync(targetId, targetTcs, timeout: 1000));
        Assert.True(await pendingSlots.AddPendingAsync(otherId, otherTcs, timeout: 1000));
        Assert.True(pendingResponses.TryAdd(targetId, targetTcs));
        Assert.True(pendingResponses.TryAdd(otherId, otherTcs));

        InvokeHandleActionResponse(connection, new ActionResponse
        {
            RequestStatus = ActionRequestStatus.Ok,
            Retcode = ActionRetcode.Ok.Code,
            Message = ActionRetcode.Ok.Message,
            Data = StjJsonSerializer.SerializeToElement(new PingResult { Time = 1717171717171L }, DaemonClientRpcJsonBoundary.StjOptions),
            Id = targetId
        });

        var response = await targetTcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(targetId, response.Id);
        Assert.False(otherTcs.Task.IsCompleted);
        Assert.False(pendingResponses.ContainsKey(targetId));
        Assert.True(pendingResponses.ContainsKey(otherId));

        pendingResponses.TryRemove(otherId, out _);
        pendingSlots.TryRemovePending(otherId, out _);
    }

    [Fact]
    [Trait("Category", "DaemonClientTransportModernization")]
    public async Task ResponseCorrelation_HandleActionResponse_UnknownResponseId_LeavesKnownPendingRequestUntouched()
    {
        var connection = CreateOfflineConnection(new ClientConnectionConfig
        {
            HeartBeat = false,
            HeartBeatTick = TimeSpan.FromMilliseconds(50),
            MaxFailCount = 1,
            PendingRequestCapacity = 1,
            PingTimeout = 100
        });
        var pendingSlots = GetPendingRequests(connection);
        var pendingResponses = GetPendingResponses(connection);
        var knownId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var unknownId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var knownTcs = new TaskCompletionSource<ActionResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(await pendingSlots.AddPendingAsync(knownId, knownTcs, timeout: 1000));
        Assert.True(pendingResponses.TryAdd(knownId, knownTcs));

        InvokeHandleActionResponse(connection, new ActionResponse
        {
            RequestStatus = ActionRequestStatus.Ok,
            Retcode = ActionRetcode.Ok.Code,
            Message = ActionRetcode.Ok.Message,
            Data = StjJsonSerializer.SerializeToElement(new PingResult { Time = 1717171717171L }, DaemonClientRpcJsonBoundary.StjOptions),
            Id = unknownId
        });

        Assert.False(knownTcs.Task.IsCompleted);
        Assert.True(pendingResponses.ContainsKey(knownId));

        pendingResponses.TryRemove(knownId, out _);
        pendingSlots.TryRemovePending(knownId, out _);
    }

    [Fact]
    [Trait("Category", "DaemonClientTransportModernization")]
    public async Task ReconnectRecovery_WhenReplayCannotRun_PrunesPersistentSubscriptions()
    {
        var connection = CreateOfflineConnection();

        connection.SubscribedEvents.EventSet.Add((EventType.InstanceLog,
            new InstanceLogEventMeta { InstanceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa") }));
        connection.SubscribedEvents.EventSet.Add((EventType.DaemonReport, null));

        await InvokeReconnectRecoveryAsync(connection);

        Assert.Empty(connection.SubscribedEvents.EventSet);
        Assert.Empty(connection.SubscribedEvents.Events);
    }

    [Fact]
    [Trait("Category", "DaemonClientTransportModernization")]
    public async Task ConnectionLost_CancelsPendingResponses_AndClosesPendingSlots()
    {
        var connection = CreateOfflineConnection(new ClientConnectionConfig
        {
            HeartBeat = false,
            HeartBeatTick = TimeSpan.FromMilliseconds(50),
            MaxFailCount = 1,
            PendingRequestCapacity = 1,
            PingTimeout = 100
        });

        var pendingRequests = GetPendingRequests(connection);
        var pendingResponses = GetPendingResponses(connection);
        var requestId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var pendingTcs = new TaskCompletionSource<ActionResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(await pendingRequests.AddPendingAsync(requestId, pendingTcs, timeout: 1000));
        Assert.True(pendingResponses.TryAdd(requestId, pendingTcs));

        InvokeTransportConnectionLost(connection);

        Assert.True(pendingTcs.Task.IsCanceled);
        Assert.False(pendingResponses.ContainsKey(requestId));
        Assert.False(pendingRequests.TryGetPending(requestId, out _));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await pendingRequests.AddPendingAsync(Guid.NewGuid(), new TaskCompletionSource<ActionResponse>(), timeout: 1000));
    }

    [Fact]
    [Trait("Category", "DaemonClientTransportModernization")]
    public async Task OpenAsync_WhenCancellationAlreadyRequested_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await ClientConnection.OpenAsync("127.0.0.1", 24444, "token", false, new ClientConnectionConfig
            {
                HeartBeat = false,
                HeartBeatTick = TimeSpan.FromMilliseconds(50),
                MaxFailCount = 1,
                PendingRequestCapacity = 1,
                PingTimeout = 100
            }, cts.Token));
    }

    private static ClientConnection CreateOfflineConnection()
    {
        return CreateOfflineConnection(new ClientConnectionConfig
        {
            HeartBeat = false,
            HeartBeatTick = TimeSpan.FromMilliseconds(50),
            MaxFailCount = 1,
            PendingRequestCapacity = 8,
            PingTimeout = 100
        });
    }

    private static ClientConnection CreateOfflineConnection(ClientConnectionConfig config)
    {
        var ctor = typeof(ClientConnection).GetConstructor(
                       BindingFlags.Instance | BindingFlags.NonPublic,
                       binder: null,
                       [typeof(ClientConnectionConfig)],
                       modifiers: null)
                   ?? throw new MissingMethodException(typeof(ClientConnection).FullName, ".ctor");

        return (ClientConnection)ctor.Invoke([config]);
    }

    private static JsonElement InvokePrivateSerializeParameterForTransport(IActionParameter? parameter)
    {
        var method = typeof(ClientConnection).GetMethod("SerializeParameterForTransport", BindingFlags.Static | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(typeof(ClientConnection).FullName, "SerializeParameterForTransport");

        return (JsonElement)method.Invoke(null, [parameter])!;
    }

    private static JsonElement ParseJsonElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string InvokeBuildServerEndpoint(string address, int port, string token, bool isSecure)
    {
        var method = typeof(TouchSocketClientTransport).GetMethod("BuildServerEndpoint", BindingFlags.Static | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(typeof(TouchSocketClientTransport).FullName, "BuildServerEndpoint");

        return (string)method.Invoke(null, [address, port, token, isSecure])!;
    }

    private static async Task<TResult?> InvokePrivateEndRequestAsync<TResult>(
        ClientConnection connection,
        TaskCompletionSource<ActionResponse> tcs,
        Guid id,
        int timeout,
        CancellationToken cancellationToken)
        where TResult : class, IActionResult
    {
        var method = typeof(ClientConnection).GetMethod("PrivateEndRequestAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(typeof(ClientConnection).FullName, "PrivateEndRequestAsync");

        var task = (Task<TResult?>?)method
            .MakeGenericMethod(typeof(TResult))
            .Invoke(connection, [tcs, id, timeout, cancellationToken]);

        return await (task ?? throw new InvalidOperationException("PrivateEndRequestAsync did not return a task."));
    }

    private static ConnectionPendingRequests GetPendingRequests(ClientConnection connection)
    {
        var field = typeof(ClientConnection).GetField("_pendingRequests", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new MissingFieldException(typeof(ClientConnection).FullName, "_pendingRequests");

        return (ConnectionPendingRequests)field.GetValue(connection)!;
    }

    private static ConcurrentDictionary<Guid, TaskCompletionSource<ActionResponse>> GetPendingResponses(ClientConnection connection)
    {
        var field = typeof(ClientConnection).GetField("_pendingResponses", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new MissingFieldException(typeof(ClientConnection).FullName, "_pendingResponses");

        return (ConcurrentDictionary<Guid, TaskCompletionSource<ActionResponse>>)field.GetValue(connection)!;
    }

    private static void InvokeHandleActionResponse(ClientConnection connection, ActionResponse response)
    {
        var method = typeof(ClientConnection).GetMethod("HandleActionResponse", BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(typeof(ClientConnection).FullName, "HandleActionResponse");

        method.Invoke(connection, [response]);
    }

    private static async Task InvokeReconnectRecoveryAsync(ClientConnection connection)
    {
        var method = typeof(ClientConnection).GetMethod("OnReconnectedEventHandler", BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(typeof(ClientConnection).FullName, "OnReconnectedEventHandler");

        var task = (Task?)method.Invoke(connection, null)
                   ?? throw new InvalidOperationException("Reconnect recovery handler did not return a task.");

        await task;
    }

    private static void InvokeTransportConnectionLost(ClientConnection connection)
    {
        var transportField = typeof(ClientConnection).GetField("_transport", BindingFlags.Instance | BindingFlags.NonPublic)
                             ?? throw new MissingFieldException(typeof(ClientConnection).FullName, "_transport");
        var transport = transportField.GetValue(connection) ?? throw new InvalidOperationException("Transport was null.");
        var method = transport.GetType().GetMethod("OnConnectionLost", BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(transport.GetType().FullName, "OnConnectionLost");

        method.Invoke(transport, null);
    }
}
