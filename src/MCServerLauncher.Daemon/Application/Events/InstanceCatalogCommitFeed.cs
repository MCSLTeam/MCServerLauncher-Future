using System.Threading.Channels;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.State;

namespace MCServerLauncher.Daemon.ApplicationCore.Events;

internal sealed record InstanceCatalogCommit(
    long Version,
    InstanceCatalogChangeOperation Operation,
    Guid InstanceId,
    InstanceSnapshot? Snapshot);

/// <summary>
/// Carries authoritative catalog commits from the snapshot publication lock to one awaited event publisher.
/// </summary>
internal sealed class InstanceCatalogCommitFeed
{
    private readonly Channel<InstanceCatalogCommit> _channel = Channel.CreateUnbounded<InstanceCatalogCommit>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    private readonly object _productionGate = new();
    private bool _accepting = true;
    private int _readerClaimed;

    internal void Publish(Func<InstanceCatalogCommit> publishStateAndCreateCommit)
    {
        ArgumentNullException.ThrowIfNull(publishStateAndCreateCommit);
        lock (_productionGate)
        {
            if (!_accepting)
                throw new InvalidOperationException("Instance catalog commit production has stopped.");

            var commit = publishStateAndCreateCommit();
            ArgumentNullException.ThrowIfNull(commit);
            if (!_channel.Writer.TryWrite(commit))
            {
                throw new InvalidOperationException(
                    "The unbounded instance catalog commit feed unexpectedly rejected an authoritative commit.");
            }
        }
    }

    internal void CompleteProduction()
    {
        lock (_productionGate)
        {
            if (!_accepting)
                return;
            _accepting = false;

            if (!_channel.Writer.TryComplete())
                throw new InvalidOperationException("The instance catalog commit feed could not be completed.");
        }
    }

    internal IAsyncEnumerable<InstanceCatalogCommit> ReadAllAsync()
    {
        if (Interlocked.Exchange(ref _readerClaimed, 1) != 0)
            throw new InvalidOperationException("The instance catalog commit feed supports exactly one reader.");

        return _channel.Reader.ReadAllAsync();
    }
}
