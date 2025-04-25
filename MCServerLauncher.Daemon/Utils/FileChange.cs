namespace MCServerLauncher.Daemon.Utils;

public class FileChange
{
    private FileInfo? _lastFileInfo;

    public FileChange(string path)
    {
        Path = path;
        Exists = File.Exists(path);
        _lastFileInfo = Exists ? new FileInfo(path) : null;
    }

    public bool Exists { get; private set; }
    public string Path { get; }

    public bool HasChanged()
    {
        var exists = File.Exists(Path);
        // 如果存在状态发生变化，则更新状态
        if (exists != Exists)
        {
            Exists = exists;
            _lastFileInfo = exists ? new FileInfo(Path) : null;
            return true;
        }

        // 要么同时存在，要么同时不存在:
        // 1. 如果曾经和现在都不存在, 说明没变
        if (!exists && _lastFileInfo is null) return false;

        // 2. 如果曾今和现在都存在, 比较最后修改时间
        return exists && _lastFileInfo!.LastWriteTimeUtc != new FileInfo(Path).LastWriteTimeUtc;
    }
}