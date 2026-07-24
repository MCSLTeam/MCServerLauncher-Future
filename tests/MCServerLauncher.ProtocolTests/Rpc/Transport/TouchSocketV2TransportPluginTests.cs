using System.Net.WebSockets;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Dispatch;
using MCServerLauncher.Daemon.Remote.Rpc.Events;
using MCServerLauncher.Daemon.Remote.Rpc.Files;
using MCServerLauncher.Daemon.Remote.Rpc.Transport;
using RustyOptions;
using TouchSocket.Http.WebSockets;

namespace MCServerLauncher.ProtocolTests.Rpc.Transport;

public sealed class TouchSocketV2TransportPluginTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void Authentication_RejectsDecodableForgedAndExpiredTokens()
    {
        var time = new FixedTimeProvider(DateTimeOffset.Parse("2030-01-01T00:00:00Z"));
        var forged = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            claims: [new Claim("permissions", "*"), new Claim("jti", Guid.NewGuid().ToString())],
            expires: time.GetUtcNow().AddHours(1).UtcDateTime));
        Assert.Equal("*", new JwtSecurityTokenHandler().ReadJwtToken(forged).Claims.Single(c => c.Type == "permissions").Value);
        Assert.False(TouchSocketV2TransportPlugin.TryAuthenticateToken(forged, time, out _));
        Assert.False(TouchSocketV2TransportPlugin.TryAuthenticateToken("expired", time,
            static _ => true,
            static _ => (Guid.NewGuid(), "user-a", "*", DateTime.Parse("2029-12-31T23:59:59Z").ToUniversalTime()), out _));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void Authentication_InjectedClaimsRejectMissingSubjectOrTokenId(
        bool hasSubject,
        bool hasTokenId)
    {
        var tokenId = hasTokenId ? Guid.NewGuid() : Guid.Empty;
        var subject = hasSubject ? "user-a" : null;

        Assert.False(TouchSocketV2TransportPlugin.TryAuthenticateToken(
            "signed-jwt",
            TimeProvider.System,
            static _ => true,
            _ => (tokenId, subject, "mcsl.instance.catalog.get", DateTime.UtcNow.AddMinutes(5)),
            out _));
    }

    [Theory]
    [InlineData(WSDataType.Text)]
    [InlineData(WSDataType.Binary)]
    public void TouchSocketCombinator_AssemblesCompleteFragmentedMessages(WSDataType opcode)
    {
        var assembler = new TouchSocketV2MessageAssembler(new WebSocketMessageCombinator());
        var first = new WSDataFrame(new byte[] { 1, 2 }) { Opcode = opcode, FIN = false };
        var last = new WSDataFrame(new byte[] { 3, 4 }) { Opcode = WSDataType.Cont, FIN = true };

        Assert.False(assembler.TryAssemble(first, out _));
        Assert.True(assembler.TryAssemble(last, out var message));
        using (message)
        {
            Assert.Equal(opcode, message.Opcode);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, message.PayloadData.ToArray());
        }
    }

    [Fact]
    public void TouchSocketCombinator_RejectsContinuationWithoutInitialFrame()
    {
        var assembler = new TouchSocketV2MessageAssembler(new WebSocketMessageCombinator());
        var continuation = new WSDataFrame(new byte[] { 1 }) { Opcode = WSDataType.Cont, FIN = true };
        Assert.ThrowsAny<Exception>(() => assembler.TryAssemble(continuation, out _));
        var valid = new WSDataFrame(new byte[] { 2 }) { Opcode = WSDataType.Text, FIN = true };
        Assert.True(assembler.TryAssemble(valid, out var recovered));
        recovered.Dispose();
    }

    [Fact]
    public void TouchSocketCombinator_RejectsInterleavedInitialFramesAndRecovers()
    {
        var assembler = new TouchSocketV2MessageAssembler(new WebSocketMessageCombinator());
        Assert.False(assembler.TryAssemble(
            new WSDataFrame(new byte[] { 1 }) { Opcode = WSDataType.Text, FIN = false }, out _));
        Assert.Throws<InvalidDataException>(() => assembler.TryAssemble(
            new WSDataFrame(new byte[] { 2 }) { Opcode = WSDataType.Binary, FIN = false }, out _));

        Assert.True(assembler.TryAssemble(
            new WSDataFrame(new byte[] { 3 }) { Opcode = WSDataType.Binary, FIN = true }, out var recovered));
        using (recovered)
            Assert.Equal(new byte[] { 3 }, recovered.PayloadData.ToArray());
    }

    [Fact]
    public void TouchSocketCombinator_BoundsAggregatePayloadAndRecovers()
    {
        var assembler = new TouchSocketV2MessageAssembler(new WebSocketMessageCombinator());
        Assert.False(assembler.TryAssemble(
            new WSDataFrame(new byte[TouchSocketV2MessageAssembler.MaxBinaryMessageSize])
                { Opcode = WSDataType.Binary, FIN = false }, out _));
        Assert.Throws<InvalidDataException>(() => assembler.TryAssemble(
            new WSDataFrame(new byte[] { 1 }) { Opcode = WSDataType.Cont, FIN = true }, out _));

        Assert.True(assembler.TryAssemble(
            new WSDataFrame(new byte[] { 4 }) { Opcode = WSDataType.Text, FIN = true }, out var recovered));
        recovered.Dispose();
    }

    [Fact]
    public void TouchSocketCombinator_ControlFrameDoesNotBreakFragmentedMessage()
    {
        var assembler = new TouchSocketV2MessageAssembler(new WebSocketMessageCombinator());
        Assert.False(assembler.TryAssemble(
            new WSDataFrame(new byte[] { 1 }) { Opcode = WSDataType.Text, FIN = false }, out _));
        Assert.True(assembler.TryAssemble(
            new WSDataFrame(new byte[] { 9 }) { Opcode = WSDataType.Ping, FIN = true }, out var control));
        control.Dispose();
        Assert.True(assembler.TryAssemble(
            new WSDataFrame(new byte[] { 2 }) { Opcode = WSDataType.Cont, FIN = true }, out var message));
        using (message)
            Assert.Equal(new byte[] { 1, 2 }, message.PayloadData.ToArray());
    }

    [Fact]
    public async Task NonV2Callbacks_InvokeNextAndDoNotCreateState()
    {
        await using var fixture = Fixture.Create();
        var next = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await fixture.Plugin.HandleConnectedAsync("/other", "other", [], fixture.Sender("other"),
            () => { next.TrySetResult(); return Task.CompletedTask; }, Never, Never);
        await next.Task.WaitAsync(Timeout);
        await fixture.Plugin.HandleReceivedAsync("other", WSDataType.Text, Utf8("{}"), () => Task.CompletedTask);
        await fixture.Plugin.HandleClosedAsync("other", () => Task.CompletedTask);

        Assert.Equal(0, fixture.Plugin.ConnectionCount);
        Assert.Equal(0, fixture.Events.RawEntryCount);
    }

    [Fact]
    public async Task V2Connected_BuildsOneStateWithImmutablePermissionSnapshotAndSuppressesNext()
    {
        await using var fixture = Fixture.Create();
        var permissions = new[] { "mcsl.file.upload" };
        var nextCalls = 0;

        await fixture.Plugin.HandleConnectedAsync(TouchSocketV2TransportPlugin.Endpoint, "v2", permissions,
            fixture.Sender("v2"), () => { nextCalls++; return Task.CompletedTask; }, Never, Never);
        permissions[0] = "changed";

        Assert.Equal(0, nextCalls);
        Assert.Equal(1, fixture.Plugin.ConnectionCount);
        Assert.True(fixture.Events.TryGet("v2", out var entry));
        Assert.Single(entry!.Owner.Permissions);
        Assert.Equal("mcsl.file.upload", entry.Owner.Permissions[0]);
    }

    [Fact]
    public async Task ConnectionAdministration_ProvidesImmutableLookupAndOwnedCloseOperations()
    {
        await using var fixture = Fixture.Create();
        IV2ConnectionAdministration administration = fixture.Plugin;
        var first = fixture.Sender("first");
        var second = fixture.Sender("second");
        var firstToken = Guid.NewGuid();
        var expiry = DateTimeOffset.Parse("2030-01-01T01:00:00Z");

        await fixture.Plugin.HandleConnectedAsync(
            TouchSocketV2TransportPlugin.Endpoint,
            "b-connection",
            ["mcsl.file.upload"],
            second,
            Never,
            Never,
            Never,
            expiry,
            Guid.NewGuid(),
            "127.0.0.1:20002");
        await fixture.Plugin.HandleConnectedAsync(
            TouchSocketV2TransportPlugin.Endpoint,
            "a-connection",
            ["mcsl.daemon.instance.read"],
            first,
            Never,
            Never,
            Never,
            expiry,
            firstToken,
            "127.0.0.1:20001");

        var snapshot = administration.Snapshot();
        Assert.Equal(["a-connection", "b-connection"], snapshot.Select(static item => item.ConnectionId));
        Assert.True(administration.TryGet("a-connection", out var found));
        Assert.Equal("127.0.0.1:20001", found.RemoteEndpoint);
        Assert.Equal(firstToken, found.TokenId);
        Assert.Equal("mcsl.daemon.instance.read", Assert.Single(found.Permissions));
        Assert.Equal(expiry, found.ExpiresAt);

        Assert.True(await administration.CloseAsync("a-connection"));
        await first.Closed.Task.WaitAsync(Timeout);
        Assert.False(administration.TryGet("a-connection", out _));
        Assert.False(await administration.CloseAsync("a-connection"));
        Assert.Equal(2, snapshot.Length);

        Assert.Equal(1, await administration.CloseAllAsync());
        await second.Closed.Task.WaitAsync(Timeout);
        Assert.Empty(administration.Snapshot());
    }

    [Fact]
    public async Task FutureTokenExpiry_RemovesAndClosesConnectionOnInjectedClock()
    {
        var now = DateTimeOffset.Parse("2030-01-01T00:00:00Z");
        var time = new ManualTimeProvider(now);
        await using var fixture = Fixture.Create(time);
        var sender = fixture.Sender("expiring");
        await fixture.Plugin.HandleConnectedAsync(TouchSocketV2TransportPlugin.Endpoint, "expiring", ["*"], sender,
            Never, Never, Never, now.AddMinutes(1));

        time.Advance(TimeSpan.FromMinutes(1));
        await sender.Closed.Task.WaitAsync(Timeout);

        Assert.Equal(0, fixture.Plugin.ConnectionCount);
        Assert.Equal(0, fixture.Events.RawEntryCount);
        Assert.Equal(1, sender.CloseCount);
    }

    [Fact]
    public async Task V2TextAndBinaryFrames_UseInboundPipelineAndSuppressNext()
    {
        await using var fixture = Fixture.Create();
        var sender = fixture.Sender("frames");
        await fixture.ConnectAsync("frames", sender);
        var nextCalls = 0;

        await fixture.Plugin.HandleReceivedAsync("frames", WSDataType.Text,
            Utf8("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"missing\"}"),
            () => { nextCalls++; return Task.CompletedTask; });
        await sender.Sent.Task.WaitAsync(Timeout);

        await fixture.Plugin.HandleReceivedAsync("frames", WSDataType.Binary, new byte[] { 1 },
            () => { nextCalls++; return Task.CompletedTask; });
        await sender.Closed.Task.WaitAsync(Timeout);

        Assert.Equal(0, nextCalls);
        Assert.Equal(1, fixture.Diagnostics.BinaryFaults);
        Assert.Equal(0, fixture.Plugin.ConnectionCount);
    }

    [Fact]
    public async Task PerConnectionAssembler_DeliversOnlyTheCompletedFragmentedMessage()
    {
        await using var fixture = Fixture.Create();
        var sender = fixture.Sender("fragmented-core");
        await fixture.ConnectAsync("fragmented-core", sender);
        var combinator = new WebSocketMessageCombinator();
        var json = Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"missing\"}");
        var split = json.Length / 2;

        await fixture.Plugin.HandleFrameAsync("fragmented-core", combinator,
            new WSDataFrame(json.AsMemory(0, split)) { Opcode = WSDataType.Text, FIN = false }, Never);
        Assert.False(sender.Sent.Task.IsCompleted);
        await fixture.Plugin.HandleFrameAsync("fragmented-core", combinator,
            new WSDataFrame(json.AsMemory(split)) { Opcode = WSDataType.Cont, FIN = true }, Never);

        await sender.Sent.Task.WaitAsync(Timeout);
        Assert.Equal(1, fixture.Plugin.ConnectionCount);
    }

    [Fact]
    public async Task UnsupportedOpcode_AbortsV2ConnectionWithoutInvokingNext()
    {
        await using var fixture = Fixture.Create();
        var sender = fixture.Sender("opcode");
        await fixture.ConnectAsync("opcode", sender);
        var nextCalls = 0;

        await fixture.Plugin.HandleReceivedAsync("opcode", (WSDataType)99, Array.Empty<byte>(),
            () => { nextCalls++; return Task.CompletedTask; });
        await sender.Closed.Task.WaitAsync(Timeout);

        Assert.Equal(0, nextCalls);
        Assert.Equal(1, sender.CloseCount);
        Assert.Equal(0, fixture.Events.RawEntryCount);
    }

    [Fact]
    public async Task UnsupportedOpcode_DoesNotWaitForPhysicalCloseInsideReceiveCallback()
    {
        await using var fixture = Fixture.Create();
        var callbackReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new ReentrantCloseSender(callbackReturned.Task, closeEntered);
        await fixture.Plugin.HandleConnectedAsync(TouchSocketV2TransportPlugin.Endpoint, "reentrant", ["*"],
            sender, Never, Never, Never);

        var receive = fixture.Plugin.HandleReceivedAsync("reentrant", (WSDataType)99, Array.Empty<byte>(), Never);
        await receive.WaitAsync(Timeout);
        callbackReturned.SetResult();
        await closeEntered.Task.WaitAsync(Timeout);

        Assert.Equal(0, fixture.Plugin.ConnectionCount);
        Assert.Equal(0, fixture.Events.RawEntryCount);
    }

    [Fact]
    public async Task ClosingWindow_RetainsV2TombstoneUntilClosedCallback()
    {
        await using var fixture = Fixture.Create();
        var releaseClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new ReentrantCloseSender(releaseClose.Task, closeEntered);
        await fixture.Plugin.HandleConnectedAsync(TouchSocketV2TransportPlugin.Endpoint, "tombstone", ["*"],
            sender, Never, Never, Never);

        await fixture.Plugin.HandleReceivedAsync("tombstone", (WSDataType)99, Array.Empty<byte>(), Never);
        await closeEntered.Task.WaitAsync(Timeout);
        var nextCalls = 0;
        await fixture.Plugin.HandleReceivedAsync("tombstone", WSDataType.Text, Utf8("{}"),
            () => { nextCalls++; return Task.CompletedTask; });
        await fixture.Plugin.HandleClosedAsync("tombstone", () => { nextCalls++; return Task.CompletedTask; });
        Assert.Equal(0, nextCalls);
        Assert.Equal(0, fixture.Plugin.V2ConnectionMarkerCount);

        releaseClose.TrySetResult();
        await fixture.Plugin.HandleReceivedAsync("tombstone", WSDataType.Text, Utf8("{}"),
            () => { nextCalls++; return Task.CompletedTask; });
        Assert.Equal(1, nextCalls);
    }

    [Fact]
    public async Task RejectedV2Connection_KeepsTombstoneAndSuppressesCallbacks()
    {
        await using var fixture = Fixture.Create();
        await fixture.Plugin.ShutdownAsync();
        var rejected = 0;
        await fixture.Plugin.HandleConnectedAsync(TouchSocketV2TransportPlugin.Endpoint, "rejected", ["*"],
            fixture.Sender("rejected"), Never, () => { rejected++; return Task.CompletedTask; }, Never);
        var nextCalls = 0;
        await fixture.Plugin.HandleReceivedAsync("rejected", WSDataType.Text, Utf8("{}"),
            () => { nextCalls++; return Task.CompletedTask; });

        Assert.Equal(1, rejected);
        Assert.Equal(0, nextCalls);
        Assert.Equal(1, fixture.Plugin.V2ConnectionMarkerCount);
        await fixture.Plugin.HandleClosedAsync("rejected", () => { nextCalls++; return Task.CompletedTask; });
        Assert.Equal(0, nextCalls);
        Assert.Equal(0, fixture.Plugin.V2ConnectionMarkerCount);
    }

    [Fact]
    public async Task InvalidAuthenticationCloseFailure_IsDiagnosedAndLateFrameNeverInvokesNext()
    {
        await using var fixture = Fixture.Create();
        await fixture.Plugin.RejectV2Async("invalid-auth",
            static () => Task.FromException(new IOException("physical reject close failed")));
        var nextCalls = 0;
        await fixture.Plugin.HandleReceivedAsync("invalid-auth", WSDataType.Text, Utf8("{}"),
            () => { nextCalls++; return Task.CompletedTask; });

        Assert.Equal(1, fixture.Diagnostics.UnexpectedCount);
        Assert.Equal(0, nextCalls);
        Assert.Equal(1, fixture.Plugin.V2ConnectionMarkerCount);
        await fixture.Plugin.HandleClosedAsync("invalid-auth", Never);
        Assert.Equal(0, fixture.Plugin.V2ConnectionMarkerCount);
    }

    [Fact]
    public async Task AutonomousSendAndCloseFailure_RemovesActiveStateButKeepsTombstone()
    {
        await using var fixture = Fixture.Create();
        await fixture.Plugin.HandleConnectedAsync(TouchSocketV2TransportPlugin.Endpoint, "failing", ["*"],
            new FailingSender(), Never, Never, Never);
        await fixture.Plugin.HandleReceivedAsync("failing", WSDataType.Text,
            Utf8("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"missing\"}"), Never);
        await fixture.Diagnostics.Unexpected.Task.WaitAsync(Timeout);

        Assert.Equal(0, fixture.Plugin.ConnectionCount);
        Assert.Equal(1, fixture.Plugin.V2ConnectionMarkerCount);
        var nextCalls = 0;
        await fixture.Plugin.HandleReceivedAsync("failing", WSDataType.Text, Utf8("{}"),
            () => { nextCalls++; return Task.CompletedTask; });
        Assert.Equal(0, nextCalls);
    }

    [Fact]
    public async Task ShutdownBeforeExpiryStarts_DoesNotUseDisposedCancellationSource()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var now = DateTimeOffset.Parse("2030-01-01T00:00:00Z");
        await using var fixture = Fixture.Create(new ManualTimeProvider(now), async () =>
        {
            entered.TrySetResult();
            await release.Task;
        });
        var connect = fixture.Plugin.HandleConnectedAsync(TouchSocketV2TransportPlugin.Endpoint, "expiry-race", ["*"],
            fixture.Sender("expiry-race"), Never, Never, Never, now.AddMinutes(1));
        await entered.Task.WaitAsync(Timeout);

        await fixture.Plugin.ShutdownAsync().WaitAsync(Timeout);
        release.TrySetResult();
        await connect.WaitAsync(Timeout);

        Assert.Equal(0, fixture.Plugin.ConnectionCount);
        Assert.Equal(0, fixture.Diagnostics.UnexpectedCount);
    }

    [Fact]
    public async Task ExpiryAndShutdownRace_CleansOwnerAndRegistryExactlyOnce()
    {
        var now = DateTimeOffset.Parse("2030-01-01T00:00:00Z");
        var time = new ManualTimeProvider(now);
        await using var fixture = Fixture.Create(time);
        var sender = fixture.Sender("expiry-shutdown-race");
        await fixture.Plugin.HandleConnectedAsync(TouchSocketV2TransportPlugin.Endpoint, "expiry-shutdown-race", ["*"],
            sender, Never, Never, Never, now.AddMinutes(1));
        using var barrier = new Barrier(2);

        var expire = Task.Run(() =>
        {
            Assert.True(barrier.SignalAndWait(Timeout));
            time.Advance(TimeSpan.FromMinutes(1));
        });
        var shutdown = Task.Run(async () =>
        {
            Assert.True(barrier.SignalAndWait(Timeout));
            await fixture.Plugin.ShutdownAsync();
        });
        await Task.WhenAll(expire, shutdown).WaitAsync(Timeout);
        await sender.Closed.Task.WaitAsync(Timeout);

        Assert.Equal(1, sender.CloseCount);
        Assert.Equal(0, fixture.Plugin.ConnectionCount);
        Assert.Equal(0, fixture.Events.RawEntryCount);
        Assert.Equal(0, fixture.Diagnostics.UnexpectedCount);
    }

    [Fact]
    public async Task DuplicateConnectionId_CleansPartialAttachExactlyOnce()
    {
        await using var fixture = Fixture.Create();
        await fixture.ConnectAsync("duplicate", fixture.Sender("first"));
        var rejected = fixture.Sender("second");

        await fixture.Plugin.HandleConnectedAsync(TouchSocketV2TransportPlugin.Endpoint, "duplicate", ["*"], rejected,
            Never, Never, Never);
        await rejected.Closed.Task.WaitAsync(Timeout);

        Assert.Equal(1, rejected.CloseCount);
        Assert.Equal(1, fixture.Plugin.ConnectionCount);
        Assert.Equal(1, fixture.Events.RawEntryCount);
        Assert.True(fixture.Events.TryGet("duplicate", out _));
    }

    [Fact]
    public async Task PeerCloseAndShutdownRace_DoesNotLeakOrDoubleClean()
    {
        await using var fixture = Fixture.Create();
        var sender = fixture.Sender("race");
        await fixture.ConnectAsync("race", sender);
        using var barrier = new Barrier(2);

        var peer = Task.Run(async () =>
        {
            Assert.True(barrier.SignalAndWait(Timeout));
            await fixture.Plugin.HandleClosedAsync("race", Never);
        });
        var shutdown = Task.Run(async () =>
        {
            Assert.True(barrier.SignalAndWait(Timeout));
            await fixture.Plugin.ShutdownAsync();
        });
        await Task.WhenAll(peer, shutdown).WaitAsync(Timeout);

        Assert.Equal(0, fixture.Plugin.ConnectionCount);
        Assert.Equal(0, fixture.Events.RawEntryCount);
        Assert.InRange(sender.CloseCount, 0, 1);
    }

    [Fact]
    public async Task Shutdown_DrainsConnectionsAndSubsequentCallbacksAreIdempotent()
    {
        await using var fixture = Fixture.Create();
        var first = fixture.Sender("one");
        var second = fixture.Sender("two");
        await fixture.ConnectAsync("one", first);
        await fixture.ConnectAsync("two", second);

        await fixture.Plugin.ShutdownAsync().WaitAsync(Timeout);
        await Task.WhenAll(first.Closed.Task, second.Closed.Task).WaitAsync(Timeout);
        var nextCalls = 0;
        await fixture.Plugin.HandleReceivedAsync("one", WSDataType.Text, Utf8("{}"),
            () => { nextCalls++; return Task.CompletedTask; });
        await fixture.Plugin.HandleClosedAsync("two", () => { nextCalls++; return Task.CompletedTask; });
        await fixture.Plugin.ShutdownAsync();

        Assert.Equal(0, nextCalls);
        Assert.Equal(0, fixture.Plugin.ConnectionCount);
        Assert.Equal(0, fixture.Events.RawEntryCount);
    }

    private static Task Never() => Task.CompletedTask;
    private static ReadOnlyMemory<byte> Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() { lock (_gate) return _now; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state, dueTime, period);
            lock (_gate) _timers.Add(timer);
            return timer;
        }
        internal void Advance(TimeSpan duration)
        {
            ManualTimer[] due;
            lock (_gate)
            {
                _now += duration;
                due = _timers.Where(timer => timer.DueAt <= _now).ToArray();
            }
            foreach (var timer in due) timer.Fire(_now);
        }
        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state,
            TimeSpan dueTime, TimeSpan period) : ITimer
        {
            private int _disposed;
            internal DateTimeOffset DueAt { get; private set; } = owner._now + dueTime;
            public bool Change(TimeSpan due, TimeSpan nextPeriod) { DueAt = owner._now + due; period = nextPeriod; return true; }
            public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
            internal void Fire(DateTimeOffset now)
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                DueAt = period == global::System.Threading.Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : now + period;
                callback(state);
            }
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(TouchSocketV2TransportPlugin plugin, V2EventConnectionRegistry events, Diagnostics diagnostics)
        {
            Plugin = plugin;
            Events = events;
            Diagnostics = diagnostics;
        }

        internal TouchSocketV2TransportPlugin Plugin { get; }
        internal V2EventConnectionRegistry Events { get; }
        internal Diagnostics Diagnostics { get; }
        internal static Fixture Create(TimeProvider? timeProvider = null, Func<Task>? beforeStartExpiry = null)
        {
            var files = new FakeFiles();
            var builder = new ProtocolCatalogBuilder(new OpenRpcInfo("transport", "1"));
            BuiltInFileRpcRegistrar.Register(builder, files);
            var catalog = builder.Freeze();
            var events = new V2EventConnectionRegistry(catalog);
            var diagnostics = new Diagnostics();
            return new(new TouchSocketV2TransportPlugin(new TestApplication(files), catalog, events,
                new RpcDiagnostics(), diagnostics, timeProvider, beforeStartExpiry), events, diagnostics);
        }

        internal RecordingSender Sender(string name) => new(name);
        internal Task ConnectAsync(string id, RecordingSender sender) =>
            Plugin.HandleConnectedAsync(TouchSocketV2TransportPlugin.Endpoint, id, ["*"], sender, Never, Never, Never);
        public async ValueTask DisposeAsync() => await Plugin.DisposeAsync();
    }

    private sealed class RecordingSender(string name) : IV2OutboundSender
    {
        internal TaskCompletionSource Sent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int CloseCount { get; private set; }
        public ValueTask SendAsync(V2OutboundFrame frame, CancellationToken cancellationToken)
        {
            Sent.TrySetResult();
            return ValueTask.CompletedTask;
        }
        public ValueTask CloseAsync(V2ConnectionCloseReason reason, CancellationToken cancellationToken)
        {
            CloseCount++;
            Closed.TrySetResult();
            return ValueTask.CompletedTask;
        }
        public override string ToString() => name;
    }

    private sealed class ReentrantCloseSender(Task callbackReturned, TaskCompletionSource closeEntered) : IV2OutboundSender
    {
        public ValueTask SendAsync(V2OutboundFrame frame, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public async ValueTask CloseAsync(V2ConnectionCloseReason reason, CancellationToken cancellationToken)
        {
            closeEntered.TrySetResult();
            await callbackReturned.WaitAsync(cancellationToken);
        }
    }

    private sealed class FailingSender : IV2OutboundSender
    {
        public ValueTask SendAsync(V2OutboundFrame frame, CancellationToken cancellationToken) =>
            ValueTask.FromException(new IOException("send failed"));
        public ValueTask CloseAsync(V2ConnectionCloseReason reason, CancellationToken cancellationToken) =>
            ValueTask.FromException(new IOException("close failed"));
    }

    private sealed class Diagnostics : IV2InboundDiagnosticSink
    {
        internal int BinaryFaults { get; private set; }
        internal int UnexpectedCount { get; private set; }
        internal TaskCompletionSource Unexpected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void RecordUnexpected(string correlationId, Exception exception)
        {
            UnexpectedCount++;
            Unexpected.TrySetResult();
        }
        public void RecordBinaryFault(BinaryFrameReadResult readResult) => BinaryFaults++;
    }

    private sealed class RpcDiagnostics : IV2RpcDiagnosticSink
    {
        public void RecordUnexpected(V2RpcUnexpectedDiagnostic diagnostic) { }
        public void RecordNotificationSuppressed(V2RpcNotificationSuppressionDiagnostic diagnostic) { }
    }

    private sealed class TestApplication(IFileApplication files) : IDaemonApplication
    {
        public IInstanceApplication Instances => throw new NotSupportedException();
        public IFileApplication Files { get; } = files;
        public ISystemApplication System => throw new NotSupportedException();
        public IEventRuleApplication EventRules => throw new NotSupportedException();

        public IOperationApplication Operations { get; } = null!;
        public IProvisioningApplication Provisioning { get; } = null!;
    }

    private sealed class FakeFiles : IFileApplication
    {
        public Task<Result<UploadSession, DaemonError>> OpenUploadAsync(UploadOpenRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> WriteUploadChunkAsync(UploadChunkRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CloseUploadAsync(Guid sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CancelUploadAsync(Guid sessionId, CancellationToken cancellationToken) => Task.FromResult(Result.Ok<Unit, DaemonError>(Unit.Default));
        public Task<Result<DownloadSession, DaemonError>> OpenDownloadAsync(DownloadOpenRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<DownloadChunk, DaemonError>> ReadDownloadChunkAsync(DownloadChunkRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CloseDownloadAsync(Guid sessionId, CancellationToken cancellationToken) => Task.FromResult(Result.Ok<Unit, DaemonError>(Unit.Default));
        public Task<Result<DirectoryDetails, DaemonError>> GetDirectoryInfoAsync(PathRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<FileDetails, DaemonError>> GetFileInfoAsync(PathRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CreateDirectoryAsync(PathRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> DeleteFileAsync(PathRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> DeleteDirectoryAsync(DeleteDirectoryRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> RenameFileAsync(PathRenameRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> RenameDirectoryAsync(PathRenameRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> MoveFileAsync(PathTransferRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> MoveDirectoryAsync(PathTransferRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CopyFileAsync(PathTransferRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CopyDirectoryAsync(PathTransferRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
