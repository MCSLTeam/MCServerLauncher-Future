using Serilog;

namespace MCServerLauncher.Daemon;

public class GracefulShutdown : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(0);
    private readonly CancellationTokenSource _source = new();
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
        if (_source.IsCancellationRequested)
            throw new InvalidOperationException("Already shutdown");

        _source.Cancel();
        Log.Information("[GracefulShutdown] shutting down...");
        if (OnShutdown is not null) await OnShutdown.Invoke();
        _semaphore.Release();
    }

    public async Task WaitForShutdownAsync(int timeout = -1)
    {
        await _semaphore.WaitAsync(timeout);
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