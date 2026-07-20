using System.Diagnostics;
using System.Text;

namespace MCServerLauncher.Daemon.Management.Pty;

internal sealed class PipeConsoleHost : IInstanceConsoleHost
{
    private readonly Process _process;
    private StreamWriter? _stdin;

    public PipeConsoleHost(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        _process = process;
    }

    public bool IsPty => false;
    public Stream InputStream => _process.StandardInput.BaseStream;
    public Stream OutputStream => _process.StandardOutput.BaseStream;
    public int? ProcessId => null;
    public bool OwnsProcessLifecycle => false;

    private StreamWriter Stdin => _stdin ??= _process.StandardInput;

    public void Resize(ushort columns, ushort rows)
    {
    }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken) =>
        InputStream.WriteAsync(data, cancellationToken).AsTask();

    public async Task WriteLineAsync(string line, Encoding encoding, CancellationToken cancellationToken)
    {
        await Stdin.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        await Stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Write(ReadOnlyMemory<byte> data)
    {
        InputStream.Write(data.Span);
        InputStream.Flush();
    }

    public void WriteLine(string line, Encoding encoding)
    {
        Stdin.WriteLine(line);
        Stdin.Flush();
    }

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
