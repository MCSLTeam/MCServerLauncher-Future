using Serilog;

namespace MCServerLauncher.Daemon;

public class GracefulShutdown : IDisposable
{
    private readonly CancellationTokenSource _source = new();
    private readonly TaskCompletionSource _shutdownCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _shutdownGate = new();
    private readonly ConsoleCancelEventHandler _cancelKeyPressHandler;
    private readonly Task _shutdownTask;
    private Task? _shutdownDriver;
    private Task? _consoleShutdownObserver;
    private int _shutdownStarted;
    private bool _isDisposed;

    public GracefulShutdown()
    {
        _shutdownTask = _shutdownCompletion.Task;
        _cancelKeyPressHandler = OnCancelKeyPress;
        System.Console.CancelKeyPress += _cancelKeyPressHandler;
    }

    public CancellationToken CancellationToken => _source.Token;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public event Func<Task>? OnShutdown;

    public Task Shutdown()
    {
        EnsureShutdownStarted();
        return _shutdownTask;
    }

    private void EnsureShutdownStarted()
    {
        if (Interlocked.CompareExchange(ref _shutdownStarted, 1, 0) != 0)
            return;

        var driver = DriveShutdownAsync();
        Volatile.Write(ref _shutdownDriver, driver);
    }

    private async Task DriveShutdownAsync()
    {
        try
        {
            await ShutdownCoreAsync();
            _shutdownCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[GracefulShutdown] shutdown driver observed a failure");
            _shutdownCompletion.TrySetException(exception);
        }
    }

    private async Task ShutdownCoreAsync()
    {
        List<Exception>? failures = null;
        try
        {
            await _source.CancelAsync();
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[GracefulShutdown] cancellation callback failed");
            (failures ??= []).Add(exception);
        }

        Log.Information("[GracefulShutdown] shutting down...");
        if (OnShutdown is not null)
        {
            foreach (var callback in OnShutdown.GetInvocationList().Cast<Func<Task>>())
            {
                try
                {
                    await callback();
                }
                catch (Exception exception)
                {
                    Log.Error(exception, "[GracefulShutdown] shutdown callback failed");
                    (failures ??= []).Add(exception);
                }
            }
        }

        if (failures is not null)
            throw new AggregateException("One or more graceful shutdown steps failed.", failures);
    }

    public async Task WaitForShutdownAsync(int timeout = -1)
    {
        if (timeout < 0)
        {
            await _shutdownTask;
            return;
        }

        await _shutdownTask.WaitAsync(TimeSpan.FromMilliseconds(timeout));
    }

    private void Dispose(bool dispose)
    {
        if (_isDisposed) return;
        if (dispose)
        {
            System.Console.CancelKeyPress -= _cancelKeyPressHandler;
            _source.Dispose();
        }
        _isDisposed = true;
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs eventArgs)
    {
        eventArgs.Cancel = true;
        StartShutdownFromConsole();
    }

    internal void StartShutdownFromConsole()
    {
        EnsureShutdownStarted();
        lock (_shutdownGate)
            _consoleShutdownObserver ??= ObserveConsoleShutdownAsync();
    }

    internal Task ConsoleShutdownObservation
    {
        get
        {
            lock (_shutdownGate)
                return _consoleShutdownObserver ?? Task.CompletedTask;
        }
    }

    private async Task ObserveConsoleShutdownAsync()
    {
        try
        {
            await _shutdownTask;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[GracefulShutdown] console shutdown observer observed a failure");
        }
    }

    ~GracefulShutdown()
    {
        Dispose(false);
    }
}
