using System.Threading;
using System.Text.Json.Serialization;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using RustyOptions;

namespace MCServerLauncher.PluginFixtures.StartHanging;

public sealed class NeverCompletingStartPlugin : IGeneratedDaemonPluginAdapter
{
    public static PluginAdapterMetadata Metadata { get; } = new(
        "fixture.start-never-completes",
        "1.0.0",
        "PluginEntry.dll",
        "MCServerLauncher.PluginFixtures.StartHanging.NeverCompletingStartPlugin",
        "[2.0.0, 3.0.0)",
        ["event.publish", "instance.query", "rpc.register"],
        "514ef77f364b028f43471542b8b2455804d0f2a286b2acdd59f3fece4fae5f76");

    public Result<Unit, DaemonError> Configure(IPluginContext context)
    {
        var rpcResult = context.Rpc.Register(
            NeverCompletingStartProtocol.Rpc,
            static (_, _) => Task.FromResult(PluginResult.Ok<UnitResult>(new UnitResult())));
        if (rpcResult.IsErr(out var rpcError))
            return Result.Err<Unit, DaemonError>(rpcError!);

        var eventResult = context.Events.Register(NeverCompletingStartProtocol.Changed);
        return eventResult.IsErr(out var eventError)
            ? Result.Err<Unit, DaemonError>(eventError!)
            : PluginResult.Ok();
    }

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return new TaskCompletionSource<Result<Unit, DaemonError>>(
            TaskCreationOptions.RunContinuationsAsynchronously).Task;
    }

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}

public sealed class BlockingLifetimeCancellationPlugin : IGeneratedDaemonPluginAdapter
{
    public static PluginAdapterMetadata Metadata { get; } = new(
        "fixture.start-blocking-lifetime-cancellation",
        "1.0.0",
        "PluginEntry.dll",
        "MCServerLauncher.PluginFixtures.StartHanging.BlockingLifetimeCancellationPlugin",
        "[2.0.0, 3.0.0)",
        ["event.publish", "instance.query", "rpc.register"],
        "bcf1feaf105224149502fe70060faa4a3954b971f2fe5cc745b9a19267832354");

    private IPluginContext? _context;

    public Result<Unit, DaemonError> Configure(IPluginContext context)
    {
        _context = context;
        return PluginResult.Ok();
    }

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _context!.LifetimeToken.Register(static () => Thread.Sleep(TimeSpan.FromMilliseconds(100)));
        return new TaskCompletionSource<Result<Unit, DaemonError>>(
            TaskCreationOptions.RunContinuationsAsynchronously).Task;
    }

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}

public sealed class BlockingStartCancellationPlugin : IGeneratedDaemonPluginAdapter
{
    public static PluginAdapterMetadata Metadata { get; } = new(
        "fixture.start-blocking-start-cancellation",
        "1.0.0",
        "PluginEntry.dll",
        "MCServerLauncher.PluginFixtures.StartHanging.BlockingStartCancellationPlugin",
        "[2.0.0, 3.0.0)",
        ["event.publish", "instance.query", "rpc.register"],
        "63caee1c2b3b180aa738483b1d00d638ed5b622e8c3d9dc8a9b586486224af4f");

    public Result<Unit, DaemonError> Configure(IPluginContext context) => PluginResult.Ok();

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.Register(static () => Thread.Sleep(TimeSpan.FromMilliseconds(100)));
        return new TaskCompletionSource<Result<Unit, DaemonError>>(
            TaskCreationOptions.RunContinuationsAsynchronously).Task;
    }

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}

public sealed class IgnoresCancellationStartPlugin : IGeneratedDaemonPluginAdapter
{
    public static PluginAdapterMetadata Metadata { get; } = new(
        "fixture.start-ignores-cancellation",
        "1.0.0",
        "PluginEntry.dll",
        "MCServerLauncher.PluginFixtures.StartHanging.IgnoresCancellationStartPlugin",
        "[2.0.0, 3.0.0)",
        ["event.publish", "instance.query", "rpc.register"],
        "b2e1500368a2ad24479e84004459691d5f7a68da14c1c81237871c105bd7aa91");

    public Result<Unit, DaemonError> Configure(IPluginContext context) => PluginResult.Ok();

    public async Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        // Stay incomplete forever so Task.Delay-based host supervision cannot lose to a short
        // delayed success under timer/thread-pool pressure on CI.
        await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
        return PluginResult.Ok();
    }

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}

/// <summary>
/// Registers a valid capability before completing after the unit-test startup deadline.
/// It proves a timed-out runtime cannot contribute its pre-start draft when it later succeeds.
/// </summary>
public sealed class DelayedRegisteredSuccessPlugin : IGeneratedDaemonPluginAdapter
{
    public static PluginAdapterMetadata Metadata { get; } = new(
        "fixture.start-late-success",
        "1.0.0",
        "PluginEntry.dll",
        "MCServerLauncher.PluginFixtures.StartHanging.DelayedRegisteredSuccessPlugin",
        "[2.0.0, 3.0.0)",
        ["event.publish", "instance.query", "rpc.register"],
        "f5f6795447c4ad608ad104e4e6225168dbaf092f912a89e230f03a6159ae6a02");

    public Result<Unit, DaemonError> Configure(IPluginContext context)
    {
        var rpcResult = context.Rpc.Register(
            DelayedRegisteredSuccessProtocol.Rpc,
            static (_, _) => Task.FromResult(PluginResult.Ok<UnitResult>(new UnitResult())));
        if (rpcResult.IsErr(out var rpcError))
            return Result.Err<Unit, DaemonError>(rpcError!);

        var eventResult = context.Events.Register(DelayedRegisteredSuccessProtocol.Changed);
        return eventResult.IsErr(out var eventError)
            ? Result.Err<Unit, DaemonError>(eventError!)
            : PluginResult.Ok();
    }

    public async Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        // Intentionally ignores cancellation and must remain incomplete past the host deadline so
        // late success cannot race past Task.Delay supervision under CI load.
        await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
        return PluginResult.Ok();
    }

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}

public sealed class SynchronouslyBlockingStartPlugin : IGeneratedDaemonPluginAdapter
{
    public static PluginAdapterMetadata Metadata { get; } = new(
        "fixture.start-synchronously-blocks",
        "1.0.0",
        "PluginEntry.dll",
        "MCServerLauncher.PluginFixtures.StartHanging.SynchronouslyBlockingStartPlugin",
        "[2.0.0, 3.0.0)",
        ["event.publish", "instance.query", "rpc.register"],
        "e6d169328216692a66602fe65f31a877564ad0dcda32ef6d223701dfba0e465f");

    public Result<Unit, DaemonError> Configure(IPluginContext context) => PluginResult.Ok();

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        // Block the LongRunning start thread indefinitely so Task.Delay-based supervision still
        // wins under thread-pool pressure (a short Sleep can complete before a delayed timer).
        Thread.Sleep(Timeout.InfiniteTimeSpan);
        return Task.FromResult(PluginResult.Ok());
    }

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}

public static class DelayedRegisteredSuccessProtocol
{
    public static RpcDescriptor<EmptyRequest, UnitResult> Rpc { get; } = PluginProtocol.CreateRpc(
        "plugin.fixture.start-late-success.rpc.ping",
        "plugin.fixture.start-late-success.rpc",
        StartHangingJsonContext.Default.EmptyRequest,
        StartHangingJsonContext.Default.UnitResult,
        new RpcDocumentation(
            "fixture.start-late-success",
            "Late startup ping",
            "Must not be admitted after startup supervision times out.",
            "fixture.empty-request",
            "fixture.unit-result"));

    public static EventDescriptor<UnitResult, Unit> Changed { get; } = PluginProtocol.CreateEvent<UnitResult, Unit>(
        "plugin.fixture.start-late-success.event.changed",
        "plugin.fixture.start-late-success.event",
        StartHangingJsonContext.Default.UnitResult,
        null,
        new EventDocumentation(
            "fixture.start-late-success",
            "Late startup event",
            "Must not be admitted after startup supervision times out.",
            "fixture.unit-result",
            null));
}

public static class NeverCompletingStartProtocol
{
    public static RpcDescriptor<EmptyRequest, UnitResult> Rpc { get; } = PluginProtocol.CreateRpc(
        "plugin.fixture.start-never-completes.rpc.ping",
        "plugin.fixture.start-never-completes.rpc",
        StartHangingJsonContext.Default.EmptyRequest,
        StartHangingJsonContext.Default.UnitResult,
        new RpcDocumentation(
            "fixture.start-never-completes",
            "Blocked startup ping",
            "Must not be admitted while the plugin startup task remains incomplete.",
            "fixture.empty-request",
            "fixture.unit-result"));

    public static EventDescriptor<UnitResult, Unit> Changed { get; } = PluginProtocol.CreateEvent<UnitResult, Unit>(
        "plugin.fixture.start-never-completes.event.changed",
        "plugin.fixture.start-never-completes.event",
        StartHangingJsonContext.Default.UnitResult,
        null,
        new EventDocumentation(
            "fixture.start-never-completes",
            "Blocked startup event",
            "Must not be admitted while the plugin startup task remains incomplete.",
            "fixture.unit-result",
            null));
}

[JsonSerializable(typeof(EmptyRequest))]
[JsonSerializable(typeof(UnitResult))]
internal partial class StartHangingJsonContext : JsonSerializerContext;
