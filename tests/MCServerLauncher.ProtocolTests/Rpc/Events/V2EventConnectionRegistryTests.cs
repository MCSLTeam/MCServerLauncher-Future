using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Events;
using MCServerLauncher.Daemon.Remote.Rpc.Transport;

namespace MCServerLauncher.ProtocolTests.Rpc.Events;

public sealed class V2EventConnectionRegistryTests
{
    [Fact]
    public async Task DuplicateAttachIsRejectedAndCleanupRemovesOnlyItsExactEntry()
    {
        var registry = new V2EventConnectionRegistry(CreateCatalog());
        var firstOwner = Owner();
        var duplicateOwner = Owner();

        Assert.Equal(V2EventConnectionAttachResult.Attached, registry.TryAttach("same", firstOwner, out var first));
        Assert.Equal(V2EventConnectionAttachResult.DuplicateConnectionId, registry.TryAttach("same", duplicateOwner, out var duplicate));
        Assert.Null(duplicate);
        Assert.Single(registry.Snapshot());
        Assert.Equal(1, registry.RawEntryCount);
        Assert.Equal(1, firstOwner.CleanupRegistrationCount);
        Assert.Equal(0, duplicateOwner.CleanupRegistrationCount);

        await duplicateOwner.AbortAsync();
        Assert.True(registry.TryGet("same", out var retained));
        Assert.Same(first, retained);

        await firstOwner.AbortAsync();
        Assert.Empty(registry.Snapshot());
        Assert.Equal(0, registry.RawEntryCount);
        Assert.Equal(1, first!.CleanupCount);
        first!.Close();
        Assert.Equal(1, first.CleanupCount);
        Assert.Empty(registry.Snapshot());
    }

    [Fact]
    public async Task ClosedOwnerAttachIsRejectedWithoutResidualEntry()
    {
        var registry = new V2EventConnectionRegistry(CreateCatalog());
        var owner = Owner();
        await owner.AbortAsync();

        Assert.Equal(V2EventConnectionAttachResult.ConnectionClosed, registry.TryAttach("closed", owner, out var entry));
        Assert.Null(entry);
        Assert.Empty(registry.Snapshot());
        Assert.Equal(0, registry.RawEntryCount);
    }

    [Fact]
    public void StaleEntryCloseDoesNotRemoveReplacementWithSameConnectionId()
    {
        var catalog = CreateCatalog();
        var registry = new V2EventConnectionRegistry(catalog);
        var staleOwner = Owner();
        var stale = new V2EventConnectionRegistry.V2EventConnectionEntry(
            "same",
            staleOwner,
            new V2EventSubscriptionLedger(catalog, staleOwner),
            registry);

        Assert.Equal(V2EventConnectionAttachResult.Attached, registry.TryAttach("same", Owner(), out var replacement));

        stale.Close();

        Assert.True(registry.TryGet("same", out var retained));
        Assert.Same(replacement, retained);
        Assert.Equal(1, registry.RawEntryCount);
    }

    [Fact]
    public async Task AttachCloseRaceNeverLeavesAnActiveEntry()
    {
        const int attempts = 128;
        var registry = new V2EventConnectionRegistry(CreateCatalog());
        var owner = Owner();
        using var barrier = new Barrier(2);

        var attach = Task.Run(() =>
        {
            Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(10)));
            for (var index = 0; index < attempts; index++)
                registry.TryAttach($"race-{index}", owner, out _);
        });
        var close = Task.Run(async () =>
        {
            Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(10)));
            await owner.AbortAsync();
        });

        await Task.WhenAll(attach, close).WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Empty(registry.Snapshot());
        Assert.Equal(0, registry.RawEntryCount);
        for (var index = 0; index < attempts; index++)
            Assert.False(registry.TryGet($"race-{index}", out _));
    }

    private static FrozenProtocolCatalog CreateCatalog()
    {
        var builder = new ProtocolCatalogBuilder(new OpenRpcInfo("registry", "1.0.0"));
        var descriptor = BuiltInProtocolDefinitions.Events
            .Single(item => item.Name.Value == "mcsl.event.daemon.report");
        builder.RegisterBuiltInEvent(
            descriptor,
            new EventBinding<DaemonReportEventData>(ProtocolExecutionOwner.BuiltIn));
        return builder.Freeze();
    }

    private static V2ConnectionOwner Owner() => new(new NoOpSender(), ["*"]);

    private sealed class NoOpSender : IV2OutboundSender
    {
        public ValueTask SendAsync(V2OutboundFrame frame, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask CloseAsync(V2ConnectionCloseReason reason, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
