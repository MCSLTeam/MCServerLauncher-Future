using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using MCServerLauncher.Benchmarks.Infrastructure;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Remote.Action;

namespace MCServerLauncher.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[JsonExporterAttribute.Full]
public class ActionDispatchBenchmarks
{
    private DaemonDispatchBenchmarkContext _context = null!;
    private Func<System.Text.Json.JsonElement?, Guid, MCServerLauncher.Daemon.Remote.WsContext, TouchSocket.Core.IResolver, CancellationToken, ActionResponse> _pingHandler = null!;
    private Func<System.Text.Json.JsonElement?, Guid, MCServerLauncher.Daemon.Remote.WsContext, TouchSocket.Core.IResolver, CancellationToken, Task<ActionResponse>> _getSystemInfoHandler = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _context = DaemonDispatchBenchmarkContext.Create();
        _pingHandler = _context.Registry.SyncHandlers[ActionType.Ping];
        _getSystemInfoHandler = _context.Registry.AsyncHandlers[ActionType.GetSystemInfo];
    }

    [Benchmark]
    public ActionResponse DispatchPing()
    {
        var checkedHandler = _context.Executor.CheckHandler(_context.PingRequest, _context.WsContext);
        if (checkedHandler.IsErr(out var errorResponse))
            return errorResponse ?? throw new InvalidOperationException("Ping benchmark permission check unexpectedly failed without a response.");

        return _pingHandler(
            _context.PingRequest.Parameter,
            _context.PingRequest.Id,
            _context.WsContext,
            _context.Resolver,
            CancellationToken.None);
    }

    [Benchmark]
    public Task<ActionResponse> DispatchGetSystemInfo()
    {
        var checkedHandler = _context.Executor.CheckHandler(_context.GetSystemInfoRequest, _context.WsContext);
        if (checkedHandler.IsErr(out var errorResponse))
            return Task.FromResult(errorResponse ?? throw new InvalidOperationException("GetSystemInfo benchmark permission check unexpectedly failed without a response."));

        return _getSystemInfoHandler(
            _context.GetSystemInfoRequest.Parameter,
            _context.GetSystemInfoRequest.Id,
            _context.WsContext,
            _context.Resolver,
            CancellationToken.None);
    }
}
