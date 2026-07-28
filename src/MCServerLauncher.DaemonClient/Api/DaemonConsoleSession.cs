using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.DaemonClient.Connection.V2;
using RustyOptions;

namespace MCServerLauncher.DaemonClient;

public readonly record struct DaemonConsoleOutput(long Offset, ReadOnlyMemory<byte> Data);

public sealed class DaemonConsoleSession : IAsyncDisposable
{
    private readonly V2ClientConnectionCore _core;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;

    internal DaemonConsoleSession(
        ConsoleSession session,
        ChannelReader<DaemonConsoleOutput> output,
        V2ClientConnectionCore core)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Output = output ?? throw new ArgumentNullException(nameof(output));
        _core = core ?? throw new ArgumentNullException(nameof(core));
    }

    public ConsoleSession Session { get; }

    public ChannelReader<DaemonConsoleOutput> Output { get; }

    public Task<Result<Unit, DaemonError>> WriteAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default) =>
        _core.SendConsoleInputAsync(Session.SessionId, data, cancellationToken);

    public Task<Result<Unit, DaemonError>> ResizeAsync(
        ushort columns,
        ushort rows,
        CancellationToken cancellationToken = default) =>
        _core.InvokeUnitAsync(
            BuiltInProtocolDefinitions.ResizeConsole,
            new ConsoleResizeRequest(Session.SessionId, columns, rows),
            cancellationToken);

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        _core.UnregisterConsoleSession(Session.SessionId);
        try
        {
            await _core.InvokeUnitAsync(
                BuiltInProtocolDefinitions.CloseConsole,
                new ConsoleSessionReference(Session.SessionId),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
        GC.SuppressFinalize(this);
    }
}
