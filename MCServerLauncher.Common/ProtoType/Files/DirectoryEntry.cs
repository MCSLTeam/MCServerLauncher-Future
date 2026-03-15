using MCServerLauncher.Common.Helpers;

namespace MCServerLauncher.Common.ProtoType.Files;

public record FileSystemMetadata
{
    public long CreationTime;
    public bool Hidden;
    public long LastAccessTime;

    public long LastWriteTime;
    // public string? LinkTarget;

    public FileSystemMetadata()
    {
    }

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

    public FileMetadata()
    {
    }

    public FileMetadata(FileInfo info) : base(info)
    {
        Size = info.Length;
        ReadOnly = info.IsReadOnly;
    }
}

public record DirectoryMetadata : FileSystemMetadata
{
    public DirectoryMetadata()
    {
    }

    public DirectoryMetadata(DirectoryInfo info) : base(info)
    {
    }
}

public record DirectoryEntry
{
    public DirectoryInformation[] Directories;
    public FileInformation[] Files;
    public string? Parent;

    public DirectoryEntry()
    {
        Directories = Array.Empty<DirectoryInformation>();
        Files = Array.Empty<FileInformation>();
    }

    public DirectoryEntry(string path, string root) : this(new DirectoryInfo(path), root)
    {
    }

    private DirectoryEntry(DirectoryInfo info, string root)
    {
        Files = info.GetFiles().Select(x => new FileInformation(x)).ToArray();
        Directories = info.GetDirectories().Select(x => new DirectoryInformation(x)).ToArray();
        Parent = PathHelper.GetRelativePath(root, info.FullName);
    }

    public record FileInformation
    {
        public FileMetadata Meta;
        public string Name;

        public FileInformation()
        {
            Meta = new FileMetadata();
            Name = string.Empty;
        }

        public FileInformation(FileInfo info)
        {
            Name = info.Name;
            Meta = new FileMetadata(info);
        }
    }

    public record DirectoryInformation
    {
        public DirectoryMetadata Meta;
        public string Name;

        public DirectoryInformation()
        {
            Meta = new DirectoryMetadata();
            Name = string.Empty;
        }

        public DirectoryInformation(DirectoryInfo info)
        {
            Name = info.Name;
            Meta = new DirectoryMetadata(info);
        }
    }
}