using System;

namespace MCServerLauncher.DaemonClient;

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

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~DisposableObject()
    {
        Dispose(false);
    }
}