using System.Net;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon;
using MCServerLauncher.Daemon.Bootstrap;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Events;
using MCServerLauncher.Daemon.Remote.Rpc.Transport;
using MCServerLauncher.DaemonClient.Connection.V2;
using MCServerLauncher.DaemonClient.Protocol;
using MCServerLauncher.ProtocolTests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Sockets;

namespace MCServerLauncher.ProtocolTests.DaemonClient.V2;

[Collection(DaemonInstanceStorageIsolationCollection.Name)]
public sealed class TouchSocketV2ClientRealHostTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    [Trait("Category", "DaemonInbound")]
    public async Task ProductionSession_AuthenticatesSynchronizesInvokesAndCloses()
    {
        await using var fixture = new ProductionRealHostFixture();
        await fixture.StartAsync();
        var endpoint = new Uri($"ws://127.0.0.1:{fixture.Port}/api/v2");
        var factory = new TouchSocketV2ClientConnectionSessionFactory(
            endpoint,
            AppConfig.Get().MainToken,
            requestTimeout: Timeout);
        await using var owner = new V2ClientConnectionOwner(
            factory,
            TimeProvider.System,
            TimeSpan.FromMilliseconds(20));
        using var timeout = new CancellationTokenSource(Timeout);

        var connected = await owner.ConnectAsync(timeout.Token);

        Assert.True(connected.IsOk(out _));
        Assert.True(owner.IsReady);
        Assert.True(owner.TryGetReadyCore(out var core));
        var ping = await core.InvokeAsync(
            V2ClientProtocol.PingDaemon,
            new EmptyRequest(),
            timeout.Token);
        Assert.True(ping.IsOk(out var result));
        Assert.True(result!.Time > 0);

        await owner.CloseAsync().WaitAsync(Timeout);
        Assert.Equal(V2ClientConnectionOwnerState.Closed, owner.State);
        await AssertEventuallyAsync(() =>
            fixture.Host.Resolver.GetRequiredService<TouchSocketV2TransportPlugin>().ConnectionCount == 0 &&
            fixture.Host.Resolver.GetRequiredService<V2EventConnectionRegistry>().RawEntryCount == 0);
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    public async Task ProductionSession_HandshakeFailureReturnsTypedErrorAndCleansUp()
    {
        await using var fixture = new ProductionRealHostFixture();
        await fixture.StartAsync();
        var factory = new TouchSocketV2ClientConnectionSessionFactory(
            new Uri($"ws://127.0.0.1:{fixture.Port}/api/v2"),
            "not-a-valid-token",
            requestTimeout: Timeout);
        await using var owner = new V2ClientConnectionOwner(
            factory,
            TimeProvider.System,
            TimeSpan.FromMilliseconds(20));
        using var timeout = new CancellationTokenSource(Timeout);

        var connected = await owner.ConnectAsync(timeout.Token);

        Assert.True(connected.IsErr(out var error));
        Assert.Equal("transport.connect_failed", error!.Code);
        Assert.Equal(Daemon.API.Errors.DaemonErrorKind.Transport, error.Kind);
        Assert.Equal(V2ClientConnectionOwnerState.Created, owner.State);
        await AssertEventuallyAsync(() =>
            fixture.Host.Resolver.GetRequiredService<TouchSocketV2TransportPlugin>().ConnectionCount == 0 &&
            fixture.Host.Resolver.GetRequiredService<V2EventConnectionRegistry>().RawEntryCount == 0);
    }

    private static async Task AssertEventuallyAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The V2 real host did not finish connection cleanup.");
            await Task.Delay(20);
        }
    }

    private sealed class ProductionRealHostFixture : IAsyncDisposable
    {
        private DaemonTouchSocketTransportConfiguration? _transport;
        private IServiceProvider? _rootProvider;
        private DaemonLifecycleAttachment? _lifecycle;
        private int _disposed;

        internal HttpService Host { get; } = new();
        internal int Port { get; private set; }

        internal async Task StartAsync()
        {
            try
            {
                Directory.CreateDirectory(Daemon.Storage.FileManager.InstancesRoot);
                var services = new ServiceCollection();
                services.AddLogging();
                _transport = DaemonTouchSocketTransportProfile.CreateConfig(
                    services,
                    Host,
                    ActionHandlerRegistryRuntime.CreateSelected(useGeneratedActionRegistry: true),
                    new IPHost(IPAddress.Loopback, 0));
                await Host.SetupAsync(_transport.Config);
                _rootProvider = _transport.Container.ServiceProvider;

                var catalogAccessor = Host.Resolver.GetRequiredService<IFrozenProtocolCatalogAccessor>();
                Assert.False(catalogAccessor.TryGet(out _));
                _lifecycle = DaemonServiceComposition.AttachDaemonLifecycle(Host);
                Assert.True(catalogAccessor.TryGet(out _));

                await Host.StartAsync();
                var endpoint = Assert.IsType<IPEndPoint>(Host.Monitors
                    .Select(monitor => monitor.Socket.LocalEndPoint)
                    .Single(address => address is IPEndPoint));
                Port = endpoint.Port;
            }
            catch (Exception startException)
            {
                if (_rootProvider is null && _transport is not null)
                {
                    try
                    {
                        _rootProvider = _transport.Container.ServiceProvider;
                    }
                    catch
                    {
                    }
                }

                try
                {
                    await DisposeAsync();
                }
                catch (Exception cleanupException)
                {
                    throw new AggregateException(
                        "Production V2 client host setup and cleanup both failed.",
                        startException,
                        cleanupException);
                }
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            List<Exception> failures = [];
            if (_lifecycle is not null)
                await CaptureAsync(() => _lifecycle.DisposeAsync().AsTask(), failures);
            await CaptureAsync(() => Host.StopAsync(), failures);
            try
            {
                Host.SafeDispose();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            if (_rootProvider is IAsyncDisposable asyncDisposable)
                await CaptureAsync(() => asyncDisposable.DisposeAsync().AsTask(), failures);
            else if (_rootProvider is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            if (failures.Count != 0)
                throw new AggregateException("Production V2 client real host cleanup failed.", failures);
        }

        private static async Task CaptureAsync(Func<Task> cleanup, List<Exception> failures)
        {
            try
            {
                await cleanup();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
    }
}
