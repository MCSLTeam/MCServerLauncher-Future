using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.DaemonClient.Connection.V2;
using MCServerLauncher.DaemonClient.State;

namespace MCServerLauncher.ProtocolTests.DaemonClient.V2;

public sealed class V2ClientConnectionCoordinatorTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly Guid FirstId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly List<V2ClientConnectionCoordinator> _ownedCoordinators = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        List<Exception>? failures = null;
        foreach (var coordinator in _ownedCoordinators)
        {
            try
            {
                await coordinator.CloseAsync().WaitAsync(Timeout);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
            throw new AggregateException("One or more coordinator test cleanups failed.", failures);
    }

    [Fact]
    public async Task StartSubscribesBeforeReadingAndReplaysDeltaBetweenAckAndFullSnapshot()
    {
        var transport = new ScriptedTransport();
        var mirror = new RemoteInstanceCatalogMirror();
        var coordinator = Coordinator(transport, mirror);

        var start = coordinator.StartAsync();
        var subscribe = await transport.NextAsync();
        Assert.Equal("mcsl.event.subscribe", subscribe.Method);
        Assert.Contains("mcsl.event.instance.catalog.changed", subscribe.Json, StringComparison.Ordinal);
        Assert.Equal(1, transport.SendCount);

        coordinator.Core.RouteText(Success(subscribe, "{}"));
        var snapshot = await transport.NextAsync();
        Assert.Equal("mcsl.instance.catalog.get", snapshot.Method);
        coordinator.Core.RouteText(CatalogEvent(1, "one"));
        Assert.False(coordinator.IsReady);
        Assert.Empty(mirror.Current.Value.Instances);

        coordinator.Core.RouteText(Success(snapshot, Catalog(0)));
        Assert.True((await start.WaitAsync(Timeout)).IsOk(out _));
        Assert.True(coordinator.IsReady);
        Assert.Equal(1, mirror.Current.Version);
        Assert.Equal("one", mirror.Current.Value.Instances[FirstId].Name);
        await coordinator.CloseAsync().WaitAsync(Timeout);
    }

    [Fact]
    public async Task SubscribeFailurePreservesOriginalErrorAndDoesNotReadSnapshot()
    {
        var transport = new ScriptedTransport();
        var coordinator = Coordinator(transport, new RemoteInstanceCatalogMirror());

        var start = coordinator.StartAsync();
        var subscribe = await transport.NextAsync();
        coordinator.Core.RouteText(Error(subscribe, -32001, "auth.denied", "permission", "denied"));

        var result = await start.WaitAsync(Timeout);
        Assert.True(result.IsErr(out var error));
        Assert.IsType<PermissionDaemonError>(error);
        Assert.Equal("auth.denied", error!.Code);
        Assert.Same(error, coordinator.TerminalError);
        Assert.Equal(1, transport.SendCount);
        await coordinator.CloseAsync().WaitAsync(Timeout);
    }

    [Fact]
    public async Task ReadyGapCoalescesDuplicateSignalIntoOneSerializedRefetch()
    {
        var transport = new ScriptedTransport();
        var mirror = new RemoteInstanceCatalogMirror();
        var coordinator = Coordinator(transport, mirror);
        await MakeReadyAsync(coordinator, transport, Catalog(1, Item("one")));

        var gap = CatalogEvent(3, "three");
        coordinator.Core.RouteText(gap);
        coordinator.Core.RouteText(gap);
        Assert.False(coordinator.IsReady);
        Assert.Equal(1, mirror.Current.Version);

        var refetch = await transport.NextAsync();
        Assert.Equal("mcsl.instance.catalog.get", refetch.Method);
        Assert.Equal(3, transport.SendCount);
        coordinator.Core.RouteText(Success(refetch, Catalog(2, Item("two"))));
        await WaitUntilReadyAsync(coordinator);

        Assert.Equal(3, mirror.Current.Version);
        Assert.Equal("three", mirror.Current.Value.Instances[FirstId].Name);
        Assert.Equal(3, transport.SendCount);
        await coordinator.CloseAsync().WaitAsync(Timeout);
    }

    [Fact]
    public async Task ConflictingDuplicateWithdrawsReadyAndUsesOneRefetchWithoutResubscribe()
    {
        var transport = new ScriptedTransport();
        var mirror = new RemoteInstanceCatalogMirror();
        var coordinator = Coordinator(transport, mirror);
        await MakeReadyAsync(coordinator, transport, Catalog(1, Item("one")));

        var conflict = CatalogEvent(1, "changed");
        coordinator.Core.RouteText(conflict);
        coordinator.Core.RouteText(conflict);
        var refetch = await transport.NextAsync();

        Assert.False(coordinator.IsReady);
        Assert.Equal("mcsl.instance.catalog.get", refetch.Method);
        Assert.Equal(3, transport.SendCount);
        coordinator.Core.RouteText(Success(refetch, Catalog(1, Item("changed"))));
        await WaitUntilReadyAsync(coordinator);
        Assert.Equal("changed", mirror.Current.Value.Instances[FirstId].Name);
        Assert.Equal(3, transport.SendCount);
        await coordinator.CloseAsync().WaitAsync(Timeout);
    }

    [Fact]
    public async Task BufferOverflowSupersedesPendingReadAndPublishesOnlyRefetchedGeneration()
    {
        var transport = new ScriptedTransport();
        var mirror = new RemoteInstanceCatalogMirror();
        var coordinator = Coordinator(transport, mirror);
        var start = coordinator.StartAsync();
        var subscribe = await transport.NextAsync();
        coordinator.Core.RouteText(Success(subscribe, "{}"));
        var superseded = await transport.NextAsync();

        for (var version = 1; version <= RemoteInstanceCatalogMirror.MaximumBufferedChanges + 1; version++)
            coordinator.Core.RouteText(CatalogEvent(version, $"item-{version}"));

        coordinator.Core.RouteText(Success(superseded, Catalog(0)));
        var refetch = await transport.NextAsync();
        Assert.False(coordinator.IsReady);
        Assert.Empty(mirror.Current.Value.Instances);
        coordinator.Core.RouteText(Success(
            refetch,
            Catalog(RemoteInstanceCatalogMirror.MaximumBufferedChanges + 1, Item("authoritative"))));

        Assert.True((await start.WaitAsync(Timeout)).IsOk(out _));
        Assert.True(coordinator.IsReady);
        Assert.Equal(RemoteInstanceCatalogMirror.MaximumBufferedChanges + 1, mirror.Current.Version);
        Assert.Equal("authoritative", mirror.Current.Value.Instances[FirstId].Name);
        Assert.Equal(3, transport.SendCount);
        await coordinator.CloseAsync().WaitAsync(Timeout);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"data\":null")]
    [InlineData(",\"meta\":{},\"data\":{\"version\":2,\"operation\":\"remove\",\"instance_id\":\"11111111-1111-1111-1111-111111111111\"}")]
    [InlineData(",\"data\":{\"version\":2,\"operation\":\"remove\",\"instance_id\":\"11111111-1111-1111-1111-111111111111\",\"unknown_secret\":true}")]
    public async Task MalformedRequiredCatalogEventFaultsEpochWithoutPublication(string fields)
    {
        var transport = new ScriptedTransport();
        var mirror = new RemoteInstanceCatalogMirror();
        var coordinator = Coordinator(transport, mirror);
        await MakeReadyAsync(coordinator, transport, Catalog(1, Item("one")));
        var historical = mirror.Current;

        coordinator.Core.RouteText(Utf8($"{{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.instance.catalog.changed\",\"params\":{{\"sequence\":9,\"timestamp\":9{fields}}}}}"));

        Assert.False(coordinator.IsReady);
        Assert.IsType<TransportDaemonError>(coordinator.TerminalError);
        Assert.Equal(V2ClientEventMaterializer.InvalidEventCode, coordinator.TerminalError!.Code);
        Assert.Equal(V2ClientEventMaterializer.InvalidEventMessage, coordinator.TerminalError.Message);
        Assert.Null(coordinator.TerminalError.Details);
        Assert.Same(historical, mirror.Current);
        Assert.Equal("one", mirror.Current.Value.Instances[FirstId].Name);
        await coordinator.CloseAsync().WaitAsync(Timeout);
    }

    [Fact]
    public async Task ConcurrentStartCallersShareWireWorkAndCallerCancellationOnlyCancelsItsWait()
    {
        var transport = new ScriptedTransport();
        var coordinator = Coordinator(transport, new RemoteInstanceCatalogMirror());
        using var caller = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceledWait = WaitAndStartAsync(release.Task, coordinator, caller.Token);
        var shared = Enumerable.Range(0, 32)
            .Select(_ => WaitAndStartAsync(release.Task, coordinator))
            .ToArray();
        release.SetResult();
        var subscribe = await transport.NextAsync();
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledWait.WaitAsync(Timeout));

        coordinator.Core.RouteText(Success(subscribe, "{}"));
        var snapshot = await transport.NextAsync();
        coordinator.Core.RouteText(Success(snapshot, Catalog(0)));
        var results = await Task.WhenAll(shared).WaitAsync(Timeout);

        Assert.All(results, result => Assert.True(result.IsOk(out _)));
        Assert.True(coordinator.IsReady);
        Assert.Equal(2, transport.SendCount);
        await coordinator.CloseAsync().WaitAsync(Timeout);
    }

    [Fact]
    public async Task RefetchErrorPreservesOriginalErrorAndLeavesStaleSnapshotReadable()
    {
        var transport = new ScriptedTransport();
        var mirror = new RemoteInstanceCatalogMirror();
        var coordinator = Coordinator(transport, mirror);
        await MakeReadyAsync(coordinator, transport, Catalog(1, Item("stale")));
        var historical = mirror.Current;

        coordinator.Core.RouteText(CatalogEvent(3, "gap"));
        var refetch = await transport.NextAsync();
        coordinator.Core.RouteText(Error(refetch, -32000, "catalog.refetch_failed", "storage", "failed"));
        var readiness = await coordinator.WaitForReadyAsync().WaitAsync(Timeout);

        Assert.True(readiness.IsErr(out var error));
        Assert.IsType<StorageDaemonError>(error);
        Assert.Equal("catalog.refetch_failed", error!.Code);
        Assert.Same(error, coordinator.TerminalError);
        Assert.False(coordinator.IsReady);
        Assert.Same(historical, mirror.Current);
        Assert.Equal("stale", mirror.Current.Value.Instances[FirstId].Name);
        await coordinator.CloseAsync().WaitAsync(Timeout);
    }

    [Fact]
    public async Task OverlappingEpochImmediatelyWithdrawsOldReadyAndOldEpochCannotRefetchOrPublish()
    {
        var mirror = new RemoteInstanceCatalogMirror();
        var oldTransport = new ScriptedTransport();
        var oldEpoch = Coordinator(oldTransport, mirror);
        await MakeReadyAsync(oldEpoch, oldTransport, Catalog(1, Item("stale")));

        var replacementTransport = new ScriptedTransport();
        var replacement = Coordinator(replacementTransport, mirror);
        var replacementStart = replacement.StartAsync();
        Assert.False(oldEpoch.IsReady);
        Assert.Equal("stale", mirror.Current.Value.Instances[FirstId].Name);

        oldEpoch.Core.RouteText(CatalogEvent(2, "old-event"));
        Assert.Equal("connection.epoch_superseded", oldEpoch.TerminalError!.Code);
        Assert.False(oldEpoch.IsReady);
        Assert.Equal(2, oldTransport.SendCount);
        Assert.Equal("stale", mirror.Current.Value.Instances[FirstId].Name);

        var subscribe = await replacementTransport.NextAsync();
        replacement.Core.RouteText(Success(subscribe, "{}"));
        var snapshot = await replacementTransport.NextAsync();
        replacement.Core.RouteText(Success(snapshot, Catalog(2, Item("fresh"))));
        Assert.True((await replacementStart.WaitAsync(Timeout)).IsOk(out _));
        Assert.True(replacement.IsReady);
        oldEpoch.Core.RouteText(CatalogEvent(3, "late-old-event"));
        Assert.Equal("fresh", mirror.Current.Value.Instances[FirstId].Name);
        Assert.Equal(2, oldTransport.SendCount);
    }

    [Fact]
    public async Task ExternallySupersededPendingSnapshotTerminatesOldEpochWithoutRefetchLoop()
    {
        var mirror = new RemoteInstanceCatalogMirror();
        var oldTransport = new ScriptedTransport();
        var oldEpoch = Coordinator(oldTransport, mirror);
        await MakeReadyAsync(oldEpoch, oldTransport, Catalog(1, Item("stale")));
        oldEpoch.Core.RouteText(CatalogEvent(3, "gap"));
        var staleSnapshot = await oldTransport.NextAsync();

        var replacementTransport = new ScriptedTransport();
        var replacement = Coordinator(replacementTransport, mirror);
        var replacementStart = replacement.StartAsync();
        oldEpoch.Core.RouteText(Error(staleSnapshot, -32000, "stale.failed", "storage", "stale"));

        Assert.True(SpinWait.SpinUntil(
            () => oldEpoch.TerminalError is not null,
            Timeout));
        Assert.Equal("connection.epoch_superseded", oldEpoch.TerminalError!.Code);
        Assert.NotEqual("stale.failed", oldEpoch.TerminalError.Code);
        Assert.Equal(3, oldTransport.SendCount);

        var oldReadiness = await oldEpoch.WaitForReadyAsync().WaitAsync(Timeout);

        Assert.True(oldReadiness.IsErr(out var superseded));
        Assert.Equal("connection.epoch_superseded", superseded!.Code);
        Assert.False(oldEpoch.IsReady);
        Assert.Equal(3, oldTransport.SendCount);
        Assert.Equal("stale", mirror.Current.Value.Instances[FirstId].Name);

        var subscribe = await replacementTransport.NextAsync();
        replacement.Core.RouteText(Success(subscribe, "{}"));
        var currentSnapshot = await replacementTransport.NextAsync();
        replacement.Core.RouteText(Success(currentSnapshot, Catalog(2, Item("fresh"))));
        Assert.True((await replacementStart.WaitAsync(Timeout)).IsOk(out _));
        Assert.True(replacement.IsReady);
        Assert.Equal("fresh", mirror.Current.Value.Instances[FirstId].Name);
    }

    [Fact]
    public async Task LocallySupersededSnapshotErrorIsIgnoredAndNewGenerationReachesReady()
    {
        var transport = new ScriptedTransport();
        var mirror = new RemoteInstanceCatalogMirror();
        var coordinator = Coordinator(transport, mirror);
        await MakeReadyAsync(coordinator, transport, Catalog(1, Item("one")));

        coordinator.Core.RouteText(CatalogEvent(3, "gap"));
        var staleRead = await transport.NextAsync();
        coordinator.Core.RouteText(CatalogEvent(4, "first"));
        coordinator.Core.RouteText(CatalogEvent(4, "conflict"));
        coordinator.Core.RouteText(Error(staleRead, -32000, "stale.failed", "storage", "stale"));

        var currentRead = await transport.NextAsync();
        Assert.Null(coordinator.TerminalError);
        Assert.False(coordinator.IsReady);
        coordinator.Core.RouteText(Success(currentRead, Catalog(4, Item("authoritative"))));
        var readiness = await coordinator.WaitForReadyAsync().WaitAsync(Timeout);

        Assert.True(readiness.IsOk(out _));
        Assert.True(coordinator.IsReady);
        Assert.Null(coordinator.TerminalError);
        Assert.Equal(4, mirror.Current.Version);
        Assert.Equal("authoritative", mirror.Current.Value.Instances[FirstId].Name);
        Assert.Equal(4, transport.SendCount);
    }

    [Fact]
    public async Task SnapshotErrorPreservesOriginalErrorAndFaultsEpoch()
    {
        var transport = new ScriptedTransport();
        var coordinator = Coordinator(transport, new RemoteInstanceCatalogMirror());
        var start = coordinator.StartAsync();
        var subscribe = await transport.NextAsync();
        coordinator.Core.RouteText(Success(subscribe, "{}"));
        var snapshot = await transport.NextAsync();
        coordinator.Core.RouteText(Error(snapshot, -32000, "catalog.failed", "storage", "failed"));

        var result = await start.WaitAsync(Timeout);
        Assert.True(result.IsErr(out var error));
        Assert.IsType<StorageDaemonError>(error);
        Assert.Equal("catalog.failed", error!.Code);
        Assert.Same(error, coordinator.TerminalError);
        Assert.False(coordinator.IsReady);
        await coordinator.CloseAsync().WaitAsync(Timeout);
    }

    [Fact]
    public async Task MalformedSnapshotFaultsWithCoreProtocolErrorAndNeverPublishes()
    {
        var transport = new ScriptedTransport();
        var mirror = new RemoteInstanceCatalogMirror();
        var coordinator = Coordinator(transport, mirror);
        var start = coordinator.StartAsync();
        var subscribe = await transport.NextAsync();
        coordinator.Core.RouteText(Success(subscribe, "{}"));
        var snapshot = await transport.NextAsync();
        coordinator.Core.RouteText(Success(snapshot, "{\"version\":1,\"items\":null}"));

        var result = await start.WaitAsync(Timeout);
        Assert.True(result.IsErr(out var error));
        Assert.IsType<TransportDaemonError>(error);
        Assert.Equal("protocol.result_invalid", error!.Code);
        Assert.Empty(mirror.Current.Value.Instances);
        Assert.False(coordinator.IsReady);
        await coordinator.CloseAsync().WaitAsync(Timeout);
    }

    [Fact]
    public async Task CloseDrainsPendingSnapshotAndPreventsLateReadyOrPublication()
    {
        var transport = new ScriptedTransport();
        var mirror = new RemoteInstanceCatalogMirror();
        var coordinator = Coordinator(transport, mirror);
        var start = coordinator.StartAsync();
        var subscribe = await transport.NextAsync();
        coordinator.Core.RouteText(Success(subscribe, "{}"));
        var snapshot = await transport.NextAsync();

        var close = coordinator.CloseAsync();
        coordinator.Core.RouteText(Success(snapshot, Catalog(4, Item("late"))));
        await close.WaitAsync(Timeout);
        var result = await start.WaitAsync(Timeout);

        Assert.True(result.IsErr(out var error));
        Assert.Equal("connection.closed", error!.Code);
        Assert.False(coordinator.IsReady);
        Assert.Empty(mirror.Current.Value.Instances);
        Assert.Equal(0, coordinator.Core.PendingCount);
    }

    [Fact]
    public async Task ConcurrentCloseResponseAndEventHaveNoLatePublicationReadyOrDeadlock()
    {
        var transport = new ScriptedTransport();
        var mirror = new RemoteInstanceCatalogMirror();
        var coordinator = Coordinator(transport, mirror);
        await MakeReadyAsync(coordinator, transport, Catalog(1, Item("one")));
        coordinator.Core.RouteText(CatalogEvent(3, "three"));
        var refetch = await transport.NextAsync();
        using var workersReady = new CountdownEvent(3);
        using var release = new ManualResetEventSlim();
        var failures = new ConcurrentQueue<Exception>();
        var threads = new[]
        {
            DedicatedWorker(() => coordinator.Core.RouteText(Success(refetch, Catalog(2, Item("two")))), workersReady, release, failures),
            DedicatedWorker(() => coordinator.Core.RouteText(CatalogEvent(3, "three")), workersReady, release, failures),
            DedicatedWorker(() => coordinator.CloseAsync().WaitAsync(Timeout).GetAwaiter().GetResult(), workersReady, release, failures)
        };

        try
        {
            foreach (var thread in threads)
                thread.Start();

            Assert.True(workersReady.Wait(Timeout));
            release.Set();
            Assert.All(threads, thread => Assert.True(thread.Join(Timeout)));
            await coordinator.CloseAsync().WaitAsync(Timeout);
        }
        finally
        {
            release.Set();
            foreach (var thread in threads)
            {
                if (thread.IsAlive)
                    thread.Join(Timeout);
            }

            await coordinator.CloseAsync().WaitAsync(Timeout);
        }

        Assert.Empty(failures);
        Assert.False(coordinator.IsReady);
        var afterClose = mirror.Current;
        coordinator.Core.RouteText(CatalogEvent(4, "late-event"));
        coordinator.Core.RouteText(Success(refetch, Catalog(4, Item("late-response"))));
        Assert.Same(afterClose, mirror.Current);
        Assert.False(coordinator.IsReady);
        Assert.Equal(0, coordinator.Core.PendingCount);
    }

    [Fact]
    public async Task CloseWaitsForIncompleteSendObserverUntilTransportIsExplicitlyReleased()
    {
        var transport = new IgnoringCancellationTransport();
        var coordinator = Coordinator(transport, new RemoteInstanceCatalogMirror());
        var start = coordinator.StartAsync();
        Task? close = null;

        try
        {
            await transport.Entered.WaitAsync(Timeout);
            close = coordinator.CloseAsync();
            await transport.CancellationObserved.WaitAsync(Timeout);
            await Assert.ThrowsAsync<TimeoutException>(() =>
                close.WaitAsync(TimeSpan.FromMilliseconds(100)));
            Assert.Equal(1, coordinator.Core.SendObserverCount);
        }
        finally
        {
            transport.Release();
            close ??= coordinator.CloseAsync();
            await close.WaitAsync(Timeout);
        }

        var result = await start.WaitAsync(Timeout);
        Assert.True(result.IsErr(out var error));
        Assert.Equal("connection.closed", error!.Code);
        Assert.Equal(0, coordinator.Core.SendObserverCount);
        Assert.Equal(0, coordinator.Core.ActiveSendLifetimeCount);
    }

    [Fact]
    public async Task ReplacementCoordinatorRetainsStaleSnapshotAndOldEpochCannotPublish()
    {
        var mirror = new RemoteInstanceCatalogMirror();
        var firstTransport = new ScriptedTransport();
        var first = Coordinator(firstTransport, mirror);
        await MakeReadyAsync(first, firstTransport, Catalog(1, Item("stale")));
        await first.CloseAsync().WaitAsync(Timeout);

        var replacementTransport = new ScriptedTransport();
        var replacement = Coordinator(replacementTransport, mirror);
        var start = replacement.StartAsync();
        var subscribe = await replacementTransport.NextAsync();
        replacement.Core.RouteText(Success(subscribe, "{}"));
        var snapshot = await replacementTransport.NextAsync();
        Assert.False(replacement.IsReady);
        Assert.Equal("stale", mirror.Current.Value.Instances[FirstId].Name);

        first.Core.RouteText(CatalogEvent(2, "old-epoch"));
        Assert.Equal("stale", mirror.Current.Value.Instances[FirstId].Name);
        replacement.Core.RouteText(Success(snapshot, Catalog(2, Item("fresh"))));
        Assert.True((await start.WaitAsync(Timeout)).IsOk(out _));
        Assert.True(replacement.IsReady);
        Assert.Equal("fresh", mirror.Current.Value.Instances[FirstId].Name);
        await replacement.CloseAsync().WaitAsync(Timeout);
    }

    [Fact]
    public async Task CloseBeforeStartIsTerminalAndSendsNoWireWork()
    {
        var transport = new ScriptedTransport();
        var coordinator = Coordinator(transport, new RemoteInstanceCatalogMirror());

        await coordinator.CloseAsync().WaitAsync(Timeout);
        var result = await coordinator.StartAsync();

        Assert.True(result.IsErr(out var error));
        Assert.Equal("connection.closed", error!.Code);
        Assert.Equal(0, transport.SendCount);
    }

    private V2ClientConnectionCoordinator Coordinator(
        IV2ClientWireTransport transport,
        RemoteInstanceCatalogMirror mirror)
    {
        var next = 0;
        var coordinator = new V2ClientConnectionCoordinator(
            transport,
            mirror,
            TimeProvider.System,
            TimeSpan.FromMinutes(1),
            () => JsonRpcRequestId.FromString($"request-{Interlocked.Increment(ref next)}"));
        _ownedCoordinators.Add(coordinator);
        return coordinator;
    }

    private static async Task MakeReadyAsync(
        V2ClientConnectionCoordinator coordinator,
        ScriptedTransport transport,
        string catalog)
    {
        var start = coordinator.StartAsync();
        var subscribe = await transport.NextAsync();
        coordinator.Core.RouteText(Success(subscribe, "{}"));
        var snapshot = await transport.NextAsync();
        coordinator.Core.RouteText(Success(snapshot, catalog));
        Assert.True((await start.WaitAsync(Timeout)).IsOk(out _));
        Assert.True(coordinator.IsReady);
    }

    private static async Task<RustyOptions.Result<RustyOptions.Unit, DaemonError>> WaitAndStartAsync(
        Task release,
        V2ClientConnectionCoordinator coordinator,
        CancellationToken cancellationToken = default)
    {
        await release.WaitAsync(Timeout);
        return await coordinator.StartAsync(cancellationToken).WaitAsync(Timeout);
    }

    private static async Task WaitUntilReadyAsync(V2ClientConnectionCoordinator coordinator)
    {
        var result = await coordinator.WaitForReadyAsync().WaitAsync(Timeout);
        Assert.True(result.IsOk(out _));
    }

    private static string Catalog(long version, params string[] items) =>
        $"{{\"version\":{version},\"items\":[{string.Join(',', items)}]}}";

    private static string Item(string name) =>
        $"{{\"instance_id\":\"{FirstId:D}\",\"name\":\"{name}\",\"instance_type\":\"universal\",\"version\":\"1\",\"status\":\"running\"}}";

    private static byte[] CatalogEvent(long version, string name) =>
        Utf8($"{{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.instance.catalog.changed\",\"params\":{{\"sequence\":{version},\"timestamp\":{version},\"data\":{{\"version\":{version},\"operation\":\"upsert\",\"instance_id\":\"{FirstId:D}\",\"snapshot\":{Item(name)}}}}}}}");

    private static byte[] Success(SentRequest request, string result) =>
        Utf8($"{{\"jsonrpc\":\"2.0\",\"id\":{request.IdJson},\"result\":{result}}}");

    private static byte[] Error(SentRequest request, int code, string daemonCode, string kind, string message) =>
        Utf8($"{{\"jsonrpc\":\"2.0\",\"id\":{request.IdJson},\"error\":{{\"code\":{code},\"message\":\"{message}\",\"data\":{{\"daemon_error_code\":\"{daemonCode}\",\"daemon_error_kind\":\"{kind}\",\"correlation_id\":\"test\"}}}}}}");

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static Thread DedicatedWorker(
        Action action,
        CountdownEvent ready,
        ManualResetEventSlim release,
        ConcurrentQueue<Exception> failures)
    {
        return new Thread(() =>
        {
            try
            {
                ready.Signal();
                if (!release.Wait(Timeout))
                    throw new TimeoutException("The concurrent coordinator test was not released.");

                action();
            }
            catch (Exception exception)
            {
                failures.Enqueue(exception);
            }
        })
        {
            IsBackground = true
        };
    }

    private sealed record SentRequest(string Method, string IdJson, string Json);

    private sealed class ScriptedTransport : IV2ClientWireTransport
    {
        private readonly ConcurrentQueue<SentRequest> _requests = new();
        private readonly SemaphoreSlim _available = new(0);
        private int _sendCount;

        internal int SendCount => Volatile.Read(ref _sendCount);

        public ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken)
        {
            var json = Encoding.UTF8.GetString(utf8Json.AsSpan());
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            _requests.Enqueue(new SentRequest(
                root.GetProperty("method").GetString()!,
                root.GetProperty("id").GetRawText(),
                json));
            Interlocked.Increment(ref _sendCount);
            _available.Release();
            return ValueTask.CompletedTask;
        }

        internal async Task<SentRequest> NextAsync()
        {
            Assert.True(await _available.WaitAsync(Timeout));
            Assert.True(_requests.TryDequeue(out var request));
            return request!;
        }
    }

    private sealed class IgnoringCancellationTransport : IV2ClientWireTransport
    {
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cancellationObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Entered => _entered.Task;
        internal Task CancellationObserved => _cancellationObserved.Task;

        public ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken)
        {
            cancellationToken.Register(() => _cancellationObserved.TrySetResult());
            _entered.TrySetResult();
            return new ValueTask(_release.Task);
        }

        internal void Release() => _release.TrySetResult();
    }
}
