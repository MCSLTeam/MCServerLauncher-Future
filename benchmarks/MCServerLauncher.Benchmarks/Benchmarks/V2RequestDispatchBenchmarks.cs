using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using MCServerLauncher.Benchmarks.Infrastructure;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;

namespace MCServerLauncher.Benchmarks.Benchmarks;

/// <summary>
/// Measures the definition-derived V2 request binding without a socket.
/// Transport and serialization have separate benchmarks; this isolates the
/// dispatch work that is comparable to the Phase 0 request-dispatch baseline.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
[JsonExporterAttribute.Full]
public class V2RequestDispatchBenchmarks
{
    private RpcBinding<EmptyRequest, PingResult> _ping = null!;
    private ProtocolInvocationContext _context = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var catalog = BenchmarkProtocolCatalogFactory.Create();
        _ping = (RpcBinding<EmptyRequest, PingResult>)catalog.Rpcs[
            new RpcMethod("mcsl.daemon.ping")].Binding;
        _context = new ProtocolInvocationContext(ProtocolExecutionOwner.BuiltIn);
    }

    [Benchmark]
    public async Task<long> DispatchPing()
    {
        var execution = await _ping.Handler(_context, new EmptyRequest(), CancellationToken.None);
        if (!execution.Result.IsOk(out var result))
            throw new InvalidOperationException("The benchmark ping binding returned an error.");

        return result!.Time;
    }
}
