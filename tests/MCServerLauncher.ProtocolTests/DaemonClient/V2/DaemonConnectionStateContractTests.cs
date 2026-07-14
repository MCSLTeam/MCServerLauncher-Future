using MCServerLauncher.DaemonClient;

namespace MCServerLauncher.ProtocolTests.DaemonClient.V2;

public sealed class DaemonConnectionStateContractTests
{
    [Fact]
    public void StateValuesAreFrozen()
    {
        Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(DaemonConnectionState)));
        Assert.Equal(
            [
                nameof(DaemonConnectionState.Disconnected),
                nameof(DaemonConnectionState.Connecting),
                nameof(DaemonConnectionState.Synchronizing),
                nameof(DaemonConnectionState.Ready),
                nameof(DaemonConnectionState.Reconnecting),
                nameof(DaemonConnectionState.Closing),
                nameof(DaemonConnectionState.Closed)
            ],
            Enum.GetNames<DaemonConnectionState>());

        Assert.Equal(0, (int)DaemonConnectionState.Disconnected);
        Assert.Equal(1, (int)DaemonConnectionState.Connecting);
        Assert.Equal(2, (int)DaemonConnectionState.Synchronizing);
        Assert.Equal(3, (int)DaemonConnectionState.Ready);
        Assert.Equal(4, (int)DaemonConnectionState.Reconnecting);
        Assert.Equal(5, (int)DaemonConnectionState.Closing);
        Assert.Equal(6, (int)DaemonConnectionState.Closed);
    }

    [Fact]
    public void DefaultStateIsDisconnectedAndTypeIsPublicInRootNamespace()
    {
        DaemonConnectionState state = default;

        Assert.Equal(DaemonConnectionState.Disconnected, state);
        Assert.True(typeof(DaemonConnectionState).IsPublic);
        Assert.Equal("MCServerLauncher.DaemonClient", typeof(DaemonConnectionState).Namespace);
        Assert.Equal("MCServerLauncher.DaemonClient", typeof(DaemonConnectionState).Assembly.GetName().Name);
        Assert.Same(
            typeof(global::MCServerLauncher.DaemonClient.Daemon).Assembly,
            typeof(DaemonConnectionState).Assembly);
    }
}
