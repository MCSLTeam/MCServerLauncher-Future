using System.Text.Json;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Events;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.Plugin.Sdk;
using Microsoft.Extensions.DependencyInjection;
using RustyOptions;

namespace MCServerLauncher.PluginFixtures.ExtensionPush;

[DaemonPluginModule]
public partial class ExtensionPushPlugin
{
    public const string PluginId = "community.extension-push";

    private IPluginEventPublisher<ExtensionProtocolDispatchResult, EmptyRequest>? _publisher;
    private CancellationTokenSource? _stopSignal;
    private Task? _backgroundTask;

    public void ConfigureServices(IServiceCollection services, ExtensionPushPluginFeatures features)
    {
        _ = services;
        var eventResult = features.Events.Register(PluginExtensionProtocol.CreateExtensionEvent(PluginId));
        _publisher = eventResult.Unwrap();
    }

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _stopSignal = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _backgroundTask = PublishWhenTriggeredAsync(_stopSignal.Token);
        return Task.FromResult(PluginResult.Ok());
    }

    public async Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopSignal = Interlocked.Exchange(ref _stopSignal, null);
        if (stopSignal is not null)
        {
            await stopSignal.CancelAsync().ConfigureAwait(false);
            stopSignal.Dispose();
        }

        var backgroundTask = Interlocked.Exchange(ref _backgroundTask, null);
        if (backgroundTask is not null)
            await backgroundTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        return PluginResult.Ok();
    }

    private async Task PublishWhenTriggeredAsync(CancellationToken cancellationToken)
    {
        var triggerPath = Environment.GetEnvironmentVariable("MCSL_EXTENSION_PUSH_TRIGGER_PATH");
        if (string.IsNullOrWhiteSpace(triggerPath))
            return;

        try
        {
            while (!File.Exists(triggerPath))
                await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken).ConfigureAwait(false);

            using var patch = JsonDocument.Parse(
                """
                {
                  "status": "ready",
                  "source": "fixture"
                }
                """);
            var envelope = PluginExtensionProtocol.StatePatch(PluginId, 1, patch.RootElement);
            var result = await _publisher!.PublishAsync(
                DaemonEventField<EmptyRequest>.Missing,
                DaemonEventField<ExtensionProtocolDispatchResult>.FromValue(envelope),
                cancellationToken).ConfigureAwait(false);
            if (result.IsErr(out var error))
                throw new InvalidOperationException($"Extension push failed: {error!.Code} {error.Message}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
