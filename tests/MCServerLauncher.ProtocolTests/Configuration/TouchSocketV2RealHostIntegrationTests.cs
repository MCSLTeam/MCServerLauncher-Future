using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MCServerLauncher.Daemon;
using MCServerLauncher.Daemon.Bootstrap;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Remote.Rpc.Events;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Transport;
using MCServerLauncher.ProtocolTests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Http.WebSockets;
using TouchSocket.Sockets;

namespace MCServerLauncher.ProtocolTests.Configuration;

[Collection(DaemonInstanceStorageIsolationCollection.Name)]
public sealed class TouchSocketV2RealHostIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    [Trait("Category", "DaemonInbound")]
    public async Task RealHost_AuthenticatesDispatchesAndCleansV2Connections()
    {
        var fixture = new RealHostFixture();
        try
        {
            await fixture.StartAsync();
            var host = fixture.Host;
            var publishedCatalog = fixture.PublishedCatalog;
            var port = fixture.Port;
            var token = AppConfig.Get().MainToken;
            var capture = new CapturePlugin();
            var client = await fixture.ConnectAsync(token, capture);
            await AssertEndpointIsNotUpgradedAsync(port, "/api/invalid", token);
            await AssertHttpMetadataReportsV2Async(port);

            var request = Encoding.UTF8.GetBytes(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"rpc.discover\"}");
            await client.SendAsync(request, WSDataType.Text);
            var response = await capture.Text.Task.WaitAsync(Timeout);
            using var document = JsonDocument.Parse(response);
            Assert.Equal("2.0", document.RootElement.GetProperty("jsonrpc").GetString());
            Assert.Equal(1, document.RootElement.GetProperty("id").GetInt32());
            Assert.NotEmpty(document.RootElement.GetProperty("result").GetProperty("methods").EnumerateArray());
            Assert.Equal(publishedCatalog!.Rpcs.Count,
                document.RootElement.GetProperty("result").GetProperty("methods").GetArrayLength());

            // TouchSocket can reset a rejected upgrade before HttpClient receives the status response.
            await AssertHandshakeRejectedAsync(port, token: null, allowConnectionReset: true);
            await AssertHandshakeRejectedAsync(port, "not-a-valid-token", allowConnectionReset: true);
            await AssertHandshakeRejectedAsync(port, JwtUtils.GenerateToken("*", -1), allowConnectionReset: true);
            WsVerifyHandler.RejectWithNoReason = true;
            try
            {
                await AssertHandshakeRejectedAsync(
                    port,
                    token,
                    System.Net.HttpStatusCode.Forbidden,
                    allowConnectionReset: true);
            }
            finally
            {
                WsVerifyHandler.RejectWithNoReason = false;
            }

            await client.SendAsync(new byte[] { 1 }, WSDataType.Binary);
            await capture.Closed.Task.WaitAsync(Timeout);
            Assert.Equal(0, host.Resolver.GetRequiredService<TouchSocketV2TransportPlugin>().ConnectionCount);
            Assert.Equal(0, host.Resolver.GetRequiredService<V2EventConnectionRegistry>().RawEntryCount);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    private static async Task AssertHandshakeRejectedAsync(
        int port,
        string? token,
        System.Net.HttpStatusCode expectedStatus = System.Net.HttpStatusCode.Unauthorized,
        bool allowConnectionReset = false)
    {
        var maximumAttempts = allowConnectionReset ? 1 : 3;
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                var query = token is null ? string.Empty : $"?token={Uri.EscapeDataString(token)}";
                using var request = new HttpRequestMessage(
                    System.Net.Http.HttpMethod.Get,
                    $"http://127.0.0.1:{port}/api/v2{query}");
                request.Headers.TryAddWithoutValidation("Connection", "Upgrade");
                request.Headers.TryAddWithoutValidation("Upgrade", "websocket");
                request.Headers.TryAddWithoutValidation("Sec-WebSocket-Version", "13");
                request.Headers.TryAddWithoutValidation(
                    "Sec-WebSocket-Key",
                    Convert.ToBase64String(Guid.NewGuid().ToByteArray()));
                using var timeout = new CancellationTokenSource(Timeout);
                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);
                Assert.Equal(expectedStatus, response.StatusCode);
                return;
            }
            catch (HttpRequestException exception) when (HasConnectionReset(exception))
            {
                if (allowConnectionReset)
                    return;
                if (attempt == maximumAttempts)
                    throw;
            }
        }
    }

    private static bool HasConnectionReset(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException { SocketErrorCode: SocketError.ConnectionReset })
                return true;
        }

        return false;
    }

    private static async Task AssertEndpointIsNotUpgradedAsync(int port, string path, string token)
    {
        using var client = new System.Net.Http.HttpClient();
        using var request = new HttpRequestMessage(
            System.Net.Http.HttpMethod.Get,
            $"http://127.0.0.1:{port}{path}?token={Uri.EscapeDataString(token)}");
        request.Headers.TryAddWithoutValidation("Connection", "Upgrade");
        request.Headers.TryAddWithoutValidation("Upgrade", "websocket");
        request.Headers.TryAddWithoutValidation("Sec-WebSocket-Version", "13");
        request.Headers.TryAddWithoutValidation(
            "Sec-WebSocket-Key",
            Convert.ToBase64String(Guid.NewGuid().ToByteArray()));
        using var timeout = new CancellationTokenSource(Timeout);
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);

        Assert.NotEqual(System.Net.HttpStatusCode.SwitchingProtocols, response.StatusCode);
    }

    private static async Task AssertHttpMetadataReportsV2Async(int port)
    {
        using var client = new System.Net.Http.HttpClient();
        foreach (var path in new[] { "/", "/info" })
        {
            using var timeout = new CancellationTokenSource(Timeout);
            using var response = await client.GetAsync($"http://127.0.0.1:{port}{path}", timeout.Token);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            Assert.Equal("v2", document.RootElement.GetProperty("api_version").GetString());
        }
    }

    private sealed class RealHostFixture : IAsyncDisposable
    {
        private DaemonTouchSocketTransportConfiguration? _transport;
        private IServiceProvider? _rootProvider;
        private DaemonLifecycleAttachment? _lifecycle;
        private WebSocketClient? _client;
        private int _disposed;

        internal HttpService Host { get; } = new();
        internal FrozenProtocolCatalog PublishedCatalog { get; private set; } = default!;
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
                    new IPHost(IPAddress.Loopback, 0));
                await Host.SetupAsync(_transport.Config);
                _rootProvider = _transport.Container.ServiceProvider;

                var catalogAccessor = Host.Resolver.GetRequiredService<IFrozenProtocolCatalogAccessor>();
                Assert.False(catalogAccessor.TryGet(out _));
                _lifecycle = DaemonServiceComposition.AttachDaemonLifecycle(Host);
                Assert.True(catalogAccessor.TryGet(out var publishedCatalog));
                PublishedCatalog = publishedCatalog!;

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
                    throw new AggregateException("Real V2 host setup and cleanup both failed.",
                        startException, cleanupException);
                }
                throw;
            }
        }

        internal async Task<WebSocketClient> ConnectAsync(string token, CapturePlugin capture)
        {
            var client = new WebSocketClient();
            _client = client;
            try
            {
                var config = new TouchSocketConfig()
                    .SetRemoteIPHost(new IPHost(
                        $"ws://127.0.0.1:{Port}/api/v2?token={Uri.EscapeDataString(token)}"))
                    .ConfigurePlugins(plugins => plugins.Add(capture));
                await client.SetupAsync(config);
                using var timeout = new CancellationTokenSource(Timeout);
                await client.ConnectAsync(timeout.Token);
                return client;
            }
            catch
            {
                client.SafeDispose();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            List<Exception> failures = [];
            try
            {
                _client?.SafeDispose();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

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
                throw new AggregateException("Real V2 host cleanup failed.", failures);
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

    private sealed class CapturePlugin : PluginBase,
        IWebSocketConnectedPlugin, IWebSocketReceivedPlugin, IWebSocketClosedPlugin
    {
        internal TaskCompletionSource Connected { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<byte[]> Text { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Closed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task OnWebSocketConnected(IWebSocket webSocket, HttpContextEventArgs e)
        {
            Connected.TrySetResult();
            await e.InvokeNext().ConfigureAwait(false);
        }

        public async Task OnWebSocketReceived(IWebSocket webSocket, WSDataFrameEventArgs e)
        {
            if (e.DataFrame.Opcode == WSDataType.Text)
                Text.TrySetResult(e.DataFrame.PayloadData.ToArray());
            await e.InvokeNext().ConfigureAwait(false);
        }

        public async Task OnWebSocketClosed(IWebSocket webSocket, ClosedEventArgs e)
        {
            Closed.TrySetResult();
            await e.InvokeNext().ConfigureAwait(false);
        }
    }
}
