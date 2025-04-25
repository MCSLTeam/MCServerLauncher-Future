namespace MCServerLauncher.Daemon.Utils;

public abstract class DisposableObject : IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
    }

    protected abstract void ProtectedDispose();

    private void Dispose(bool disposing)
    {
        if (!_disposed)
            if (disposing)
                ProtectedDispose();

        // TODO: free unmanaged resources ()
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~DisposableObject()
    {
        Dispose(false);
    }
}