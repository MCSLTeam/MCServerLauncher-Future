using System;
using System.Threading.Tasks;

namespace MCServerLauncher.DaemonClient;

public sealed class DaemonEventSubscription : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly IAsyncDisposable _handle;
    private Task? _disposeTask;

    internal DaemonEventSubscription(IAsyncDisposable handle) =>
        _handle = handle ?? throw new ArgumentNullException(nameof(handle));

    public ValueTask DisposeAsync()
    {
        lock (_gate)
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        await _handle.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
