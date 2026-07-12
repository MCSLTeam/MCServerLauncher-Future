using System.Collections.Concurrent;
using System.Text.Json;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Event;
using TouchSocket.Http;

namespace MCServerLauncher.ProtocolTests;

public sealed class WsEventPluginQueueTests
{
    [Fact]
    public async Task NormalDrain_WaitsForBlockedSend_FlushesAcceptedEventsInOrder_WithoutCancellingLifetimeToken()
    {
        var eventService = new EventService();
        var contexts = CreateSubscribedContext(out var instanceId);
        var sendEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sentPayloads = new ConcurrentQueue<byte[]>();
        CancellationToken capturedToken = default;
        var sends = 0;
        await using var plugin = CreatePlugin(
            eventService,
            contexts,
            async (_, payload, cancellationToken) =>
            {
                sentPayloads.Enqueue(payload);
                capturedToken = cancellationToken;
                if (Interlocked.Increment(ref sends) == 1)
                {
                    sendEntered.TrySetResult();
                    await releaseFirstSend.Task.WaitAsync(cancellationToken);
                }
            });

        eventService.OnInstanceLog(instanceId, "first");
        await sendEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        eventService.OnInstanceLog(instanceId, "second");

        plugin.StopAccepting();
        var drain = plugin.DrainAsync(CancellationToken.None);
        Assert.False(drain.IsCompleted);
        Assert.False(capturedToken.IsCancellationRequested);

        releaseFirstSend.TrySetResult();
        await drain.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(["first", "second"], sentPayloads.Select(GetInstanceLog).ToArray());
        Assert.False(capturedToken.IsCancellationRequested);
    }

    [Fact]
    public async Task ExternalDrainCancellation_CancelsLifetimeSendToken_DrainsProcessor_AndPreservesExternalCancellation()
    {
        var eventService = new EventService();
        var contexts = CreateSubscribedContext(out var instanceId);
        var sendEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processorStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken capturedToken = default;
        await using var plugin = CreatePlugin(
            eventService,
            contexts,
            async (_, _, cancellationToken) =>
            {
                capturedToken = cancellationToken;
                sendEntered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    processorStopped.TrySetResult();
                }
            });

        eventService.OnInstanceLog(instanceId, "blocked");
        await sendEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        using var externalCancellation = new CancellationTokenSource();
        var drain = plugin.DrainAsync(externalCancellation.Token);

        externalCancellation.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => drain);

        Assert.Equal(externalCancellation.Token, exception.CancellationToken);
        Assert.True(capturedToken.IsCancellationRequested);
        await processorStopped.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await plugin.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task ProcessorFault_IsObservedByDrain_AndCapturedTokenRemainsObservableAfterDispose()
    {
        var eventService = new EventService();
        var contexts = CreateSubscribedContext(out var instanceId);
        CancellationToken capturedToken = default;
        await using var plugin = CreatePlugin(
            eventService,
            contexts,
            (_, _, cancellationToken) =>
            {
                capturedToken = cancellationToken;
                return Task.FromException(new InvalidOperationException("send failed"));
            });

        eventService.OnInstanceLog(instanceId, "fault");
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => plugin.DrainAsync(CancellationToken.None));
        Assert.Equal("send failed", failure.Message);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await plugin.DisposeAsync());

        Assert.True(capturedToken.CanBeCanceled);
        Assert.False(capturedToken.IsCancellationRequested);
    }

    private static WsEventPlugin CreatePlugin(
        IEventService eventService,
        WsContextContainer contexts,
        LegacyWebSocketSendAsync sendAsync)
    {
        return new WsEventPlugin(eventService, contexts, new HttpService(), sendAsync);
    }

    private static WsContextContainer CreateSubscribedContext(out Guid instanceId)
    {
        instanceId = Guid.NewGuid();
        var contexts = new WsContextContainer();
        var context = contexts.CreateContext("queue-test", Guid.NewGuid(), "*", DateTime.UtcNow.AddMinutes(1));
        context.SubscribeEvent(EventType.InstanceLog, new InstanceLogEventMeta { InstanceId = instanceId });
        return contexts;
    }

    private static string GetInstanceLog(byte[] payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement
            .GetProperty("data")
            .GetProperty("log")
            .GetString() ?? throw new InvalidOperationException("The event packet did not contain a log.");
    }
}
