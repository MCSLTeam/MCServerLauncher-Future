namespace MCServerLauncher.Daemon.Utils;

public abstract class DisposableObject : IDisposable
{
    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        Dispose(true);
    }

    protected abstract void ProtectedDispose();

    private void Dispose(bool disposing)
    {
        if (!IsDisposed)
            if (disposing)
                ProtectedDispose();

        IsDisposed = true;
        GC.SuppressFinalize(this);
    }

    ~DisposableObject()
    {
        Dispose(false);
    }
}