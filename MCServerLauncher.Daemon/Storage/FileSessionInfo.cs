using MCServerLauncher.Daemon.Utils;

namespace MCServerLauncher.Daemon.Storage;

public class FileSessionInfo(long size, FileStream file, string? sha1, string path, TimeSpan timeout)
{
    private DateTime _lastAccessTime = DateTime.Now;
    private SpinLock _lock;

    public long Size { get; } = size;
    public FileStream File { get; } = file;
    public string? Sha1 { get; } = sha1?.ToLower();
    public string Path { get; } = path;
    public LongRemain Remain { get; } = new LongRemain(0, size);
    public long RemainLength => Remain.GetRemain();

    public bool Timeout => IsTimeout();

    private bool IsTimeout()
    {
        var locked = false;
        _lock.Enter(ref locked);
        try
        {
            return DateTime.Now - _lastAccessTime > timeout;
        }
        finally
        {
            if (locked) _lock.Exit();
        }
    }

    public void Touch()
    {
        if (IsTimeout()) return;

        var locked = false;
        _lock.Enter(ref locked);
        try
        {
            _lastAccessTime = DateTime.Now;
        }
        finally
        {
            if (locked) _lock.Exit();
        }
    }

    public void Close()
    {
        File.Close();
    }
}

public class FileUploadInfo(string path, long size, string? sha1, FileStream file, TimeSpan timeout)
    : FileSessionInfo(size, file, sha1, path, timeout);

public class FileDownloadInfo(long size, string? sha1, FileStream file, string path, TimeSpan timeout)
    : FileSessionInfo(size, file, sha1, path, timeout);