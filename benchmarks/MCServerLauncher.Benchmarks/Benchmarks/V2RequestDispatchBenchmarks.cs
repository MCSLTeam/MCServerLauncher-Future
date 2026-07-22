using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Benchmarks.Infrastructure;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Dispatch;

namespace MCServerLauncher.Benchmarks.Benchmarks;

/// <summary>
/// Measures the same dispatch boundary as the Phase 0 V1 request benchmark:
/// catalog lookup, notification and permission admission, params
/// validation/materialization, erased binding invocation, result validation,
/// and success envelope construction. Socket framing, request parsing, and
/// final response serialization are separate transport concerns, just as they
/// were for the V1 baseline.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
[JsonExporterAttribute.Full]
public class V2RequestDispatchBenchmarks
{
    private V2RpcDispatcher _dispatcher = null!;
    private V2RpcConnectionContext _connection = null!;
    private JsonRpcRequestEnvelope _pingRequest = null!;
    private JsonRpcRequestEnvelope _discoverRequest = null!;
    private ReadOnlyMemory<byte> _discoverRequestUtf8;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _dispatcher = new V2RpcDispatcher(
            BenchmarkProtocolCatalogFactory.Create(),
            BenchmarkDiagnosticSink.Instance);
        _connection = new V2RpcConnectionContext(
            new BenchmarkPermissionView(ImmutableArray.Create("*")),
            null,
            CancellationToken.None);
        _pingRequest = new JsonRpcRequestEnvelope(
            "mcsl.daemon.ping",
            JsonRpcRequestId.FromInt64(1),
            JsonRpcObjectPayload.From(new EmptyRequest(), BuiltInProtocolJsonContext.Default.EmptyRequest));
        _discoverRequest = new JsonRpcRequestEnvelope(
            "rpc.discover",
            JsonRpcRequestId.FromInt64(1),
            JsonRpcObjectPayload.From(new EmptyRequest(), BuiltInProtocolJsonContext.Default.EmptyRequest));
        _discoverRequestUtf8 =
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"rpc.discover\",\"params\":{}}"u8.ToArray();

        var validation = _dispatcher
            .DispatchParsedAsync(_pingRequest, _connection, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        if (validation.SuccessResponse?.Result.Deserialize(BuiltInProtocolJsonContext.Default.PingResult) is not PingResult)
        {
            throw new InvalidOperationException("The benchmark dispatch did not produce a typed ping success response.");
        }

        var discoverValidation = _dispatcher
            .DispatchParsedAsync(_discoverRequest, _connection, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        if (discoverValidation.SuccessResponse?.Result.Deserialize(
                BuiltInProtocolJsonContext.Default.OpenRpcDocument) is not OpenRpcDocument)
        {
            throw new InvalidOperationException("The benchmark dispatch did not produce a typed discover success response.");
        }
    }

    [Benchmark]
    public Task DispatchPing() =>
        _dispatcher.DispatchParsedAsync(_pingRequest, _connection, CancellationToken.None);

    [Benchmark]
    public Task DispatchDiscover() =>
        _dispatcher.DispatchParsedAsync(_discoverRequest, _connection, CancellationToken.None);

    [Benchmark]
    public Task DispatchDiscoverWire() =>
        _dispatcher.DispatchAsync(_discoverRequestUtf8, _connection, CancellationToken.None);

    private sealed class BenchmarkPermissionView : ICompiledProtocolPermissionView
    {
        internal BenchmarkPermissionView(ImmutableArray<string> permissions)
        {
            Permissions = permissions;
            CompiledPermissions = new Permissions(permissions.ToArray());
        }

        public ImmutableArray<string> Permissions { get; }

        public string Subject => "benchmark";

        public bool IsMainToken => true;

        public Permissions CompiledPermissions { get; }
    }

    private sealed class BenchmarkDiagnosticSink : IV2RpcDiagnosticSink
    {
        internal static BenchmarkDiagnosticSink Instance { get; } = new();

        public void RecordUnexpected(V2RpcUnexpectedDiagnostic diagnostic) =>
            throw new InvalidOperationException("The benchmark dispatch produced an unexpected diagnostic.");

        public void RecordNotificationSuppressed(V2RpcNotificationSuppressionDiagnostic diagnostic) =>
            throw new InvalidOperationException("The benchmark dispatch suppressed its request.");
    }
}
