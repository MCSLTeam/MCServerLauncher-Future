using MCServerLauncher.Daemon.Helpers;

namespace MCServerLauncher.Daemon.FileManagement;

public class FileUploadInfo
{
    public long Size { get; }
    public LongRemain Remain { get; private set; }
    public string FileName { get; private set; }
    public long RemainLength { get; set; }
    public long ChunkSize { get; }
    public string Sha1 { get; }
    public FileStream File { get; }

    public FileUploadInfo(string fileName, long size, long chunkSize, string sha1, FileStream file)
    {
        Size = size;
        FileName = fileName;
        Remain = new LongRemain(0, size);
        RemainLength = size;
        ChunkSize = chunkSize;
        Sha1 = sha1.ToLower();
        File = file;
    }
}