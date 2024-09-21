namespace MCServerLauncher.Daemon.Storage;

public class FileLoadInfo
{
    protected FileLoadInfo(long size, FileStream file, string? sha1, string path)
    {
        Size = size;
        File = file;
        Sha1 = sha1?.ToLower();
        Path = path;
        Remain = new LongRemain(0, size);
    }

    public long Size { get; }
    public FileStream File { get; }
    public string? Sha1 { get; }
    public string Path { get; }
    public LongRemain Remain { get; }
}

public class FileUploadInfo : FileLoadInfo
{
    public FileUploadInfo(string path, long size, long chunkSize, string? sha1, FileStream file)
        : base(size, file, sha1, path)
    {
        RemainLength = size;
        ChunkSize = chunkSize;
    }

    public long RemainLength { get; set; }
    public long ChunkSize { get; }
}

public class FileDownloadInfo : FileLoadInfo
{
    public FileDownloadInfo(long size, string? sha1, FileStream file, string path) : base(size, file, sha1, path)
    {
    }
}