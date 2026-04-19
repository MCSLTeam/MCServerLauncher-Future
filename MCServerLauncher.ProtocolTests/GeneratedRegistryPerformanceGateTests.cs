using System.Text.Json;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.ProtocolTests.Helpers;
using TouchSocket.Core;

namespace MCServerLauncher.ProtocolTests;

/// <summary>
/// Task 16 deterministic perf gate for the generated action registry.
///
/// The plan requires in-process median comparisons against the retained legacy registry path so these gates
/// remain deterministic on CI while still proving the generated startup path is materially cheaper.
/// </summary>
[Collection("PerformanceGate")]
public class GeneratedRegistryPerformanceGateTests
{
    private static readonly Guid PingRequestId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid GetSystemInfoRequestId = Guid.Parse("88888888-8888-8888-8888-888888888888");

    private const int WarmupSamples = 4;
    private const int MeasuredSamples = 9;
    private const double StartupTimeRatioThreshold = 0.80;
    private const double StartupAllocationRatioThreshold = 0.25;
    // CI-measured ratio 0.966 (noisy run: 1.212). 1.30 gives ~35% margin for runner variance
    private const double DispatchTimeRatioThreshold = 1.30;
    private const double DispatchAllocationOverheadThreshold = 32;

    [Fact]
    [Trait("Category", "Perf")]
    [Trait("Category", "PerformanceGate")]
    [Trait("Category", "AllocationGate")]
    [Trait("Category", "GeneratedRegistryPerformanceGate")]
    public void PerfGate_GeneratedRegistryStartupRegistration_MeetsTimeAndAllocationTargets()
    {
        const int operationsPerSample = 30;

        var legacy = MeasureStartupRegistration(useGeneratedActionRegistry: false, operationsPerSample);
        var generated = MeasureStartupRegistration(useGeneratedActionRegistry: true, operationsPerSample);

        AssertTimeRatio(
            scenarioName: "generated registry startup registration",
            legacy,
            generated,
            StartupTimeRatioThreshold);

        AssertAllocationRatio(
            scenarioName: "generated registry startup registration",
            legacy,
            generated,
            StartupAllocationRatioThreshold);
    }

    [Fact]
    [Trait("Category", "Perf")]
    [Trait("Category", "PerformanceGate")]
    [Trait("Category", "AllocationGate")]
    [Trait("Category", "GeneratedRegistryPerformanceGate")]
    public void PerfGate_GeneratedRegistryPingDispatch_StaysWithinLegacyOverheadBudget()
    {
        const int operationsPerSample = 6_000;

        using var legacy = CreateDispatchScenario(useGeneratedActionRegistry: false);
        using var generated = CreateDispatchScenario(useGeneratedActionRegistry: true);

        var legacyPrecheck = DispatchPing(legacy);
        var generatedPrecheck = DispatchPing(generated);

        Assert.Equal(ActionRequestStatus.Ok, legacyPrecheck.RequestStatus);
        Assert.Equal(ActionRequestStatus.Ok, generatedPrecheck.RequestStatus);

        var legacyMeasurement = PerformanceGateHarness.Measure(
            operation: () => VerifySuccessfulResponse(DispatchPing(legacy), "legacy ping dispatch"),
            operationsPerSample: operationsPerSample,
            warmupSamples: WarmupSamples,
            measuredSamples: MeasuredSamples);

        var generatedMeasurement = PerformanceGateHarness.Measure(
            operation: () => VerifySuccessfulResponse(DispatchPing(generated), "generated ping dispatch"),
            operationsPerSample: operationsPerSample,
            warmupSamples: WarmupSamples,
            measuredSamples: MeasuredSamples);

        AssertTimeRatio(
            scenarioName: "generated ping dispatch",
            legacyMeasurement,
            generatedMeasurement,
            DispatchTimeRatioThreshold);

        AssertAllocationOverhead(
            scenarioName: "generated ping dispatch",
            legacyMeasurement,
            generatedMeasurement,
            DispatchAllocationOverheadThreshold);
    }

    [Fact]
    [Trait("Category", "Perf")]
    [Trait("Category", "PerformanceGate")]
    [Trait("Category", "AllocationGate")]
    [Trait("Category", "GeneratedRegistryPerformanceGate")]
    public void PerfGate_GeneratedRegistryGetSystemInfoDispatch_StaysWithinLegacyOverheadBudget()
    {
        const int operationsPerSample = 6_000;

        using var legacy = CreateDispatchScenario(useGeneratedActionRegistry: false);
        using var generated = CreateDispatchScenario(useGeneratedActionRegistry: true);

        var legacyPrecheck = DispatchGetSystemInfo(legacy);
        var generatedPrecheck = DispatchGetSystemInfo(generated);

        Assert.Equal(ActionRequestStatus.Ok, legacyPrecheck.RequestStatus);
        Assert.Equal(ActionRequestStatus.Ok, generatedPrecheck.RequestStatus);

        var legacyMeasurement = PerformanceGateHarness.Measure(
            operation: () => VerifySuccessfulResponse(DispatchGetSystemInfo(legacy), "legacy get_system_info dispatch"),
            operationsPerSample: operationsPerSample,
            warmupSamples: WarmupSamples,
            measuredSamples: MeasuredSamples);

        var generatedMeasurement = PerformanceGateHarness.Measure(
            operation: () => VerifySuccessfulResponse(DispatchGetSystemInfo(generated), "generated get_system_info dispatch"),
            operationsPerSample: operationsPerSample,
            warmupSamples: WarmupSamples,
            measuredSamples: MeasuredSamples);

        AssertTimeRatio(
            scenarioName: "generated get_system_info dispatch",
            legacyMeasurement,
            generatedMeasurement,
            DispatchTimeRatioThreshold);

        AssertAllocationOverhead(
            scenarioName: "generated get_system_info dispatch",
            legacyMeasurement,
            generatedMeasurement,
            DispatchAllocationOverheadThreshold);
    }

    private static PerformanceMeasurement MeasureStartupRegistration(bool useGeneratedActionRegistry, int operationsPerSample)
    {
        var precheck = ActionHandlerRegistryRuntime.CreateSelected(useGeneratedActionRegistry);
        Assert.NotEmpty(precheck.HandlerMetas);
        Assert.NotEmpty(precheck.SyncHandlers);

        return PerformanceGateHarness.Measure(
            operation: () =>
            {
                var snapshot = ActionHandlerRegistryRuntime.CreateSelected(useGeneratedActionRegistry);
                if (snapshot.HandlerMetas.Count == 0)
                    throw new InvalidOperationException("Registry startup replay sample returned no handlers.");
            },
            operationsPerSample: operationsPerSample,
            warmupSamples: WarmupSamples,
            measuredSamples: MeasuredSamples);
    }

    private static DispatchScenario CreateDispatchScenario(bool useGeneratedActionRegistry)
    {
        var snapshot = ActionHandlerRegistryRuntime.CreateSelected(useGeneratedActionRegistry);
        var systemInfo = LegacyActionRegistryHarness.CreateSystemInfo();
        var emptyParameters = LegacyActionRegistryHarness.ParseElement("{}");

        return new DispatchScenario(
            new SnapshotExecutor(snapshot),
            LegacyActionRegistryHarness.CreateResolver(systemInfo),
            LegacyActionRegistryHarness.CreateContext(),
            new ActionRequest
            {
                ActionType = ActionType.Ping,
                Parameter = emptyParameters,
                Id = PingRequestId
            },
            new ActionRequest
            {
                ActionType = ActionType.GetSystemInfo,
                Parameter = emptyParameters,
                Id = GetSystemInfoRequestId
            },
            snapshot.SyncHandlers[ActionType.Ping],
            snapshot.AsyncHandlers[ActionType.GetSystemInfo]);
    }

    private static ActionResponse DispatchPing(DispatchScenario scenario)
    {
        EnsureHandlerAvailable(scenario, scenario.PingRequest, ActionType.Ping);
        return DispatchPingCore(scenario);
    }

    private static ActionResponse DispatchGetSystemInfo(DispatchScenario scenario)
    {
        EnsureHandlerAvailable(scenario, scenario.GetSystemInfoRequest, ActionType.GetSystemInfo);
        return DispatchGetSystemInfoCore(scenario);
    }

    private static void EnsureHandlerAvailable(DispatchScenario scenario, ActionRequest request, ActionType actionType)
    {
        var checkedHandler = scenario.Executor.CheckHandler(request, scenario.Context);
        if (checkedHandler.IsErr(out var errorResponse))
            throw new InvalidOperationException(
                errorResponse?.Message
                ?? $"{actionType} dispatch permission check failed without an error response.");
    }

    private static ActionResponse DispatchPingCore(DispatchScenario scenario)
    {
        return scenario.PingHandler.Invoke(
            scenario.PingRequest.Parameter,
            scenario.PingRequest.Id,
            scenario.Context,
            scenario.Resolver,
            CancellationToken.None);
    }

    private static ActionResponse DispatchGetSystemInfoCore(DispatchScenario scenario)
    {
        return scenario.GetSystemInfoHandler
            .Invoke(
                scenario.GetSystemInfoRequest.Parameter,
                scenario.GetSystemInfoRequest.Id,
                scenario.Context,
                scenario.Resolver,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private static void VerifySuccessfulResponse(ActionResponse response, string scenarioName)
    {
        if (response.RequestStatus != ActionRequestStatus.Ok)
            throw new InvalidOperationException($"{scenarioName} replay sample returned {response.RequestStatus} instead of Ok.");

        if (!response.Data.HasValue)
            throw new InvalidOperationException($"{scenarioName} replay sample returned no payload.");
    }

    private static void AssertTimeRatio(
        string scenarioName,
        PerformanceMeasurement legacy,
        PerformanceMeasurement generated,
        double maxRatio)
    {
        var ratio = generated.NanosecondsPerOperation / legacy.NanosecondsPerOperation;

        Console.WriteLine($"[PERF] {scenarioName}: generated {generated.NanosecondsPerOperation:F2} ns/op vs legacy {legacy.NanosecondsPerOperation:F2} ns/op (ratio {ratio:F3}, threshold {maxRatio:F3})");

        Assert.True(
            ratio <= maxRatio,
            $"Perf regression for {scenarioName}: generated {generated.NanosecondsPerOperation:F2} ns/op vs legacy {legacy.NanosecondsPerOperation:F2} ns/op (ratio {ratio:F3}, threshold {maxRatio:F3}).");
    }

    private static void AssertAllocationRatio(
        string scenarioName,
        PerformanceMeasurement legacy,
        PerformanceMeasurement generated,
        double maxRatio)
    {
        var ratio = generated.AllocatedBytesPerOperation / legacy.AllocatedBytesPerOperation;

        Assert.True(
            ratio <= maxRatio,
            $"Allocation regression for {scenarioName}: generated {generated.AllocatedBytesPerOperation:F2} B/op vs legacy {legacy.AllocatedBytesPerOperation:F2} B/op (ratio {ratio:F3}, threshold {maxRatio:F3}).");
    }

    private static void AssertAllocationOverhead(
        string scenarioName,
        PerformanceMeasurement legacy,
        PerformanceMeasurement generated,
        double maxIncrease)
    {
        var delta = generated.AllocatedBytesPerOperation - legacy.AllocatedBytesPerOperation;

        Assert.True(
            delta <= maxIncrease,
            $"Allocation regression for {scenarioName}: generated {generated.AllocatedBytesPerOperation:F2} B/op vs legacy {legacy.AllocatedBytesPerOperation:F2} B/op (delta {delta:F2} B/op, threshold +{maxIncrease:F0} B/op).");
    }

    private sealed class DispatchScenario(
        SnapshotExecutor executor,
        IResolver resolver,
        WsContext context,
        ActionRequest pingRequest,
        ActionRequest getSystemInfoRequest,
        Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse> pingHandler,
        Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>> getSystemInfoHandler)
        : IDisposable
    {
        public SnapshotExecutor Executor { get; } = executor;
        public IResolver Resolver { get; } = resolver;
        public WsContext Context { get; } = context;
        public ActionRequest PingRequest { get; } = pingRequest;
        public ActionRequest GetSystemInfoRequest { get; } = getSystemInfoRequest;
        public Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse> PingHandler { get; } = pingHandler;
        public Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>> GetSystemInfoHandler { get; } = getSystemInfoHandler;

        public void Dispose()
        {
            ActionHandlerRegistryRuntime.Reset();
        }
    }

    private sealed class SnapshotExecutor(ActionHandlerRegistrySnapshot snapshot) : IActionExecutor
    {
        public IReadOnlyDictionary<ActionType, ActionHandlerMeta> HandlerMetas { get; } = snapshot.HandlerMetas;

        public IReadOnlyDictionary<ActionType,
                Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>
            SyncHandlers { get; } = snapshot.SyncHandlers;

        public IReadOnlyDictionary<ActionType,
                Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>
            AsyncHandlers { get; } = snapshot.AsyncHandlers;

        public ActionResponse? ProcessAction(string text, WsContext ctx)
        {
            throw new NotSupportedException("Generated registry perf gates only exercise CheckHandler and delegate dispatch.");
        }

        public Task ShutdownAsync()
        {
            return Task.CompletedTask;
        }
    }
}
