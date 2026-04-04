using System.Text.Json;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Serialization;
using RustyOptions;
using TouchSocket.Core;
using RResult = RustyOptions.Result;

namespace MCServerLauncher.ProtocolTests;

[Collection("LegacyActionRegistryIsolation")]
public class ActionExecutorInstrumentationTests
{
    [Fact]
    [Trait("Category", "ActionExecutorInstrumentation")]
    public void ExecutorMaxDegreeOfParallelism_Remains16()
    {
        Assert.Equal(16, AnotherActionExecutor.MaxDegreeOfParallelism);
    }

    [Fact]
    [Trait("Category", "ActionExecutorInstrumentation")]
    public async Task AsyncSuccessPath_CollectsQueueHandlerAndSendMetrics_WithoutChangingResponseShape()
    {
        var requestId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var resolver = LegacyActionRegistryHarness.CreateResolver();
        var snapshot = LegacyActionRegistryHarness.BuildSnapshot(typeof(InstrumentationSuccessHandler));
        var recorder = new SendRecorder(expectedCount: 1);
        var instrumentation = new ActionExecutorInstrumentationCollector();

        var executor = new AnotherActionExecutor(
            resolver,
            new ActionHandlerRegistrySnapshot(
                ActionHandlerRegistryMode.Legacy,
                snapshot.HandlerMetas,
                snapshot.SyncHandlers,
                snapshot.AsyncHandlers),
            instrumentation,
            recorder.SendAsync);

        try
        {
            var immediate = executor.ProcessAction(
                BuildAsyncRequest(ActionType.GetSystemInfo, requestId),
                LegacyActionRegistryHarness.CreateContext());

            Assert.Null(immediate);

            var sent = await recorder.WaitForAllAsync(TimeSpan.FromSeconds(5));
            var response = JsonSerializer.Deserialize<ActionResponse>(sent.Single(), DaemonRpcJsonBoundary.StjOptions);

            Assert.NotNull(response);
            Assert.Equal(ActionRequestStatus.Ok, response!.RequestStatus);
            Assert.Equal(ActionRetcode.Ok.Code, response.Retcode);
            Assert.Equal(requestId, response.Id);

            var metrics = instrumentation.Snapshot();
            Assert.Equal(1, metrics.QueueSubmittedCount);
            Assert.Equal(0, metrics.QueueRejectedCount);
            Assert.Equal(1, metrics.QueueWaitSampleCount);
            Assert.Equal(1, metrics.HandlerDurationSampleCount);
            Assert.Equal(1, metrics.SendDurationSampleCount);
            Assert.Equal(1, metrics.HandlerSuccessCount);
            Assert.Equal(1, metrics.SendSuccessCount);
            Assert.Equal(0, metrics.HandlerFailureCount);
            Assert.Equal(0, metrics.HandlerCancellationCount);
            Assert.Equal(0, metrics.SendFailureCount);
            Assert.Equal(0, metrics.SendCancellationCount);
        }
        finally
        {
            await LegacyActionRegistryHarness.ShutdownExecutorAsync(executor);
        }
    }

    [Fact]
    [Trait("Category", "ActionExecutorInstrumentation")]
    public async Task QueueRejectPath_RecordsSubmitAndRejectCounters_AndKeepsRateLimitResponse()
    {
        var requestId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var resolver = LegacyActionRegistryHarness.CreateResolver();
        var snapshot = LegacyActionRegistryHarness.BuildSnapshot(typeof(InstrumentationSuccessHandler));
        var instrumentation = new ActionExecutorInstrumentationCollector();

        var executor = new AnotherActionExecutor(
            resolver,
            new ActionHandlerRegistrySnapshot(
                ActionHandlerRegistryMode.Legacy,
                snapshot.HandlerMetas,
                snapshot.SyncHandlers,
                snapshot.AsyncHandlers),
            instrumentation,
            static (_, _, _) => Task.CompletedTask);

        await LegacyActionRegistryHarness.ShutdownExecutorAsync(executor);

        var response = executor.ProcessAction(
            BuildAsyncRequest(ActionType.GetSystemInfo, requestId),
            LegacyActionRegistryHarness.CreateContext());

        Assert.NotNull(response);
        Assert.Equal(ActionRequestStatus.Error, response!.RequestStatus);
        Assert.Equal(ActionRetcode.RateLimitExceeded.Code, response.Retcode);
        Assert.Equal(requestId, response.Id);

        var metrics = instrumentation.Snapshot();
        Assert.Equal(1, metrics.QueueSubmittedCount);
        Assert.Equal(1, metrics.QueueRejectedCount);
        Assert.Equal(0, metrics.QueueWaitSampleCount);
        Assert.Equal(0, metrics.HandlerDurationSampleCount);
        Assert.Equal(0, metrics.SendDurationSampleCount);
    }

    [Fact]
    [Trait("Category", "ActionExecutorInstrumentation")]
    public async Task HandlerFailureAndCancellation_AreTrackedSeparately_WithoutChangingErrorEnvelopeKind()
    {
        await VerifyHandlerFailureCountersAsync(
            typeof(InstrumentationFailureHandler),
            ActionType.GetJavaList,
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            expectedMessage: "Unexpected Error: handler-failure-probe");

        await VerifyHandlerFailureCountersAsync(
            typeof(InstrumentationCancellationHandler),
            ActionType.FileUploadChunk,
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            expectedMessage: "Unexpected Error: The operation was canceled.");
    }

    [Fact]
    [Trait("Category", "ActionExecutorInstrumentation")]
    public async Task SendFailureAndCancellation_AreTrackedSeparately()
    {
        await VerifySendFaultCountersAsync(
            static (_, _, _) => throw new InvalidOperationException("send-failure-probe"),
            expectedSendFailureCount: 1,
            expectedSendCancellationCount: 0);

        await VerifySendFaultCountersAsync(
            static (_, _, _) => throw new OperationCanceledException("send-canceled-probe"),
            expectedSendFailureCount: 0,
            expectedSendCancellationCount: 1);
    }

    private static async Task VerifyHandlerFailureCountersAsync(
        Type handlerType,
        ActionType actionType,
        Guid requestId,
        string expectedMessage)
    {
        var resolver = LegacyActionRegistryHarness.CreateResolver();
        var snapshot = LegacyActionRegistryHarness.BuildSnapshot(handlerType);
        var recorder = new SendRecorder(expectedCount: 1);
        var instrumentation = new ActionExecutorInstrumentationCollector();

        var executor = new AnotherActionExecutor(
            resolver,
            new ActionHandlerRegistrySnapshot(
                ActionHandlerRegistryMode.Legacy,
                snapshot.HandlerMetas,
                snapshot.SyncHandlers,
                snapshot.AsyncHandlers),
            instrumentation,
            recorder.SendAsync);

        try
        {
            var immediate = executor.ProcessAction(BuildAsyncRequest(actionType, requestId), LegacyActionRegistryHarness.CreateContext());
            Assert.Null(immediate);

            var sent = await recorder.WaitForAllAsync(TimeSpan.FromSeconds(5));
            var response = JsonSerializer.Deserialize<ActionResponse>(sent.Single(), DaemonRpcJsonBoundary.StjOptions);

            Assert.NotNull(response);
            Assert.Equal(ActionRequestStatus.Error, response!.RequestStatus);
            Assert.Equal(ActionRetcode.UnexpectedError.Code, response.Retcode);
            Assert.Equal(expectedMessage, response.Message);
            Assert.Equal(requestId, response.Id);

            var metrics = instrumentation.Snapshot();
            Assert.Equal(1, metrics.QueueSubmittedCount);
            Assert.Equal(1, metrics.QueueWaitSampleCount);
            Assert.Equal(1, metrics.HandlerDurationSampleCount);
            Assert.Equal(1, metrics.SendDurationSampleCount);
            Assert.Equal(0, metrics.HandlerSuccessCount);
            Assert.Equal(1, metrics.SendSuccessCount);

            if (handlerType == typeof(InstrumentationFailureHandler))
            {
                Assert.Equal(1, metrics.HandlerFailureCount);
                Assert.Equal(0, metrics.HandlerCancellationCount);
            }
            else
            {
                Assert.Equal(0, metrics.HandlerFailureCount);
                Assert.Equal(1, metrics.HandlerCancellationCount);
            }
        }
        finally
        {
            await LegacyActionRegistryHarness.ShutdownExecutorAsync(executor);
        }
    }

    private static async Task VerifySendFaultCountersAsync(
        Func<WsContext, string, CancellationToken, Task> sendAsync,
        long expectedSendFailureCount,
        long expectedSendCancellationCount)
    {
        var requestId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var resolver = LegacyActionRegistryHarness.CreateResolver();
        var snapshot = LegacyActionRegistryHarness.BuildSnapshot(typeof(InstrumentationSuccessHandler));
        var instrumentation = new ActionExecutorInstrumentationCollector();

        var executor = new AnotherActionExecutor(
            resolver,
            new ActionHandlerRegistrySnapshot(
                ActionHandlerRegistryMode.Legacy,
                snapshot.HandlerMetas,
                snapshot.SyncHandlers,
                snapshot.AsyncHandlers),
            instrumentation,
            sendAsync);

        var immediate = executor.ProcessAction(
            BuildAsyncRequest(ActionType.GetSystemInfo, requestId),
            LegacyActionRegistryHarness.CreateContext());

        Assert.Null(immediate);

        await WaitUntilAsync(
            () =>
            {
                var metrics = instrumentation.Snapshot();
                return metrics.SendFailureCount + metrics.SendCancellationCount > 0;
            },
            TimeSpan.FromSeconds(5));

        var shutdownException = await Assert.ThrowsAnyAsync<Exception>(executor.ShutdownAsync);
        Assert.True(
            shutdownException is InvalidOperationException or OperationCanceledException,
            $"Unexpected shutdown exception type: {shutdownException.GetType().FullName}");

        var counters = instrumentation.Snapshot();
        Assert.Equal(1, counters.QueueSubmittedCount);
        Assert.Equal(1, counters.QueueWaitSampleCount);
        Assert.Equal(1, counters.HandlerSuccessCount);
        Assert.Equal(1, counters.HandlerDurationSampleCount);
        Assert.Equal(1, counters.SendDurationSampleCount);
        Assert.Equal(expectedSendFailureCount, counters.SendFailureCount);
        Assert.Equal(expectedSendCancellationCount, counters.SendCancellationCount);
        Assert.Equal(0, counters.SendSuccessCount);
    }

    private static string BuildAsyncRequest(ActionType actionType, Guid id)
    {
        return JsonSerializer.Serialize(
            new ActionRequest
            {
                ActionType = actionType,
                Parameter = LegacyActionRegistryHarness.ParseElement("{}"),
                Id = id
            },
            DaemonRpcJsonBoundary.StjOptions);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);

        while (!condition())
        {
            await Task.Delay(10, cts.Token);
        }
    }

    [ActionHandler(ActionType.GetSystemInfo, "*")]
    public sealed class InstrumentationSuccessHandler : IAsyncActionHandler<EmptyActionParameter, EmptyActionResult>
    {
        public async Task<Result<EmptyActionResult, ActionError>> HandleAsync(
            EmptyActionParameter param,
            WsContext ctx,
            IResolver resolver,
            CancellationToken ct)
        {
            await Task.Yield();
            return RResult.Ok<EmptyActionResult, ActionError>(new EmptyActionResult());
        }
    }

    [ActionHandler(ActionType.GetJavaList, "*")]
    public sealed class InstrumentationFailureHandler : IAsyncActionHandler<EmptyActionParameter, EmptyActionResult>
    {
        public Task<Result<EmptyActionResult, ActionError>> HandleAsync(
            EmptyActionParameter param,
            WsContext ctx,
            IResolver resolver,
            CancellationToken ct)
        {
            throw new InvalidOperationException("handler-failure-probe");
        }
    }

    [ActionHandler(ActionType.FileUploadChunk, "*")]
    public sealed class InstrumentationCancellationHandler : IAsyncActionHandler<EmptyActionParameter, EmptyActionResult>
    {
        public Task<Result<EmptyActionResult, ActionError>> HandleAsync(
            EmptyActionParameter param,
            WsContext ctx,
            IResolver resolver,
            CancellationToken ct)
        {
            throw new OperationCanceledException();
        }
    }

    private sealed class SendRecorder(int expectedCount)
    {
        private readonly List<string> _messages = [];
        private readonly TaskCompletionSource<bool> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SendAsync(WsContext _, string payload, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_messages)
            {
                _messages.Add(payload);

                if (_messages.Count >= expectedCount)
                {
                    _completion.TrySetResult(true);
                }
            }

            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<string>> WaitForAllAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            await _completion.Task.WaitAsync(cts.Token);

            lock (_messages)
            {
                return _messages.ToArray();
            }
        }
    }
}
