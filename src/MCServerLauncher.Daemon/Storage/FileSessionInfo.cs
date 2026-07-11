namespace MCServerLauncher.Daemon.Storage;

internal abstract class FileSessionInfo(
    string path,
    FileStream stream,
    DateTimeOffset expiresAt) : IAsyncDisposable
{
    public SemaphoreSlim Gate { get; } = new(1, 1);

    public string Path { get; } = path;

    public FileStream Stream { get; } = stream;

    public DateTimeOffset ExpiresAt { get; } = expiresAt;

    public bool IsClosed { get; set; }

    public virtual async ValueTask DisposeAsync()
    {
        await Stream.DisposeAsync();
        Gate.Dispose();
    }
}

internal sealed class FileUploadInfo(
    string path,
    string stagingPath,
    long size,
    string? sha256,
    string? legacySha1,
    FileStream stream,
    DateTimeOffset expiresAt)
    : FileSessionInfo(path, stream, expiresAt)
{
    public string StagingPath { get; } = stagingPath;

    public long Size { get; } = size;

    public string? Sha256 { get; } = sha256;

    public string? LegacySha1 { get; } = legacySha1;

    public long NextExpectedOffset { get; set; }

    public long Received => NextExpectedOffset;

    public bool IsComplete => NextExpectedOffset == Size;
}

internal sealed class FileDownloadInfo(
    Guid sessionId,
    string path,
    long size,
    string sha256,
    string? legacySha1,
    FileStream stream,
    DateTimeOffset expiresAt)
    : FileSessionInfo(path, stream, expiresAt)
{
    public Guid SessionId { get; } = sessionId;

    public long Size { get; } = size;

    public string Sha256 { get; } = sha256;

    public string? LegacySha1 { get; } = legacySha1;
}
