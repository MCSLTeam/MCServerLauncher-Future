using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using MCServerLauncher.Benchmarks.Infrastructure;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Daemon.Serialization;
using StjJsonSerializer = System.Text.Json.JsonSerializer;

namespace MCServerLauncher.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[JsonExporterAttribute.Full]
public class EventSerializationBenchmarks
{
    private EventPacket _packet = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var fixture = BenchmarkFixtureLoader.LoadJson(
            BenchmarkFixturePaths.EventPacketDir,
            "null-meta-structured-data.json");

        _packet = new EventPacket
        {
            EventType = EventType.DaemonReport,
            EventMeta = null,
            EventData = new JsonPayloadBuffer(fixture.GetProperty("data").Clone()),
            Timestamp = fixture.GetProperty("time").GetInt64()
        };
    }

    [Benchmark]
    public string SerializeEventPacket()
    {
        return StjJsonSerializer.Serialize(_packet, DaemonRpcJsonBoundary.StjOptions);
    }
}
