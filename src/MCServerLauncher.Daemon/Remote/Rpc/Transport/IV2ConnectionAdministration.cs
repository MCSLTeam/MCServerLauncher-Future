using System.Collections.Immutable;

namespace MCServerLauncher.Daemon.Remote.Rpc.Transport;

internal interface IV2ConnectionAdministration
{
    ImmutableArray<V2ConnectionSnapshot> Snapshot();

    bool TryGet(string connectionId, out V2ConnectionSnapshot connection);

    Task<bool> CloseAsync(string connectionId);

    Task<int> CloseAllAsync();

    Task ShutdownAsync();
}

internal readonly record struct V2ConnectionSnapshot(
    string ConnectionId,
    string RemoteEndpoint,
    Guid TokenId,
    ImmutableArray<string> Permissions,
    DateTimeOffset ExpiresAt);
