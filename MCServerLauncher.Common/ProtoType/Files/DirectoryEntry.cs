using MCServerLauncher.Common.Helpers;

namespace MCServerLauncher.Common.ProtoType.Files;

public enum FileType
{
    Directory,
    File
}

public record FileSystemMetadata
{
    public long CreationTime;
    public bool Hidden;
    public long LastAccessTime;

    public long LastWriteTime;
    // public string? LinkTarget;

    protected FileSystemMetadata(FileSystemInfo info)
    {
        CreationTime = info.CreationTime.ToUnixTimeSeconds();
        LastAccessTime = info.LastAccessTime.ToUnixTimeSeconds();
        LastWriteTime = info.LastWriteTime.ToUnixTimeSeconds();
        Hidden = info.Attributes.HasFlag(FileAttributes.Hidden);
    }
}

public record FileMetadata : FileSystemMetadata
{
    public bool ReadOnly;
    public long Size;


    public FileMetadata(FileInfo info) : base(info)
    {
        Size = info.Length;
        ReadOnly = info.IsReadOnly;
    }
}

public record DirectoryMetadata : FileSystemMetadata
{
    public DirectoryMetadata(DirectoryInfo info) : base(info)
    {
    }
}

public record FileData
{
    public string Name;
    public FileType Type;
    public FileSystemMetadata Meta;

    public static FileData Of(FileSystemInfo info)
    {
        return new FileData
        {
            Name = info.Name,
            Type = info is DirectoryInfo ? FileType.Directory : FileType.File,
            Meta = info is FileInfo f ? new FileMetadata(f) : (FileSystemMetadata)new DirectoryMetadata((DirectoryInfo)info)
        };
    }
}

public record DirectoryEntry
{
    public FileData[] Files;
    public string Name;
    public string? Parent;

    public DirectoryEntry(string path, string root) : this(new DirectoryInfo(path), root)
    {
    }

    private DirectoryEntry(DirectoryInfo info, string root)
    {
        // Files = info.GetFiles().Select(x => new FileInformation(x)).ToArray();
        // Directories = info.GetDirectories().Select(x => new DirectoryInformation(x)).ToArray();
        Name = info.Name;
        Files = info.GetDirectories()
            .Select(FileData.Of)
            .Concat(
                info.GetFiles().Select(FileData.Of)
            )
            .ToArray();
        Parent = PathHelper.GetRelativePath(root, info.FullName);
    }
    
}

