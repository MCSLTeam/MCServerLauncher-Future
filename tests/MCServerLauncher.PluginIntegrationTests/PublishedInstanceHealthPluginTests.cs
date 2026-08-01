using System.Diagnostics;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Daemon.API.Events;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.PluginFixtures.InstanceHealth;
using MCServerLauncher.PluginFixtures.StartHanging;
using RustyOptions;

namespace MCServerLauncher.PluginIntegrationTests;

public sealed class PublishedInstanceHealthPluginTests
{
    private static readonly TimeSpan SupervisedStartupTimeout = TimeSpan.FromSeconds(50);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(75);
    private static readonly RpcDescriptor<EmptyRequest, UnitResult> PackageReferenceConsumerPing =
        PluginProtocol.CreateRpc(
            "fixture.package-reference-consumer",
            "ping",
            BuiltInProtocolJsonContext.Default.EmptyRequest,
            BuiltInProtocolJsonContext.Default.UnitResult,
            new RpcDocumentation(
                "fixture.package-reference-consumer",
                "Package reference ping",
                "Verifies the published package consumer's generated Plugin.Sdk adapter is active.",
                "fixture.empty-request",
                "fixture.unit-result"));

    [Fact]
    [Trait("Category", "PublishedPlugin")]
    public async Task PublishedDaemon_LoadsHealthPluginAndServesDiscoverRpcAndEvent()
    {
        var publishedDaemon = Environment.GetEnvironmentVariable("MCSL_PUBLISHED_DAEMON");
        Assert.False(
            string.IsNullOrWhiteSpace(publishedDaemon),
            "MCSL_PUBLISHED_DAEMON must point to a published daemon executable or directory for published-plugin acceptance.");

        await using var fixture = await PublishedDaemonFixture.CreateAsync(publishedDaemon!);
        await fixture.StartAsync();

        await using var client = new global::MCServerLauncher.DaemonClient.DaemonClient(new DaemonClientOptions(
            fixture.EndpointUri,
            fixture.Token,
            RequestTimeout,
            TimeSpan.FromMilliseconds(100)));
        var connected = await client.ConnectAsync().WaitAsync(RequestTimeout);
        Assert.True(connected.IsOk(out _), connected.IsErr(out var connectionError) ? connectionError!.Message : null);

        var discover = await client.DiscoverAsync().WaitAsync(RequestTimeout);
        Assert.True(discover.IsOk(out var document), discover.IsErr(out var discoverError) ? discoverError!.Message : null);
        Assert.Contains(document!.Methods, method =>
            method.Name == "plugin.community.instance-health.rpc.get");
        Assert.Contains(document.Methods, method =>
            method.Name == PackageReferenceConsumerPing.Method.Value);

        var health = await client.InvokeAsync(
            InstanceHealthProtocol.Rpc,
            new InstanceHealthRequest { Scope = "all" }).WaitAsync(RequestTimeout);
        Assert.True(health.IsOk(out var healthResult), health.IsErr(out var healthError) ? healthError!.Message : null);
        Assert.Equal(0, healthResult!.TotalInstances);
        Assert.Equal(0, healthResult.RunningInstances);

        var invalidHealth = await client.InvokeAsync(
            InstanceHealthProtocol.Rpc,
            new InstanceHealthRequest { Scope = "unsupported" }).WaitAsync(RequestTimeout);
        Assert.True(invalidHealth.IsErr(out var invalidHealthError));
        Assert.Equal("plugin_scope_unsupported", invalidHealthError!.Code);

        var packageReferencePing = await client.InvokeAsync(
            PackageReferenceConsumerPing,
            new EmptyRequest()).WaitAsync(RequestTimeout);
        Assert.True(
            packageReferencePing.IsOk(out _),
            packageReferencePing.IsErr(out var packageReferenceError) ? packageReferenceError!.Message : null);

        var changed = new TaskCompletionSource<DaemonEvent<InstanceHealthChanged, Unit>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var subscriptionResult = await client.SubscribeAsync(
            InstanceHealthProtocol.Changed,
            DaemonEventFilter<Unit>.Wildcard,
            value =>
            {
                changed.TrySetResult(value);
                return Task.CompletedTask;
            }).WaitAsync(RequestTimeout);
        Assert.True(
            subscriptionResult.IsOk(out var subscription),
            subscriptionResult.IsErr(out var subscriptionError) ? subscriptionError!.Message : null);
        await using (subscription!)
        {
            var eventValue = await changed.Task.WaitAsync(EventTimeout);
            Assert.Equal(DaemonEventFieldKind.Missing, eventValue.Meta.Kind);
            Assert.Equal(0, eventValue.Data.Value.TotalInstances);
        }

        var logs = await fixture.StopAndReadLogsAsync();
        Assert.True(fixture.GracefulStopObserved, "Published daemon did not complete its console-driven graceful shutdown.");
        Assert.Contains("fixture.returned-error", logs, StringComparison.Ordinal);
        Assert.Contains("fixture_returned_error", logs, StringComparison.Ordinal);
        Assert.Contains("fixture.throwing", logs, StringComparison.Ordinal);
        Assert.Contains("configure_threw", logs, StringComparison.Ordinal);
        Assert.Contains("fixture.start-returned-error", logs, StringComparison.Ordinal);
        Assert.Contains("fixture_start_returned_error", logs, StringComparison.Ordinal);
        Assert.Contains("fixture.start-throwing", logs, StringComparison.Ordinal);
        Assert.Contains("start_threw", logs, StringComparison.Ordinal);
        Assert.Contains("fixture.instance_health.stop", logs, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "PublishedPlugin")]
    public async Task PublishedDaemon_TimesOutNonCooperativePluginAndStillServesTheListener()
    {
        var publishedDaemon = Environment.GetEnvironmentVariable("MCSL_PUBLISHED_DAEMON");
        Assert.False(
            string.IsNullOrWhiteSpace(publishedDaemon),
            "MCSL_PUBLISHED_DAEMON must point to a published daemon executable or directory for published-plugin acceptance.");

        await using var fixture = await PublishedDaemonFixture.CreateAsync(
            publishedDaemon!,
            includeNeverCompletingStartPlugin: true);
        var startedAt = Stopwatch.GetTimestamp();
        await fixture.StartAsync(SupervisedStartupTimeout);
        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        Assert.True(
            elapsed < SupervisedStartupTimeout,
            $"Published daemon did not become ready within supervised startup timeout: {elapsed}.");

        await using var client = new global::MCServerLauncher.DaemonClient.DaemonClient(new DaemonClientOptions(
            fixture.EndpointUri,
            fixture.Token,
            RequestTimeout,
            TimeSpan.FromMilliseconds(100)));
        var connected = await client.ConnectAsync().WaitAsync(RequestTimeout);
        Assert.True(connected.IsOk(out _), connected.IsErr(out var connectionError) ? connectionError!.Message : null);

        var discover = await client.DiscoverAsync().WaitAsync(RequestTimeout);
        Assert.True(discover.IsOk(out var document), discover.IsErr(out var discoverError) ? discoverError!.Message : null);
        Assert.Contains(document!.Methods, method => method.Name == "plugin.community.instance-health.rpc.get");
        Assert.DoesNotContain(document.Methods, method => method.Name == NeverCompletingStartProtocol.Rpc.Method.Value);
        Assert.DoesNotContain(document.Events, @event => @event.Name == NeverCompletingStartProtocol.Changed.Name.Value);

        var logs = await fixture.StopAndReadLogsAsync();
        Assert.True(fixture.GracefulStopObserved, "Published daemon did not complete its console-driven graceful shutdown.");
        Assert.Contains("fixture.start-never-completes", logs, StringComparison.Ordinal);
        Assert.Contains("start_timed_out", logs, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "PublishedPlugin")]
    public async Task PublishedDaemon_AbandonsNonCooperativeDisposeAndCompletesGracefulShutdown()
    {
        var publishedDaemon = Environment.GetEnvironmentVariable("MCSL_PUBLISHED_DAEMON");
        Assert.False(
            string.IsNullOrWhiteSpace(publishedDaemon),
            "MCSL_PUBLISHED_DAEMON must point to a published daemon executable or directory for published-plugin acceptance.");

        await using var fixture = await PublishedDaemonFixture.CreateAsync(
            publishedDaemon!,
            includeNeverCompletingDisposePlugin: true);
        await fixture.StartAsync();

        var startedAt = Stopwatch.GetTimestamp();
        var logs = await fixture.StopAndReadLogsAsync(TimeSpan.FromSeconds(45));
        var elapsed = Stopwatch.GetElapsedTime(startedAt);

        Assert.True(fixture.GracefulStopObserved, "Published daemon did not complete bounded graceful shutdown.");
        Assert.True(elapsed < TimeSpan.FromSeconds(45), $"Published daemon shutdown exceeded its test deadline: {elapsed}.");
        Assert.Contains("fixture.late-http-cleanup", logs, StringComparison.Ordinal);
        Assert.Contains("cleanup_abandoned", logs, StringComparison.Ordinal);
    }
}
