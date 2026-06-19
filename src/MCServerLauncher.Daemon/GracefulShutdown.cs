using Serilog;

namespace MCServerLauncher.Daemon;

public class GracefulShutdown : IDisposable
{
    private readonly CancellationTokenSource _source = new();
    private readonly TaskCompletionSource _shutdownSignal =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _isDisposed;

    public GracefulShutdown()
    {
        System.Console.CancelKeyPress += async (_, e) =>
        {
            e.Cancel = true;
            try
            {
                await Shutdown();
            }
            catch (InvalidOperationException)
            {
            }
        };
    }

    public CancellationToken CancellationToken => _source.Token;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public event Func<Task>? OnShutdown;

    public async Task Shutdown()
    {
        if (_source.IsCancellationRequested) return;

        await _source.CancelAsync();
        _shutdownSignal.TrySetResult();
        Log.Information("[GracefulShutdown] shutting down...");
        if (OnShutdown is not null) await OnShutdown.Invoke();
    }

    public async Task WaitForShutdownAsync(int timeout = -1)
    {
        var waitTask = _shutdownSignal.Task;
        if (timeout < 0)
        {
            await waitTask;
            return;
        }

        await waitTask.WaitAsync(TimeSpan.FromMilliseconds(timeout));
    }

    private void Dispose(bool dispose)
    {
        if (_isDisposed) return;
        if (dispose) _source.Dispose();
        _isDisposed = true;
    }

    ~GracefulShutdown()
    {
        Dispose(false);
    }
}
