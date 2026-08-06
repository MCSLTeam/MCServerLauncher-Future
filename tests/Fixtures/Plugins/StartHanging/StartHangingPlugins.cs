using System.Threading;
using System.Text.Json.Serialization;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using RustyOptions;

[assembly: GeneratedDaemonPluginMetadata(
    "fixture.start-never-completes",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.StartHanging.NeverCompletingStartPlugin",
    "[1.0.0, 2.0.0)",
    "event.publish\ninstance.query\nrpc.register",
    "578b58100a99bc6b354541855cec3c05ffc83e275f24e2db39904234544f56af")]
[assembly: GeneratedDaemonPluginMetadata(
    "fixture.start-blocking-lifetime-cancellation",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.StartHanging.BlockingLifetimeCancellationPlugin",
    "[1.0.0, 2.0.0)",
    "event.publish\ninstance.query\nrpc.register",
    "f012eb2092b13853002385cc80b1126b2971035322744d3282920736c929d9aa")]
[assembly: GeneratedDaemonPluginMetadata(
    "fixture.start-blocking-start-cancellation",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.StartHanging.BlockingStartCancellationPlugin",
    "[1.0.0, 2.0.0)",
    "event.publish\ninstance.query\nrpc.register",
    "8a81929c28341c00ac1abafffa83c1df2e7792477d3625f20224f421b738b731")]
[assembly: GeneratedDaemonPluginMetadata(
    "fixture.start-ignores-cancellation",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.StartHanging.IgnoresCancellationStartPlugin",
    "[1.0.0, 2.0.0)",
    "event.publish\ninstance.query\nrpc.register",
    "f2a7b5eb5dcb07702d9f649391ee1f045953185149aeae75d5ab826c16e19aac")]
[assembly: GeneratedDaemonPluginMetadata(
    "fixture.start-late-success",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.StartHanging.DelayedRegisteredSuccessPlugin",
    "[1.0.0, 2.0.0)",
    "event.publish\ninstance.query\nrpc.register",
    "46c16f3c594d7b992f4918618cda0bc341a01b28fc0915757ee739536b56239f")]
[assembly: GeneratedDaemonPluginMetadata(
    "fixture.start-synchronously-blocks",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.StartHanging.SynchronouslyBlockingStartPlugin",
    "[1.0.0, 2.0.0)",
    "event.publish\ninstance.query\nrpc.register",
    "d3eab94c25aeb2fd6c41698582440aff869121936255a085c00e5ce5ec4aedfe")]

namespace MCServerLauncher.PluginFixtures.StartHanging;

public sealed class NeverCompletingStartPlugin : IGeneratedDaemonPluginAdapter
{
    public Result<Unit, DaemonError> Configure(IPluginContext context)
    {
        var descriptor = NeverCompletingStartProtocol.Rpc;
        var rpcResult = context.Rpc.Register(
            "ping",
            descriptor.RequestTypeInfo,
            descriptor.ResultTypeInfo,
            descriptor.Documentation!,
            static (_, _) => Task.FromResult(PluginResult.Ok<UnitResult>(new UnitResult())),
            descriptor.AllowNotification);
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
        var enteredPath = Environment.GetEnvironmentVariable("MCSL_PLUGIN_START_ENTERED_PATH");
        if (!string.IsNullOrWhiteSpace(enteredPath))
            File.WriteAllText(enteredPath, string.Empty);
        return new TaskCompletionSource<Result<Unit, DaemonError>>(
            TaskCreationOptions.RunContinuationsAsynchronously).Task;
    }

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken)
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable("MCSL_PLUGIN_HANG_STOP"),
                "1",
                StringComparison.Ordinal))
        {
            _ = cancellationToken;
            return new TaskCompletionSource<Result<Unit, DaemonError>>(
                TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }

        return Task.FromResult(PluginResult.Ok());
    }
}

public sealed class BlockingLifetimeCancellationPlugin : IGeneratedDaemonPluginAdapter
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

public sealed class BlockingStartCancellationPlugin : IGeneratedDaemonPluginAdapter
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

public sealed class IgnoresCancellationStartPlugin : IGeneratedDaemonPluginAdapter
{
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
    public Result<Unit, DaemonError> Configure(IPluginContext context)
    {
        var descriptor = DelayedRegisteredSuccessProtocol.Rpc;
        var rpcResult = context.Rpc.Register(
            "ping",
            descriptor.RequestTypeInfo,
            descriptor.ResultTypeInfo,
            descriptor.Documentation!,
            static (_, _) => Task.FromResult(PluginResult.Ok<UnitResult>(new UnitResult())),
            descriptor.AllowNotification);
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
        var releasePath = Environment.GetEnvironmentVariable("MCSL_PLUGIN_LATE_SUCCESS_RELEASE_PATH");
        if (string.IsNullOrWhiteSpace(releasePath))
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
            return PluginResult.Ok();
        }

        // A test that wants to exercise the late-success leg opens this gate once the host has
        // already timed the plugin out, so the success is genuinely late rather than racing. The
        // test waits on the host's own late-completion log, not on this method returning.
        while (!File.Exists(releasePath))
            await Task.Delay(TimeSpan.FromMilliseconds(10), CancellationToken.None);

        return PluginResult.Ok();
    }

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}

public sealed class SynchronouslyBlockingStartPlugin : IGeneratedDaemonPluginAdapter
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
        "fixture.start-late-success",
        "ping",
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
        "fixture.start-never-completes",
        "ping",
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
