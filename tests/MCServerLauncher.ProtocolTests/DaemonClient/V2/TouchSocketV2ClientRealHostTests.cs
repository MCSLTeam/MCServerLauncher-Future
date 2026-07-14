using System.Net;
using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon;
using MCServerLauncher.Daemon.Bootstrap;
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
    public async Task ProductionSession_UploadsBinaryChunkThroughPrivateAcknowledgementRouting()
    {
        await using var fixture = new ProductionRealHostFixture();
        await fixture.StartAsync();
        var factory = new TouchSocketV2ClientConnectionSessionFactory(
            new Uri($"ws://127.0.0.1:{fixture.Port}/api/v2"),
            AppConfig.Get().MainToken,
            requestTimeout: Timeout);
        await using var owner = new V2ClientConnectionOwner(
            factory,
            TimeProvider.System,
            TimeSpan.FromMilliseconds(20));
        using var timeout = new CancellationTokenSource(Timeout);
        var content = "private-upload-ack"u8.ToArray();
        var relativePath = Path.Combine("protocol-tests", $"v2-upload-{Guid.NewGuid():N}.bin");
        var absolutePath = Path.Combine(Daemon.Storage.FileManager.Root, relativePath);

        try
        {
            var connected = await owner.ConnectAsync(timeout.Token);
            Assert.True(connected.IsOk(out _));
            Assert.True(owner.TryGetReadyCore(out var core));
            var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            var opened = await core.InvokeAsync(
                V2ClientProtocol.OpenUpload,
                new UploadOpenRequest(relativePath, content.Length, hash),
                timeout.Token);
            Assert.True(opened.IsOk(out var session));

            var uploaded = await core.SendUploadChunkAsync(
                new UploadChunkRequest(session!.SessionId, 0, ImmutableArray.Create(content)),
                session.MaxChunkSize,
                timeout.Token);
            Assert.True(uploaded.IsOk(out _));

            var closed = await core.InvokeUnitAsync(
                V2ClientProtocol.CloseUpload,
                new FileSessionReference(session.SessionId),
                timeout.Token);
            Assert.True(closed.IsOk(out _));
            Assert.Equal(content, await File.ReadAllBytesAsync(absolutePath, timeout.Token));
        }
        finally
        {
            await owner.CloseAsync().WaitAsync(Timeout);
            if (File.Exists(absolutePath))
                File.Delete(absolutePath);
        }
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    public async Task ProductionSession_DownloadsMultipleChunksWithEmptyFinalAndSha256()
    {
        await using var fixture = new ProductionRealHostFixture();
        await fixture.StartAsync();
        var factory = new TouchSocketV2ClientConnectionSessionFactory(
            new Uri($"ws://127.0.0.1:{fixture.Port}/api/v2"),
            AppConfig.Get().MainToken,
            requestTimeout: Timeout);
        var owner = new V2ClientConnectionOwner(
            factory,
            TimeProvider.System,
            TimeSpan.FromMilliseconds(20));
        var absolutePath = string.Empty;
        V2ClientConnectionCore? core = null;
        DownloadSession? session = null;
        var downloadClosed = false;
        var downloadRemoved = false;
        var bodySucceeded = false;

        try
        {
            using var timeout = new CancellationTokenSource(Timeout);
            var content = new byte[checked((int)(2 * BinaryFrameCodec.DefaultMaximumChunkSize))];
            for (var index = 0; index < content.Length; index++)
                content[index] = unchecked((byte)((index * 31 + 17) % 251));

            var relativePath = Path.Combine("protocol-tests", $"v2-download-{Guid.NewGuid():N}.bin");
            absolutePath = Path.Combine(Daemon.Storage.FileManager.Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            await File.WriteAllBytesAsync(absolutePath, content, timeout.Token);

            var connected = await owner.ConnectAsync(timeout.Token);
            Assert.True(connected.IsOk(out _));
            Assert.True(owner.TryGetReadyCore(out var readyCore));
            core = readyCore;

            var opened = await readyCore.InvokeAsync(
                V2ClientProtocol.OpenDownload,
                new DownloadOpenRequest(relativePath),
                timeout.Token);
            Assert.True(opened.IsOk(out session));
            var downloadSession = session!;
            var registered = readyCore.TryRegisterDownloadSession(downloadSession, out var registrationError);
            Assert.True(
                registered,
                registrationError is null
                    ? "The download session could not be registered in the client rendezvous."
                    : $"{registrationError.Code}: {registrationError.Message}");

            var first = await readyCore.ReadDownloadChunkAsync(
                new DownloadChunkRequest(downloadSession.SessionId, 0, downloadSession.MaxChunkSize),
                timeout.Token);
            Assert.True(first.IsOk(out var firstChunk));
            Assert.False(firstChunk!.IsFinal);
            Assert.Equal(0, firstChunk.Offset);
            Assert.Equal(downloadSession.MaxChunkSize, firstChunk.Data.Length);

            var second = await readyCore.ReadDownloadChunkAsync(
                new DownloadChunkRequest(downloadSession.SessionId, downloadSession.MaxChunkSize, downloadSession.MaxChunkSize),
                timeout.Token);
            Assert.True(second.IsOk(out var secondChunk));
            Assert.True(secondChunk!.IsFinal);
            Assert.Equal(downloadSession.MaxChunkSize, secondChunk.Offset);
            Assert.Equal(downloadSession.MaxChunkSize, secondChunk.Data.Length);

            var rereadLength = downloadSession.MaxChunkSize / 2;
            Assert.True(rereadLength > 1);
            var reread = await readyCore.ReadDownloadChunkAsync(
                new DownloadChunkRequest(downloadSession.SessionId, downloadSession.MaxChunkSize, rereadLength),
                timeout.Token);
            Assert.True(reread.IsOk(out var rereadChunk));
            Assert.False(rereadChunk!.IsFinal);
            Assert.Equal(secondChunk.Offset, rereadChunk.Offset);
            Assert.Equal(rereadLength, rereadChunk.Data.Length);
            Assert.Equal(
                content.AsSpan(downloadSession.MaxChunkSize, rereadLength).ToArray(),
                rereadChunk.Data.ToArray());

            var emptyFinal = await readyCore.ReadDownloadChunkAsync(
                new DownloadChunkRequest(downloadSession.SessionId, downloadSession.Length, downloadSession.MaxChunkSize),
                timeout.Token);
            Assert.True(emptyFinal.IsOk(out var emptyFinalChunk));
            Assert.True(emptyFinalChunk!.IsFinal);
            Assert.Equal(downloadSession.Length, emptyFinalChunk.Offset);
            Assert.Empty(emptyFinalChunk.Data);

            var assembled = new byte[firstChunk.Data.Length + secondChunk.Data.Length];
            firstChunk.Data.AsSpan().CopyTo(assembled);
            secondChunk.Data.AsSpan().CopyTo(assembled.AsSpan(firstChunk.Data.Length));
            Assert.Equal(content, assembled);
            Assert.Equal(
                downloadSession.Sha256,
                Convert.ToHexString(SHA256.HashData(assembled)).ToLowerInvariant());

            var closed = await readyCore.InvokeUnitAsync(
                V2ClientProtocol.CloseDownload,
                new FileSessionReference(downloadSession.SessionId),
                timeout.Token);
            Assert.True(closed.IsOk(out _));
            downloadClosed = true;
            var removed = readyCore.TryRemoveDownloadSession(downloadSession.SessionId, out var removalError);
            Assert.True(
                removed,
                removalError is null
                    ? "The download session could not be removed from the client rendezvous."
                    : $"{removalError.Code}: {removalError.Message}");
            downloadRemoved = true;
            bodySucceeded = true;
        }
        finally
        {
            try
            {
                if (core is not null && session is not null && !downloadClosed)
                {
                    try
                    {
                        await core.InvokeUnitAsync(
                            V2ClientProtocol.CloseDownload,
                            new FileSessionReference(session.SessionId),
                            CancellationToken.None);
                    }
                    catch
                    {
                    }
                }

                if (core is not null && session is not null && !downloadRemoved)
                {
                    try
                    {
                        core.TryRemoveDownloadSession(session.SessionId, out _);
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                Exception? ownerCloseFailure = null;
                Exception? deleteFailure = null;
                try
                {
                    await owner.CloseAsync().WaitAsync(Timeout);
                }
                catch (Exception exception)
                {
                    ownerCloseFailure = exception;
                }
                finally
                {
                    if (bodySucceeded)
                    {
                        try
                        {
                            File.Delete(absolutePath);
                        }
                        catch (Exception exception)
                        {
                            deleteFailure = exception;
                        }
                    }
                    else
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(absolutePath) && File.Exists(absolutePath))
                                File.Delete(absolutePath);
                        }
                        catch
                        {
                        }
                    }
                }

                if (bodySucceeded)
                {
                    if (ownerCloseFailure is not null)
                        ExceptionDispatchInfo.Capture(ownerCloseFailure).Throw();
                    if (deleteFailure is not null)
                        ExceptionDispatchInfo.Capture(deleteFailure).Throw();
                }
            }
        }
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
