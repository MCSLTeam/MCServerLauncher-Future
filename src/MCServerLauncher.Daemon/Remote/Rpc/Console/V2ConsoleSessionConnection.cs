using System.Collections.Immutable;
using System.Runtime.InteropServices;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Transport;
using RustyOptions;

namespace MCServerLauncher.Daemon.Remote.Rpc.Console;

/// <summary>
/// Per-connection console session operations. Fan-outs ConsoleOutput binary frames on the owner queue.
/// </summary>
internal sealed class V2ConsoleSessionConnection : IProtocolConsoleSessionOperations, IV2ConnectionCleanup
{
    private readonly ConsoleSessionCoordinator _coordinator;
    private readonly V2ConnectionOwner _owner;
    private readonly object _gate = new();
    private readonly HashSet<Guid> _ownedSessions = [];
    private bool _closed;

    private V2ConsoleSessionConnection(ConsoleSessionCoordinator coordinator, V2ConnectionOwner owner)
    {
        _coordinator = coordinator;
        _owner = owner;
    }

    internal static Result<V2ConsoleSessionConnection, DaemonError> Attach(
        ConsoleSessionCoordinator coordinator,
        V2ConnectionOwner owner)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(owner);
        var connection = new V2ConsoleSessionConnection(coordinator, owner);
        return owner.TryRegisterCleanup(connection)
            ? Result.Ok<V2ConsoleSessionConnection, DaemonError>(connection)
            : Result.Err<V2ConsoleSessionConnection, DaemonError>(
                new ConflictDaemonError("connection.closed", "The connection is closed."));
    }

    public Task<Result<ConsoleSession, DaemonError>> OpenConsoleAsync(
        ConsoleOpenRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_closed)
            {
                return Task.FromResult(Result.Err<ConsoleSession, DaemonError>(
                    new ConflictDaemonError("connection.closed", "The connection is closed.")));
            }

            // Pre-allocate session id so late/early fan-out never sees Guid.Empty
            // (EnqueueOutputAsync drops empty session frames).
            var sessionId = Guid.CreateVersion7();
            var result = _coordinator.Open(
                request,
                sessionId,
                (chunk, offset, _) => EnqueueOutputAsync(sessionId, chunk, offset));
            if (result.IsOk(out var session) && session is not null)
                _ownedSessions.Add(session.SessionId);

            return Task.FromResult(result);
        }
    }

    public Task<Result<Unit, DaemonError>> ResizeConsoleAsync(
        ConsoleResizeRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsClosed() || !Owns(request.SessionId))
        {
            return Task.FromResult(Result.Err<Unit, DaemonError>(
                new NotFoundDaemonError("console.session_not_found", "The console session was not found.")));
        }

        return Task.FromResult(_coordinator.Resize(request));
    }

    public Task<Result<Unit, DaemonError>> CloseConsoleAsync(
        ConsoleSessionReference request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _ownedSessions.Remove(request.SessionId);
        }

        return Task.FromResult(_coordinator.Close(request));
    }

    public Task<Result<Unit, DaemonError>> ReceiveConsoleInputAsync(
        Guid sessionId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsClosed() || !Owns(sessionId))
        {
            return Task.FromResult(Result.Err<Unit, DaemonError>(
                new NotFoundDaemonError("console.session_not_found", "The console session was not found.")));
        }

        return Task.FromResult(_coordinator.Write(sessionId, data));
    }

    public ValueTask CleanupAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _closed = true;
            foreach (var sessionId in _ownedSessions.ToArray())
                _coordinator.Close(new ConsoleSessionReference(sessionId));
            _ownedSessions.Clear();
        }

        return ValueTask.CompletedTask;
    }

    private bool Owns(Guid sessionId)
    {
        lock (_gate)
            return _ownedSessions.Contains(sessionId);
    }

    private bool IsClosed()
    {
        lock (_gate)
            return _closed;
    }

    private Task EnqueueOutputAsync(Guid sessionId, ReadOnlyMemory<byte> chunk, long offset)
    {
        if (sessionId == Guid.Empty || chunk.Length == 0)
            return Task.CompletedTask;

        var payload = chunk.ToArray();
        var bytes = new byte[checked(BinaryFrameCodec.HeaderSize + payload.Length)];
        var header = new BinaryFrameHeader(
            BinaryFrameKind.ConsoleOutput,
            sessionId,
            offset,
            checked((uint)payload.Length));
        if (!BinaryFrameCodec.TryWrite(bytes, header, payload, out _))
            return Task.CompletedTask;

        var immutable = ImmutableCollectionsMarshal.AsImmutableArray(bytes);
        _ = _owner.TryEnqueue(V2OutboundMessage.Single(V2OutboundFrame.Binary(immutable)));
        return Task.CompletedTask;
    }
}
