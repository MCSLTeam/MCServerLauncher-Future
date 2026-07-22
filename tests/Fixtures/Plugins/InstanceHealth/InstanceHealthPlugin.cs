using System.Text.Json.Serialization;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Events;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using Microsoft.Extensions.Logging;
using RustyOptions;

namespace MCServerLauncher.PluginFixtures.InstanceHealth;

public sealed class InstanceHealthPlugin : IDaemonPlugin
{
    private IPluginContext? _context;
    private IPluginEventPublisher<InstanceHealthChanged, Unit>? _publisher;
    private Task? _backgroundTask;

    public Result<Unit, DaemonError> Configure(IPluginContext context)
    {
        _context = context;
        var rpcResult = context.Rpc.Register(InstanceHealthProtocol.Rpc, (request, _) =>
            Task.FromResult(
                StringComparer.OrdinalIgnoreCase.Equals(request.Scope, "all")
                    ? PluginResult.Ok(CreateResult(context))
                    : PluginResult.Fail<InstanceHealthResult>(context.Errors.Create(
                        "plugin_scope_unsupported",
                        $"Instance health scope '{request.Scope}' is not supported."))));
        if (rpcResult.IsErr(out var rpcError))
            return Result.Err<Unit, DaemonError>(rpcError!);

        var eventResult = context.Events.Register(InstanceHealthProtocol.Changed);
        if (eventResult.IsErr(out var eventError))
            return Result.Err<Unit, DaemonError>(eventError!);

        _publisher = eventResult.Unwrap();
        return PluginResult.Ok();
    }

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _backgroundTask = PublishChangesAsync();
        return Task.FromResult(PluginResult.Ok());
    }

    public async Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _context?.Logger.LogInformation("fixture.instance_health.stop");
        var backgroundTask = Interlocked.Exchange(ref _backgroundTask, null);
        if (backgroundTask is not null)
            await backgroundTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        return PluginResult.Ok();
    }

    private async Task PublishChangesAsync()
    {
        var context = _context!;
        var publisher = _publisher!;
        try
        {
            await context.Activation.WaitAsync(context.LifetimeToken).ConfigureAwait(false);
            await PublishCurrentAsync(context, publisher).ConfigureAwait(false);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            while (await timer.WaitForNextTickAsync(context.LifetimeToken).ConfigureAwait(false))
                await PublishCurrentAsync(context, publisher).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.LifetimeToken.IsCancellationRequested)
        {
        }
    }

    private static async Task PublishCurrentAsync(
        IPluginContext context,
        IPluginEventPublisher<InstanceHealthChanged, Unit> publisher)
    {
        var health = CreateResult(context);
        _ = await publisher.PublishAsync(
            DaemonEventField<Unit>.Missing,
            DaemonEventField<InstanceHealthChanged>.FromValue(
                new InstanceHealthChanged(health.TotalInstances, health.RunningInstances)),
            context.LifetimeToken).ConfigureAwait(false);
    }

    private static InstanceHealthResult CreateResult(IPluginContext context)
    {
        var snapshots = context.InstanceCatalog.Current.Value.Instances.Values;
        return new InstanceHealthResult(
            snapshots.Count(),
            snapshots.Count(static snapshot => snapshot.Status == MCServerLauncher.Common.ProtoType.Instance.InstanceStatus.Running));
    }
}

public sealed record InstanceHealthRequest
{
    public string Scope { get; init; } = "all";
}

public sealed record InstanceHealthResult(int TotalInstances, int RunningInstances);

public sealed record InstanceHealthChanged(int TotalInstances, int RunningInstances);

public static class InstanceHealthProtocol
{
    public static RpcDescriptor<InstanceHealthRequest, InstanceHealthResult> Rpc { get; } =
        PluginProtocol.CreateRpc(
            "plugin.community.instance-health.rpc.get",
            "plugin.community.instance-health.rpc",
            InstanceHealthJsonContext.Default.InstanceHealthRequest,
            InstanceHealthJsonContext.Default.InstanceHealthResult,
            new RpcDocumentation(
                "plugin.community.instance-health",
                "Get instance health",
                "Returns a snapshot-derived instance health summary.",
                "plugin.community.instance-health.request",
                "plugin.community.instance-health.result"));

    public static EventDescriptor<InstanceHealthChanged, Unit> Changed { get; } =
        PluginProtocol.CreateEvent<InstanceHealthChanged, Unit>(
            "plugin.community.instance-health.event.changed",
            "plugin.community.instance-health.event",
            InstanceHealthJsonContext.Default.InstanceHealthChanged,
            null,
            new EventDocumentation(
                "plugin.community.instance-health",
                "Instance health changed",
                "Publishes a periodic snapshot-derived instance health summary.",
                "plugin.community.instance-health.changed",
                null));
}

[JsonSerializable(typeof(InstanceHealthRequest))]
[JsonSerializable(typeof(InstanceHealthResult))]
[JsonSerializable(typeof(InstanceHealthChanged))]
internal partial class InstanceHealthJsonContext : JsonSerializerContext;
