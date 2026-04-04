using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Utils.LazyCell;
using TouchSocket.Core;
using JsonElement = System.Text.Json.JsonElement;

namespace MCServerLauncher.ProtocolTests;

/// <summary>
/// T11 stop gate before generated-default cutover.
///
/// Generated and legacy registry paths must remain exactly equivalent before Task 12 is allowed to flip the default.
/// This suite is the second mandatory stop gate after handler inventory/freeze coverage.
/// </summary>
[Collection("RuntimeSwitchIsolation")]
public class GeneratedRegistryParityTests
{
    private static readonly Guid PingRequestId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid GetSystemInfoRequestId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid FailureRequestId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid AsyncFailureRequestId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    [Trait("Category", "GeneratedRegistryParity")]
    public void StopGate_ActionInventory_Classification_And_Permissions_MatchExactly()
    {
        var legacy = GeneratedRegistryParityHarness.CreateSnapshot(useGeneratedActionRegistry: false);
        var generated = GeneratedRegistryParityHarness.CreateSnapshot(useGeneratedActionRegistry: true);

        Assert.Equal(GeneratedRegistryParityHarness.Order(legacy.HandlerMetas.Keys),
            GeneratedRegistryParityHarness.Order(generated.HandlerMetas.Keys));
        Assert.Equal(GeneratedRegistryParityHarness.Order(legacy.SyncHandlers.Keys),
            GeneratedRegistryParityHarness.Order(generated.SyncHandlers.Keys));
        Assert.Equal(GeneratedRegistryParityHarness.Order(legacy.AsyncHandlers.Keys),
            GeneratedRegistryParityHarness.Order(generated.AsyncHandlers.Keys));

        foreach (var action in GeneratedRegistryParityHarness.Order(legacy.HandlerMetas.Keys))
        {
            Assert.Equal(legacy.HandlerMetas[action].Type, generated.HandlerMetas[action].Type);
            Assert.Equal(
                GeneratedRegistryParityHarness.DescribePermission(legacy.HandlerMetas[action].Permission),
                GeneratedRegistryParityHarness.DescribePermission(generated.HandlerMetas[action].Permission));
        }
    }

    [Fact]
    [Trait("Category", "GeneratedRegistryParity")]
    public async Task StopGate_Ping_And_GetSystemInfo_SuccessPathOutputs_MatchExactly()
    {
        var expectedSystemInfo = LegacyActionRegistryHarness.CreateSystemInfo();
        var legacy = GeneratedRegistryParityHarness.CreateSnapshot(useGeneratedActionRegistry: false);
        var generated = GeneratedRegistryParityHarness.CreateSnapshot(useGeneratedActionRegistry: true);
        var context = LegacyActionRegistryHarness.CreateContext();
        var legacyResolver = LegacyActionRegistryHarness.CreateResolver(expectedSystemInfo);
        var generatedResolver = LegacyActionRegistryHarness.CreateResolver(expectedSystemInfo);

        var legacyPing = legacy.SyncHandlers[ActionType.Ping].Invoke(
            LegacyActionRegistryHarness.ParseElement("{}"),
            PingRequestId,
            context,
            legacyResolver,
            CancellationToken.None);

        var generatedPing = generated.SyncHandlers[ActionType.Ping].Invoke(
            LegacyActionRegistryHarness.ParseElement("{}"),
            PingRequestId,
            context,
            generatedResolver,
            CancellationToken.None);

        GeneratedRegistryParityHarness.AssertEquivalentEnvelope(legacyPing, generatedPing, compareData: false);

        var legacyPingPayload = LegacyActionRegistryHarness.DeserializeData<PingResult>(legacyPing);
        var generatedPingPayload = LegacyActionRegistryHarness.DeserializeData<PingResult>(generatedPing);
        Assert.InRange(generatedPingPayload.Time, legacyPingPayload.Time - 5_000, legacyPingPayload.Time + 5_000);

        var legacySystemInfo = await legacy.AsyncHandlers[ActionType.GetSystemInfo].Invoke(
            LegacyActionRegistryHarness.ParseElement("{}"),
            GetSystemInfoRequestId,
            context,
            legacyResolver,
            CancellationToken.None);

        var generatedSystemInfo = await generated.AsyncHandlers[ActionType.GetSystemInfo].Invoke(
            LegacyActionRegistryHarness.ParseElement("{}"),
            GetSystemInfoRequestId,
            context,
            generatedResolver,
            CancellationToken.None);

        GeneratedRegistryParityHarness.AssertEquivalentEnvelope(legacySystemInfo, generatedSystemInfo);

        var legacyPayload = LegacyActionRegistryHarness.DeserializeData<GetSystemInfoResult>(legacySystemInfo);
        var generatedPayload = LegacyActionRegistryHarness.DeserializeData<GetSystemInfoResult>(generatedSystemInfo);

        Assert.Equal(legacyPayload, generatedPayload);
        Assert.Equal(expectedSystemInfo.Os.Name, generatedPayload.Info.Os.Name);
        Assert.Equal(expectedSystemInfo.Cpu.Name, generatedPayload.Info.Cpu.Name);
        Assert.Equal(expectedSystemInfo.Mem.Total, generatedPayload.Info.Mem.Total);
    }

    [Fact]
    [Trait("Category", "GeneratedRegistryParity")]
    public async Task StopGate_MalformedJson_And_DoesNotExist_FailurePathOutputs_MatchExactly()
    {
        await using var legacy = GeneratedRegistryParityHarness.CreateExecutorScope(
            useGeneratedActionRegistry: false,
            systemInfoCell: GeneratedRegistryParityHarness.CreateValueCell(LegacyActionRegistryHarness.CreateSystemInfo()));

        await using var generated = GeneratedRegistryParityHarness.CreateExecutorScope(
            useGeneratedActionRegistry: true,
            systemInfoCell: GeneratedRegistryParityHarness.CreateValueCell(LegacyActionRegistryHarness.CreateSystemInfo()));

        const string malformedJson = "{\"action\":\"ping\"";
        var unknownActionJson = $$"""
                                {
                                  "action": "does_not_exist",
                                  "params": {},
                                  "id": "{{FailureRequestId}}"
                                }
                                """;

        var legacyMalformed = legacy.ProcessAction(malformedJson);
        var generatedMalformed = generated.ProcessAction(malformedJson);
        GeneratedRegistryParityHarness.AssertEquivalentEnvelope(legacyMalformed, generatedMalformed);

        var legacyUnknown = legacy.ProcessAction(unknownActionJson);
        var generatedUnknown = generated.ProcessAction(unknownActionJson);
        GeneratedRegistryParityHarness.AssertEquivalentEnvelope(legacyUnknown, generatedUnknown);
    }

    [Fact]
    [Trait("Category", "GeneratedRegistryParity")]
    public async Task StopGate_GetSystemInfo_Exception_And_Cancellation_Behavior_MatchExactly()
    {
        var legacy = GeneratedRegistryParityHarness.CreateSnapshot(useGeneratedActionRegistry: false);
        var generated = GeneratedRegistryParityHarness.CreateSnapshot(useGeneratedActionRegistry: true);
        var context = LegacyActionRegistryHarness.CreateContext();
        var exception = new InvalidOperationException("parity-system-info exploded");

        var legacyExceptionResponse = await GeneratedRegistryParityHarness.InvokeAsyncWithExecutorTranslation(
            legacy.AsyncHandlers[ActionType.GetSystemInfo],
            AsyncFailureRequestId,
            context,
            LegacyActionRegistryHarness.CreateResolver(GeneratedRegistryParityHarness.CreateFaultedCell<SystemInfo>(exception)));

        var generatedExceptionResponse = await GeneratedRegistryParityHarness.InvokeAsyncWithExecutorTranslation(
            generated.AsyncHandlers[ActionType.GetSystemInfo],
            AsyncFailureRequestId,
            context,
            LegacyActionRegistryHarness.CreateResolver(GeneratedRegistryParityHarness.CreateFaultedCell<SystemInfo>(exception)));

        GeneratedRegistryParityHarness.AssertEquivalentEnvelope(legacyExceptionResponse, generatedExceptionResponse);
        Assert.Equal(ActionRequestStatus.Error, legacyExceptionResponse.RequestStatus);
        Assert.Equal(ActionRetcode.UnexpectedError.Code, legacyExceptionResponse.Retcode);
        Assert.Equal("Unexpected Error: parity-system-info exploded", legacyExceptionResponse.Message);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var legacyCanceledResponse = await GeneratedRegistryParityHarness.InvokeAsyncWithExecutorTranslation(
            legacy.AsyncHandlers[ActionType.GetSystemInfo],
            AsyncFailureRequestId,
            context,
            LegacyActionRegistryHarness.CreateResolver(GeneratedRegistryParityHarness.CreateCanceledCell<SystemInfo>(cts.Token)));

        var generatedCanceledResponse = await GeneratedRegistryParityHarness.InvokeAsyncWithExecutorTranslation(
            generated.AsyncHandlers[ActionType.GetSystemInfo],
            AsyncFailureRequestId,
            context,
            LegacyActionRegistryHarness.CreateResolver(GeneratedRegistryParityHarness.CreateCanceledCell<SystemInfo>(cts.Token)));

        GeneratedRegistryParityHarness.AssertEquivalentEnvelope(legacyCanceledResponse, generatedCanceledResponse);
        Assert.Equal(ActionRequestStatus.Error, legacyCanceledResponse.RequestStatus);
        Assert.Equal(ActionRetcode.UnexpectedError.Code, legacyCanceledResponse.Retcode);
        Assert.Equal("Unexpected Error: A task was canceled.", legacyCanceledResponse.Message);
    }
}

internal static class GeneratedRegistryParityHarness
{
    public static ActionHandlerRegistrySnapshot CreateSnapshot(bool useGeneratedActionRegistry)
    {
        ActionHandlerRegistryRuntime.Reset();
        return ActionHandlerRegistryRuntime.CreateSelected(useGeneratedActionRegistry);
    }

    public static ExecutorScope CreateExecutorScope(bool useGeneratedActionRegistry, IAsyncTimedLazyCell<SystemInfo> systemInfoCell)
    {
        var executor = new AnotherActionExecutor(
            LegacyActionRegistryHarness.CreateResolver(systemInfoCell),
            CreateSnapshot(useGeneratedActionRegistry));

        return new ExecutorScope(executor);
    }

    public static ActionType[] Order(IEnumerable<ActionType> actions)
    {
        return actions.OrderBy(action => (int)action).ToArray();
    }

    public static string DescribePermission(object permission)
    {
        return permission.ToString() ?? string.Empty;
    }

    public static IAsyncTimedLazyCell<T> CreateValueCell<T>(T value)
    {
        return new ProbeAsyncTimedLazyCell<T>(Task.FromResult(value));
    }

    public static IAsyncTimedLazyCell<T> CreateFaultedCell<T>(Exception exception)
    {
        return new ProbeAsyncTimedLazyCell<T>(Task.FromException<T>(exception));
    }

    public static IAsyncTimedLazyCell<T> CreateCanceledCell<T>(CancellationToken cancellationToken)
    {
        return new ProbeAsyncTimedLazyCell<T>(Task.FromCanceled<T>(cancellationToken));
    }

    public static async Task<ActionResponse> InvokeAsyncWithExecutorTranslation(
        Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>> handler,
        Guid id,
        WsContext context,
        IResolver resolver,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await handler(
                LegacyActionRegistryHarness.ParseElement("{}"),
                id,
                context,
                resolver,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return ResponseUtils.Err(ActionRetcode.UnexpectedError.WithMessage(exception.Message), id);
        }
    }

    public static void AssertEquivalentEnvelope(ActionResponse legacy, ActionResponse generated, bool compareData = true)
    {
        Assert.Equal(legacy.RequestStatus, generated.RequestStatus);
        Assert.Equal(legacy.Retcode, generated.Retcode);
        Assert.Equal(legacy.Message, generated.Message);
        Assert.Equal(legacy.Id, generated.Id);

        if (compareData)
        {
            Assert.Equal(SerializeData(legacy.Data), SerializeData(generated.Data));
        }
    }

    private static string? SerializeData(JsonElement? data)
    {
        return data?.GetRawText();
    }

    internal sealed class ExecutorScope(AnotherActionExecutor executor) : IAsyncDisposable
    {
        public ActionResponse ProcessAction(string json)
        {
            return Assert.IsType<ActionResponse>(executor.ProcessAction(json, LegacyActionRegistryHarness.CreateContext()));
        }

        public async ValueTask DisposeAsync()
        {
            await LegacyActionRegistryHarness.ShutdownExecutorAsync(executor);
            ActionHandlerRegistryRuntime.Reset();
        }
    }

    private sealed class ProbeAsyncTimedLazyCell<T>(Task<T> task) : IAsyncTimedLazyCell<T>
    {
        public DateTime LastUpdated { get; } = DateTime.UtcNow;
        public TimeSpan CacheDuration { get; } = TimeSpan.FromHours(1);
        public ValueTask<T> Value => new(task);

        public bool IsExpired()
        {
            return false;
        }

        public Task Update()
        {
            return Task.CompletedTask;
        }
    }
}
