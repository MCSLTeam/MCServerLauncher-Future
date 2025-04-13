using MCServerLauncher.Daemon.Utils;

namespace MCServerLauncher.Daemon.Storage;

public class FileSessionInfo
{
    private readonly TimeSpan _timeout;
    private DateTime _lastAccessTime = DateTime.Now;
    private SpinLock _lock;

    protected FileSessionInfo(long size, FileStream file, string? sha1, string path, TimeSpan timeout)
    {
        Size = size;
        File = file;
        Sha1 = sha1?.ToLower();
        Path = path;
        Remain = new LongRemain(0, size);
        _timeout = timeout;
    }

    public long Size { get; }
    public FileStream File { get; }
    public string? Sha1 { get; }
    public string Path { get; }
    public LongRemain Remain { get; }
    public long RemainLength => Remain.GetRemain();

    public bool Timeout => IsTimeout();

    private bool IsTimeout()
    {
        var locked = false;
        _lock.Enter(ref locked);
        try
        {
            return DateTime.Now - _lastAccessTime > _timeout;
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

public class FileUploadInfo : FileSessionInfo
{
    public FileUploadInfo(string path, long size, string? sha1, FileStream file, TimeSpan timeout)
        : base(size, file, sha1, path, timeout)
    {
    }
}

public class FileDownloadInfo : FileSessionInfo
{
    public FileDownloadInfo(long size, string? sha1, FileStream file, string path, TimeSpan timeout) : base(size, file,
        sha1, path, timeout)
    {
    }
}