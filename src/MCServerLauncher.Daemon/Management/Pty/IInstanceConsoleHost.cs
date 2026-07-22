using System.Text;

namespace MCServerLauncher.Daemon.Management.Pty;

internal interface IInstanceConsoleHost : IAsyncDisposable, IDisposable
{
    bool IsPty { get; }

    Stream InputStream { get; }

    Stream OutputStream { get; }

    int? ProcessId { get; }

    bool OwnsProcessLifecycle { get; }

    void NotifyProcessExited();

    void Resize(ushort columns, ushort rows);

    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken);

    Task WriteLineAsync(string line, Encoding encoding, CancellationToken cancellationToken);

    void Write(ReadOnlyMemory<byte> data);

    void WriteLine(string line, Encoding encoding);
}
