using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Common.ProtoType.Instance;
using StjJsonSerializer = System.Text.Json.JsonSerializer;

namespace MCServerLauncher.Benchmarks.Benchmarks;

/// <summary>
/// Measures source-generated event serialization and in-memory fan-out only.
/// It intentionally does not model the Phase 4 WebSocket writer or transport queues.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
[JsonExporterAttribute.Full]
public class ProtocolEventFanOutSerializationBenchmarks
{
    private InstanceCatalogChangedEventData _eventData = null!;
    private FanOutSink[] _sinks = null!;

    [Params(1, 8, 32)]
    public int Subscribers { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var instanceId = Guid.Parse("c8a9d4ee-27f3-4ba0-97be-1eb05c5952d4");
        var snapshot = new InstanceCatalogItem(
            instanceId,
            "Benchmark instance",
            InstanceType.MCJava,
            "1.21.8",
            InstanceStatus.Running,
            readyTimedOut: false);
        _eventData = new InstanceCatalogChangedEventData(
            1,
            InstanceCatalogChangeOperation.Upsert,
            instanceId,
            snapshot);
        _sinks = Enumerable.Range(0, Subscribers).Select(static _ => new FanOutSink()).ToArray();

        if (StjJsonSerializer.SerializeToUtf8Bytes(
                _eventData,
                BuiltInProtocolJsonContext.Default.InstanceCatalogChangedEventData).Length == 0)
        {
            throw new InvalidOperationException("Event serialization benchmark precheck produced an empty payload.");
        }
    }

    /// <summary>
    /// One source-generated serialization produces one byte array, then every sink receives the same memory instance.
    /// </summary>
    [Benchmark]
    public int SerializeOnceThenFanOut()
    {
        ReadOnlyMemory<byte> payload = StjJsonSerializer.SerializeToUtf8Bytes(
            _eventData,
            BuiltInProtocolJsonContext.Default.InstanceCatalogChangedEventData);
        var total = 0;
        foreach (var sink in _sinks)
            total += sink.Accept(payload);
        return total;
    }

    /// <summary>
    /// Allocation-growth baseline: each subscriber receives its own source-generated serialized byte array.
    /// </summary>
    [Benchmark(Baseline = true)]
    public int SerializePerSubscriberThenFanOut()
    {
        var total = 0;
        foreach (var sink in _sinks)
        {
            ReadOnlyMemory<byte> payload = StjJsonSerializer.SerializeToUtf8Bytes(
                _eventData,
                BuiltInProtocolJsonContext.Default.InstanceCatalogChangedEventData);
            total += sink.Accept(payload);
        }

        return total;
    }

    private sealed class FanOutSink
    {
        private int _lastLength;

        internal int Accept(ReadOnlyMemory<byte> payload)
        {
            _lastLength = payload.Length;
            return _lastLength;
        }
    }
}
