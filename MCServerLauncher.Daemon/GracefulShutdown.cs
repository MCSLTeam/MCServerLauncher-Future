using Serilog;

namespace MCServerLauncher.Daemon;

public class GracefulShutdown : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(0);
    private readonly CancellationTokenSource _source = new();
    private bool _isDisposed;

    public GracefulShutdown()
    {
        System.Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            try
            {
                Shutdown();
            }
            catch (InvalidOperationException)
            {
            }
        };
    }

    public CancellationToken CancellationToken => _source.Token;
    public bool ShutdownRequested => _source.IsCancellationRequested;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public event Action? OnShutdown;

    public void Shutdown()
    {
        if (_source.IsCancellationRequested)
            throw new InvalidOperationException("Already shutdown");

        _source.Cancel();
        Log.Information("[GracefulShutdown] shutting down...");
        OnShutdown?.Invoke();
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