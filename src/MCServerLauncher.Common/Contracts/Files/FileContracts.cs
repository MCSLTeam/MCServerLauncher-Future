using System.Collections.Immutable;

namespace MCServerLauncher.Common.Contracts.Files;

public sealed record PathRequest(string Path);

public sealed record PathRenameRequest(string Path, string NewName);

public sealed record PathTransferRequest(string SourcePath, string DestinationPath);

public sealed record DeleteDirectoryRequest(string Path, bool Recursive);

public sealed record FileSystemMetadata(
    DateTimeOffset CreationTime,
    bool Hidden,
    DateTimeOffset LastAccessTime,
    DateTimeOffset LastWriteTime);

public sealed record FileMetadata(
    DateTimeOffset CreationTime,
    bool Hidden,
    DateTimeOffset LastAccessTime,
    DateTimeOffset LastWriteTime,
    bool ReadOnly,
    long Size);

public sealed record DirectoryMetadata(
    DateTimeOffset CreationTime,
    bool Hidden,
    DateTimeOffset LastAccessTime,
    DateTimeOffset LastWriteTime);

public sealed record FileEntry(string Name, FileMetadata Meta);

public sealed record DirectoryEntry(string Name, DirectoryMetadata Meta);

public sealed record FileDetails(FileMetadata Meta);

public sealed record DirectoryDetails(
    string? Parent,
    ImmutableArray<FileEntry> Files,
    ImmutableArray<DirectoryEntry> Directories);

public sealed record UploadOpenRequest(string Path, long Length, string Sha256);

public sealed record UploadSession(Guid SessionId, int MaxChunkSize, DateTimeOffset ExpiresAt);

public sealed record UploadChunkRequest(Guid SessionId, long Offset, ImmutableArray<byte> Data);

public sealed record DownloadOpenRequest(string Path);

public sealed record DownloadSession(
    Guid SessionId,
    long Length,
    string Sha256,
    int MaxChunkSize,
    DateTimeOffset ExpiresAt);

public sealed record DownloadChunkRequest(Guid SessionId, long Offset, int MaximumLength);

public sealed record DownloadChunk(long Offset, ImmutableArray<byte> Data, bool IsFinal);
