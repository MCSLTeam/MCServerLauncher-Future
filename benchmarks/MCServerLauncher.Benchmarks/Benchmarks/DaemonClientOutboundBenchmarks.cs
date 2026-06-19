using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using MCServerLauncher.Benchmarks.Infrastructure;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.DaemonClient.Connection;
using TouchSocket.Core;
using TouchSocket.Http.WebSockets;

namespace MCServerLauncher.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[JsonExporterAttribute.Full]
public class DaemonClientOutboundBenchmarks
{
    private static readonly Guid FixedRequestId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private ActionRequest _pingRequest = null!;
    private ActionRequest _subscribeEventConcreteMetaRequest = null!;
    private ActionRequest _saveEventRulesRequest = null!;
    private byte[] _largeSaveEventRulesWirePayload = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _pingRequest = new ActionRequest
        {
            ActionType = ActionType.Ping,
            Parameter = BenchmarkFixtureLoader.ParseElement("{}"),
            Id = FixedRequestId
        };

        _subscribeEventConcreteMetaRequest = new ActionRequest
        {
            ActionType = ActionType.SubscribeEvent,
            Parameter = BenchmarkFixtureLoader.LoadJson(
                BenchmarkFixturePaths.ActionRequestDir,
                "subscribe-event-concrete-meta.json").GetProperty("params").Clone(),
            Id = FixedRequestId
        };

        _saveEventRulesRequest = new ActionRequest
        {
            ActionType = ActionType.SaveEventRules,
            Parameter = BenchmarkFixtureLoader.LoadJson(
                BenchmarkFixturePaths.ActionRequestDir,
                "save-event-rules-nested-parameter.json").GetProperty("params").Clone(),
            Id = FixedRequestId
        };

        if (SerializeActionRequestForTransport(_pingRequest).Length == 0)
            throw new InvalidOperationException("DaemonClient outbound ping benchmark precheck serialized an empty payload.");

        if (SerializeActionRequestForTransport(_subscribeEventConcreteMetaRequest).Length == 0)
            throw new InvalidOperationException("DaemonClient outbound subscribe-event benchmark precheck serialized an empty payload.");

        if (SerializeActionRequestForTransport(_saveEventRulesRequest).Length == 0)
            throw new InvalidOperationException("DaemonClient outbound save-event-rules benchmark precheck serialized an empty payload.");

        var largeParamsJson = $$"""
        {
          "blob": "{{new string('x', 64 * 1024)}}"
        }
        """;

        var largePayloadRequest = new ActionRequest
        {
            ActionType = ActionType.SaveEventRules,
            Parameter = BenchmarkFixtureLoader.ParseElement(largeParamsJson),
            Id = FixedRequestId
        };

        _largeSaveEventRulesWirePayload = SerializeActionRequestForTransport(largePayloadRequest);
        if (_largeSaveEventRulesWirePayload.Length < 64 * 1024)
            throw new InvalidOperationException("DaemonClient outbound large-payload benchmark precheck produced an unexpectedly small payload.");
    }

    [Benchmark]
    public int SerializePingActionRequestForTransport()
    {
        return SerializeActionRequestForTransport(_pingRequest).Length;
    }

    [Benchmark]
    public int SerializeSubscribeEventConcreteMetaActionRequestForTransport()
    {
        return SerializeActionRequestForTransport(_subscribeEventConcreteMetaRequest).Length;
    }

    [Benchmark]
    public int SerializeSaveEventRulesActionRequestForTransport()
    {
        return SerializeActionRequestForTransport(_saveEventRulesRequest).Length;
    }

    [Benchmark]
    public int SerializeLargeSaveEventRulesActionRequestForTransport_BaselineFrame()
    {
        return BuildBaselineTextFramePayloadLength(_largeSaveEventRulesWirePayload);
    }

    [Benchmark]
    public int SerializeLargeSaveEventRulesActionRequestForTransport_ByteBlockFrame()
    {
        return BuildByteBlockTextFramePayloadLength(_largeSaveEventRulesWirePayload);
    }

    private static byte[] SerializeActionRequestForTransport(ActionRequest request)
    {
        return ClientConnection.SerializeActionRequestForTransport(request);
    }

    private static int BuildBaselineTextFramePayloadLength(ReadOnlyMemory<byte> utf8Payload)
    {
        return new WSDataFrame(utf8Payload)
        {
            FIN = true,
            Opcode = WSDataType.Text
        }.PayloadData.Length;
    }

    private static int BuildByteBlockTextFramePayloadLength(ReadOnlyMemory<byte> utf8Payload)
    {
        using var byteBlock = new ByteBlock(utf8Payload.Length);
        byteBlock.Write(utf8Payload.Span);

        return new WSDataFrame(byteBlock.Memory)
        {
            FIN = true,
            Opcode = WSDataType.Text
        }.PayloadData.Length;
    }
}
