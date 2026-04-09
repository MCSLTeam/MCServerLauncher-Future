using System.Text.Json;
using System.Text;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Serialization;
using MCServerLauncher.ProtocolTests.Fixtures.Persistence;
using MCServerLauncher.ProtocolTests.Fixtures.Rpc;
using MCServerLauncher.ProtocolTests.Helpers;
using StjJsonSerializer = System.Text.Json.JsonSerializer;

namespace MCServerLauncher.ProtocolTests;

[Collection("PerformanceGate")]
public class DaemonTransportAndPersistencePerformanceGateTests
{
    private static readonly Type InstanceConfigType =
        Type.GetType("MCServerLauncher.Common.ProtoType.Instance.InstanceConfig, MCServerLauncher.Common", throwOnError: true)!;

    private static readonly Guid FixedResponseId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private const double MaxRegressionRatio = 0.05;
    private const int WarmupSamples = 4;
    private const int MeasuredSamples = 7;

    private static readonly HotPathBaseline ActionRequestParseBaseline = new(
        NanosecondsPerOperation: 15_100,
        AllocatedBytesPerOperation: 1_672);

    private static readonly HotPathBaseline ActionResponseSerializeBaseline = new(
        NanosecondsPerOperation: 2_400,
        AllocatedBytesPerOperation: 720);

    private static readonly HotPathBaseline EventPacketSerializeBaseline = new(
        NanosecondsPerOperation: 4_100,
        AllocatedBytesPerOperation: 1_680);

    private static readonly HotPathBaseline PersistenceReadWriteBaseline = new(
        NanosecondsPerOperation: 73_000,
        AllocatedBytesPerOperation: 16_700);

    [Fact]
    [Trait("Category", "Perf")]
    [Trait("Category", "PerformanceGate")]
    [Trait("Category", "AllocationGate")]
    public void PerfGate_DaemonInboundActionParse_DoesNotRegressMoreThanFivePercent()
    {
        var actionRequestJson = File.ReadAllText(Path.Combine(RpcFixturePaths.ActionRequestDir, "save-event-rules-nested-parameter.json"));
        var actionRequestUtf8 = Encoding.UTF8.GetBytes(actionRequestJson);
        const int operationsPerSample = 1500;

        var precheck = ActionExecutorExtensions.ParseRequest(null!, actionRequestUtf8);
        Assert.True(precheck.IsOk(out _));

        var measurement = PerformanceGateHarness.Measure(
            operation: () =>
            {
                var result = ActionExecutorExtensions.ParseRequest(null!, actionRequestUtf8);
                if (!result.IsOk(out _))
                    throw new InvalidOperationException("Inbound action parse replay sample failed unexpectedly");
            },
            operationsPerSample: operationsPerSample,
            warmupSamples: WarmupSamples,
            measuredSamples: MeasuredSamples);

        AssertWithinThreshold("daemon inbound action parse", measurement, ActionRequestParseBaseline);
    }

    [Fact]
    [Trait("Category", "Perf")]
    [Trait("Category", "PerformanceGate")]
    [Trait("Category", "AllocationGate")]
    public void PerfGate_DaemonOutboundActionResponseSerialize_DoesNotRegressMoreThanFivePercent()
    {
        var fixture = FixtureHarness.LoadFixture(RpcFixturePaths.ActionResponseDir, "success-typed-data.json");
        var response = new ActionResponse
        {
            RequestStatus = ActionRequestStatus.Ok,
            Retcode = ActionRetcode.Ok.Code,
            Message = ActionRetcode.Ok.Message,
            Data = fixture.GetProperty("data"),
            Id = FixedResponseId
        };

        const int operationsPerSample = 3000;
        var measurement = PerformanceGateHarness.Measure(
            operation: () =>
            {
                var json = StjJsonSerializer.Serialize(response, DaemonRpcJsonBoundary.StjOptions);
                if (json.Length == 0)
                    throw new InvalidOperationException("Outbound action response replay sample serialized empty payload");
            },
            operationsPerSample: operationsPerSample,
            warmupSamples: WarmupSamples,
            measuredSamples: MeasuredSamples);

        AssertWithinThreshold("daemon outbound action response serialize", measurement, ActionResponseSerializeBaseline);
    }

    [Fact]
    [Trait("Category", "Perf")]
    [Trait("Category", "PerformanceGate")]
    [Trait("Category", "AllocationGate")]
    public void PerfGate_DaemonOutboundEventPacketSerialize_DoesNotRegressMoreThanFivePercent()
    {
        var eventPacketFixture = FixtureHarness.LoadFixture(RpcFixturePaths.EventPacketDir, "null-meta-structured-data.json");
        var packet = new EventPacket
        {
            EventType = EventType.DaemonReport,
            EventMeta = null,
            EventData = new JsonPayloadBuffer(eventPacketFixture.GetProperty("data")),
            Timestamp = eventPacketFixture.GetProperty("time").GetInt64()
        };

        const int operationsPerSample = 2500;
        var measurement = PerformanceGateHarness.Measure(
            operation: () =>
            {
                var json = StjJsonSerializer.Serialize(packet, DaemonRpcJsonBoundary.StjOptions);
                if (json.Length == 0)
                    throw new InvalidOperationException("Outbound event packet replay sample serialized empty payload");
            },
            operationsPerSample: operationsPerSample,
            warmupSamples: WarmupSamples,
            measuredSamples: MeasuredSamples);

        AssertWithinThreshold("daemon outbound event packet serialize", measurement, EventPacketSerializeBaseline);
    }

    [Fact]
    [Trait("Category", "Perf")]
    [Trait("Category", "PerformanceGate")]
    [Trait("Category", "AllocationGate")]
    public void PerfGate_PersistenceReadWriteRepresentative_DoesNotRegressMoreThanFivePercent()
    {
        var fixturePath = Path.Combine(PersistenceFixturePaths.InstanceConfigDir, "event-rule-heavy-daemon-instance.json");
        var fixtureJson = File.ReadAllText(fixturePath);
        var instanceConfig = DeserializePersistenceFixture(fixtureJson);

        var precheckSerialized = JsonSerializer.Serialize(instanceConfig, InstanceConfigType, DaemonPersistenceJsonBoundary.StjWriteIndentedOptions);
        Assert.NotEmpty(precheckSerialized);

        const int operationsPerSample = 500;
        var measurement = PerformanceGateHarness.Measure(
            operation: () =>
            {
                var loaded = JsonSerializer.Deserialize(fixtureJson, InstanceConfigType, DaemonPersistenceJsonBoundary.StjOptions);
                if (loaded is null)
                    throw new InvalidOperationException("Persistence replay sample read returned null");

                var serialized = JsonSerializer.Serialize(loaded, InstanceConfigType, DaemonPersistenceJsonBoundary.StjWriteIndentedOptions);
                if (serialized.Length == 0)
                    throw new InvalidOperationException("Persistence replay sample write serialized empty payload");
            },
            operationsPerSample: operationsPerSample,
            warmupSamples: WarmupSamples,
            measuredSamples: MeasuredSamples);

        AssertWithinThreshold("persistence representative read/write", measurement, PersistenceReadWriteBaseline);
    }

    private static object DeserializePersistenceFixture(string fixtureJson)
    {
        var value = JsonSerializer.Deserialize(fixtureJson, InstanceConfigType, DaemonPersistenceJsonBoundary.StjOptions);
        return value ?? throw new InvalidOperationException("Failed to deserialize persistence fixture instance config");
    }

    private static void AssertWithinThreshold(string scenarioName, PerformanceMeasurement actual, HotPathBaseline baseline)
    {
        var maxTime = baseline.NanosecondsPerOperation * (1 + MaxRegressionRatio);
        var maxAlloc = baseline.AllocatedBytesPerOperation * (1 + MaxRegressionRatio);

        Assert.True(
            actual.NanosecondsPerOperation <= maxTime,
            $"Perf regression for {scenarioName}: {actual.NanosecondsPerOperation:F2} ns/op > {maxTime:F2} ns/op (baseline {baseline.NanosecondsPerOperation:F2}, threshold {MaxRegressionRatio:P0})");

        Assert.True(
            actual.AllocatedBytesPerOperation <= maxAlloc,
            $"Allocation regression for {scenarioName}: {actual.AllocatedBytesPerOperation:F2} B/op > {maxAlloc:F2} B/op (baseline {baseline.AllocatedBytesPerOperation:F2}, threshold {MaxRegressionRatio:P0})");
    }

    private readonly record struct HotPathBaseline(double NanosecondsPerOperation, double AllocatedBytesPerOperation);
}
