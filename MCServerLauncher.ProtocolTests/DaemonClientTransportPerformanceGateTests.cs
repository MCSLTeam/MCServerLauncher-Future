using System.Text;
using System.Text.Json;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.DaemonClient.Connection;
using MCServerLauncher.Daemon.Serialization;
using MCServerLauncher.DaemonClient.WebSocketPlugin;
using MCServerLauncher.ProtocolTests.Fixtures.Rpc;
using MCServerLauncher.ProtocolTests.Helpers;
using StjJsonSerializer = System.Text.Json.JsonSerializer;

namespace MCServerLauncher.ProtocolTests;

/// <summary>
/// DaemonClient seam-level performance gates and transport-path instrumentation checks.
/// Task 3 started these as placeholder scaffolds; Task 8 keeps the suite in-process and recalibrates
/// baselines to the stabilized instrumented seam path measured by this xUnit harness. BenchmarkDotNet runs remain
/// a separate evidence stream and are not used as direct numeric substitutes for these gate medians.
/// </summary>
[Collection("PerformanceGate")]
public class DaemonClientTransportPerformanceGateTests
{
    private static readonly Guid FixedRequestId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FixedResponseId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private const double MaxRegressionRatio = 0.20;
    private const int WarmupSamples = 4;
    private const int MeasuredSamples = 7;

    private static readonly ClientPathBaseline OutboundRequestSerializeBaseline = new(
        NanosecondsPerOperation: 3_500,
        AllocatedBytesPerOperation: 800);

    private static readonly ClientPathBaseline InboundActionResponseParseBaseline = new(
        NanosecondsPerOperation: 3_400,
        AllocatedBytesPerOperation: 750);

    private static readonly ClientPathBaseline ClientActionRoundTripBaseline = new(
        NanosecondsPerOperation: 7_800,
        AllocatedBytesPerOperation: 2_100);

    private static readonly ClientPathBaseline DaemonEventRoundTripBaseline = new(
        NanosecondsPerOperation: 5_100,
        AllocatedBytesPerOperation: 1_500);

    [Fact]
    [Trait("Category", "Perf")]
    [Trait("Category", "DaemonClientPerformanceGate")]
    public void Instrumentation_ClientActionRoundTripReplay_CapturesOutboundAndInboundTransportSeams()
    {
        var request = CreatePingRequest();
        var response = CreateSuccessResponse();
        var collector = new DaemonClientTransportInstrumentationCollector();

        using (DaemonClientTransportInstrumentationScope.Push(collector))
        {
            var result = RunClientActionRoundTrip(request, response);
            Assert.Equal(FixedResponseId, result);
        }

        var snapshot = collector.Snapshot();
        Assert.Equal(1, snapshot.OutboundSerializeSampleCount);
        Assert.Equal(1, snapshot.InboundActionResponseParseSampleCount);
        Assert.Equal(0, snapshot.InboundEventPacketParseSampleCount);
        Assert.Equal(0, snapshot.EventDataMaterializationSampleCount);
        Assert.True(snapshot.OutboundSerializeTotalBytes > 0);
        Assert.True(snapshot.InboundActionResponseTotalBytes > 0);
    }

    [Fact]
    [Trait("Category", "Perf")]
    [Trait("Category", "DaemonClientPerformanceGate")]
    public void Instrumentation_DaemonEventRoundTripReplay_CapturesInboundEventAndMaterializationSeams()
    {
        var packet = CreateInstanceLogPacket();
        var collector = new DaemonClientTransportInstrumentationCollector();

        using (DaemonClientTransportInstrumentationScope.Push(collector))
        {
            var result = RunDaemonEventRoundTrip(packet);
            Assert.NotEmpty(result);
        }

        var snapshot = collector.Snapshot();
        Assert.Equal(0, snapshot.OutboundSerializeSampleCount);
        Assert.Equal(0, snapshot.InboundActionResponseParseSampleCount);
        Assert.Equal(1, snapshot.InboundEventPacketParseSampleCount);
        Assert.Equal(1, snapshot.EventDataMaterializationSampleCount);
        Assert.True(snapshot.InboundEventPacketTotalBytes > 0);
    }

    [Fact]
    [Trait("Category", "Perf")]
    [Trait("Category", "PerformanceGate")]
    [Trait("Category", "AllocationGate")]
    [Trait("Category", "DaemonClientPerformanceGate")]
    public void PerfGate_DaemonClientOutboundRequestSerialize_DoesNotRegressMoreThanTwentyPercent()
    {
        var request = CreateSaveEventRulesRequest();
        if (SerializeActionRequestForTransport(request).Length == 0)
            throw new InvalidOperationException("DaemonClient outbound perf gate precheck serialized an empty payload.");

        const int operationsPerSample = 4_000;
        var measurement = PerformanceGateHarness.Measure(
            operation: () =>
            {
                if (SerializeActionRequestForTransport(request).Length == 0)
                    throw new InvalidOperationException("DaemonClient outbound perf sample serialized an empty payload.");
            },
            operationsPerSample: operationsPerSample,
            warmupSamples: WarmupSamples,
            measuredSamples: MeasuredSamples);

        AssertWithinThreshold("daemonclient outbound request serialize", measurement, OutboundRequestSerializeBaseline);
    }

    [Fact]
    [Trait("Category", "Perf")]
    [Trait("Category", "PerformanceGate")]
    [Trait("Category", "AllocationGate")]
    [Trait("Category", "DaemonClientPerformanceGate")]
    public void PerfGate_DaemonClientInboundActionResponseParse_DoesNotRegressMoreThanTwentyPercent()
    {
        var actionResponseJson = File.ReadAllText(Path.Combine(RpcFixturePaths.ActionResponseDir, "success-typed-data.json"));
        if (ParseActionResponse(actionResponseJson).Id == Guid.Empty)
            throw new InvalidOperationException("DaemonClient inbound perf gate precheck produced an empty correlation id.");

        const int operationsPerSample = 4_000;
        var measurement = PerformanceGateHarness.Measure(
            operation: () =>
            {
                if (ParseActionResponse(actionResponseJson).Id == Guid.Empty)
                    throw new InvalidOperationException("DaemonClient inbound perf sample produced an empty correlation id.");
            },
            operationsPerSample: operationsPerSample,
            warmupSamples: WarmupSamples,
            measuredSamples: MeasuredSamples);

        AssertWithinThreshold("daemonclient inbound action response parse", measurement, InboundActionResponseParseBaseline);
    }

    [Fact]
    [Trait("Category", "Perf")]
    [Trait("Category", "PerformanceGate")]
    [Trait("Category", "AllocationGate")]
    [Trait("Category", "DaemonClientPerformanceGate")]
    public void PerfGate_DaemonClientActionRoundTripReplay_DoesNotRegressMoreThanTwentyPercent()
    {
        var request = CreatePingRequest();
        var response = CreateSuccessResponse();
        if (RunClientActionRoundTrip(request, response) == Guid.Empty)
            throw new InvalidOperationException("DaemonClient action round-trip perf gate precheck produced an empty response id.");

        const int operationsPerSample = 2_500;
        var measurement = PerformanceGateHarness.Measure(
            operation: () =>
            {
                if (RunClientActionRoundTrip(request, response) == Guid.Empty)
                    throw new InvalidOperationException("DaemonClient action round-trip perf sample produced an empty response id.");
            },
            operationsPerSample: operationsPerSample,
            warmupSamples: WarmupSamples,
            measuredSamples: MeasuredSamples);

        AssertWithinThreshold("daemonclient action round-trip replay", measurement, ClientActionRoundTripBaseline);
    }

    [Fact]
    [Trait("Category", "Perf")]
    [Trait("Category", "PerformanceGate")]
    [Trait("Category", "AllocationGate")]
    [Trait("Category", "DaemonClientPerformanceGate")]
    public void PerfGate_DaemonEventToClientRoundTripReplay_DoesNotRegressMoreThanTwentyPercent()
    {
        var packet = CreateInstanceLogPacket();
        if (RunDaemonEventRoundTrip(packet).Length == 0)
            throw new InvalidOperationException("Daemon event round-trip perf gate precheck produced an empty log payload.");

        const int operationsPerSample = 3_000;
        var measurement = PerformanceGateHarness.Measure(
            operation: () =>
            {
                if (RunDaemonEventRoundTrip(packet).Length == 0)
                    throw new InvalidOperationException("Daemon event round-trip perf sample produced an empty log payload.");
            },
            operationsPerSample: operationsPerSample,
            warmupSamples: WarmupSamples,
            measuredSamples: MeasuredSamples);

        AssertWithinThreshold("daemon event to client round-trip replay", measurement, DaemonEventRoundTripBaseline);
    }

    private static ActionRequest CreatePingRequest()
    {
        return new ActionRequest
        {
            ActionType = ActionType.Ping,
            Parameter = ParseJsonElement("{}"),
            Id = FixedRequestId
        };
    }

    private static ActionRequest CreateSaveEventRulesRequest()
    {
        var fixture = FixtureHarness.LoadFixture(RpcFixturePaths.ActionRequestDir, "save-event-rules-nested-parameter.json");
        return new ActionRequest
        {
            ActionType = ActionType.SaveEventRules,
            Parameter = fixture.GetProperty("params").Clone(),
            Id = FixedRequestId
        };
    }

    private static ActionResponse CreateSuccessResponse()
    {
        var fixture = FixtureHarness.LoadFixture(RpcFixturePaths.ActionResponseDir, "success-typed-data.json");
        return new ActionResponse
        {
            RequestStatus = ActionRequestStatus.Ok,
            Retcode = ActionRetcode.Ok.Code,
            Message = ActionRetcode.Ok.Message,
            Data = fixture.GetProperty("data").Clone(),
            Id = FixedResponseId
        };
    }

    private static EventPacket CreateInstanceLogPacket()
    {
        var fixture = FixtureHarness.LoadFixture(RpcFixturePaths.EventPacketDir, "with-meta-and-data.json");
        return new EventPacket
        {
            EventType = EventType.InstanceLog,
            EventMeta = new JsonPayloadBuffer(fixture.GetProperty("meta").Clone()),
            EventData = new JsonPayloadBuffer(fixture.GetProperty("data").Clone()),
            Timestamp = fixture.GetProperty("time").GetInt64()
        };
    }

    private static Guid RunClientActionRoundTrip(ActionRequest request, ActionResponse response)
    {
        var requestWireJson = Encoding.UTF8.GetString(SerializeActionRequestForTransport(request));
        var parsedRequest = ActionExecutorExtensions.ParseRequest(null!, requestWireJson);
        if (!parsedRequest.IsOk(out var daemonRequest) || daemonRequest.ActionType != ActionType.Ping)
            throw new InvalidOperationException("DaemonClient action round-trip replay failed at the daemon request parse seam.");

        var responseWireJson = StjJsonSerializer.Serialize(response, DaemonRpcJsonBoundary.StjOptions);
        var parsedResponse = ParseActionResponse(responseWireJson);
        if (parsedResponse.RequestStatus != ActionRequestStatus.Ok)
            throw new InvalidOperationException("DaemonClient action round-trip replay failed at the client response parse seam.");

        return parsedResponse.Id;
    }

    private static string RunDaemonEventRoundTrip(EventPacket packet)
    {
        var wireJson = StjJsonSerializer.Serialize(packet, DaemonRpcJsonBoundary.StjOptions);
        var parsedPacket = ParseEventPacket(wireJson);
        var data = MaterializeEventData(parsedPacket.EventType, parsedPacket.EventData);
        if (data is not InstanceLogEventData instanceLog)
            throw new InvalidOperationException("Daemon event round-trip replay failed to materialize an instance-log payload.");

        return instanceLog.Log;
    }

    private static byte[] SerializeActionRequestForTransport(ActionRequest request)
    {
        return ClientConnection.SerializeActionRequestForTransport(request);
    }

    private static ActionResponse ParseActionResponse(string received)
    {
        return WsReceivedPlugin.ParseActionResponse(received);
    }

    private static EventPacket ParseEventPacket(string received)
    {
        return WsReceivedPlugin.ParseEventPacket(received);
    }

    private static IEventData? MaterializeEventData(EventType eventType, JsonPayloadBuffer? data)
    {
        return WsReceivedPlugin.MaterializeEventData(eventType, data);
    }

    private static JsonElement ParseJsonElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static void AssertWithinThreshold(string scenarioName, PerformanceMeasurement actual, ClientPathBaseline baseline)
    {
        var maxTime = baseline.NanosecondsPerOperation * (1 + MaxRegressionRatio);
        var maxAlloc = baseline.AllocatedBytesPerOperation * (1 + MaxRegressionRatio);

        Assert.True(
            actual.NanosecondsPerOperation <= maxTime,
            $"Perf regression for {scenarioName}: {actual.NanosecondsPerOperation:F2} ns/op > {maxTime:F2} ns/op (baseline {baseline.NanosecondsPerOperation:F2}, threshold {MaxRegressionRatio:P0}).");

        Assert.True(
            actual.AllocatedBytesPerOperation <= maxAlloc,
            $"Allocation regression for {scenarioName}: {actual.AllocatedBytesPerOperation:F2} B/op > {maxAlloc:F2} B/op (baseline {baseline.AllocatedBytesPerOperation:F2}, threshold {MaxRegressionRatio:P0}).");
    }

    private readonly record struct ClientPathBaseline(double NanosecondsPerOperation, double AllocatedBytesPerOperation);
}
