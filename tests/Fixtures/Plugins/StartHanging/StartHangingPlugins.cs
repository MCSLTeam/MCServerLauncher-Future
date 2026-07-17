using System.Threading;
using System.Text.Json.Serialization;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using RustyOptions;

namespace MCServerLauncher.PluginFixtures.StartHanging;

public sealed class NeverCompletingStartPlugin : IDaemonPlugin
{
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

public sealed class BlockingLifetimeCancellationPlugin : IDaemonPlugin
{
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

public sealed class BlockingStartCancellationPlugin : IDaemonPlugin
{
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

public sealed class IgnoresCancellationStartPlugin : IDaemonPlugin
{
    public Result<Unit, DaemonError> Configure(IPluginContext context) => PluginResult.Ok();

    public async Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);
        return PluginResult.Ok();
    }

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}

/// <summary>
/// Registers a valid capability before completing after the unit-test startup deadline.
/// It proves a timed-out runtime cannot contribute its pre-start draft when it later succeeds.
/// </summary>
public sealed class DelayedRegisteredSuccessPlugin : IDaemonPlugin
{
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
        await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);
        return PluginResult.Ok();
    }

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}

public sealed class SynchronouslyBlockingStartPlugin : IDaemonPlugin
{
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
