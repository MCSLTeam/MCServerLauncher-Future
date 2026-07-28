using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Channels;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.DaemonClient;
using RustyOptions;

namespace MCServerLauncher.DaemonClient.Connection.V2;

internal sealed class V2ClientConsoleCoordinator(Action<string> protocolFault)
{
    private const int OutputCapacity = 4096;
    private const int MaximumEarlySessions = 16;
    private const int MaximumEarlyChunksPerSession = 512;
    private const int MaximumEarlyBytes = 2 * 1024 * 1024;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, ConsoleState> _sessions = [];
    private readonly Dictionary<Guid, Queue<DaemonConsoleOutput>> _early = [];
    private int _earlyBytes;
    private bool _closed;

    internal Result<ChannelReader<DaemonConsoleOutput>, DaemonError> Register(
        Guid sessionId,
        int maximumChunkSize)
    {
        if (sessionId == Guid.Empty || maximumChunkSize <= 0)
        {
            return Result.Err<ChannelReader<DaemonConsoleOutput>, DaemonError>(
                new ValidationDaemonError("console.session_invalid", "The console session is invalid."));
        }

        lock (_gate)
        {
            if (_closed)
            {
                return Result.Err<ChannelReader<DaemonConsoleOutput>, DaemonError>(
                    new TransportDaemonError("connection.closed", "The V2 connection is closed."));
            }

            if (_sessions.ContainsKey(sessionId))
            {
                return Result.Err<ChannelReader<DaemonConsoleOutput>, DaemonError>(
                    new ConflictDaemonError("console.session_duplicate", "The console session is already registered."));
            }

            var channel = Channel.CreateBounded<DaemonConsoleOutput>(new BoundedChannelOptions(OutputCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
            var state = new ConsoleState(maximumChunkSize, channel);
            _sessions.Add(sessionId, state);

            if (_early.Remove(sessionId, out var queued))
            {
                foreach (var chunk in queued)
                {
                    _earlyBytes -= chunk.Data.Length;
                    if (!TryRouteLocked(sessionId, state, chunk))
                        break;
                }
            }

            return Result.Ok<ChannelReader<DaemonConsoleOutput>, DaemonError>(channel.Reader);
        }
    }

    internal bool Contains(Guid sessionId)
    {
        lock (_gate)
            return !_closed && _sessions.ContainsKey(sessionId);
    }

    internal Result<long, DaemonError> ReserveInput(Guid sessionId, int length)
    {
        lock (_gate)
        {
            if (_closed)
            {
                return Result.Err<long, DaemonError>(
                    new TransportDaemonError("connection.closed", "The V2 connection is closed."));
            }

            if (!_sessions.TryGetValue(sessionId, out var state))
            {
                return Result.Err<long, DaemonError>(
                    new NotFoundDaemonError("console.session_not_found", "The console session was not found."));
            }

            if (length < 0 || length > state.MaximumChunkSize)
            {
                return Result.Err<long, DaemonError>(
                    new ValidationDaemonError("console.chunk_too_large", "The console input chunk exceeds the session maximum."));
            }

            var offset = state.InputOffset;
            state.InputOffset = checked(offset + length);
            return Result.Ok<long, DaemonError>(offset);
        }
    }

    internal void Route(BinaryFrameHeader header, ReadOnlySpan<byte> payload)
    {
        lock (_gate)
        {
            if (_closed)
                return;

            var chunk = new DaemonConsoleOutput(header.Offset, payload.ToArray());
            if (_sessions.TryGetValue(header.SessionId, out var state))
            {
                TryRouteLocked(header.SessionId, state, chunk);
                return;
            }

            BufferEarlyLocked(header.SessionId, chunk);
        }
    }

    internal void Unregister(Guid sessionId)
    {
        lock (_gate)
        {
            if (_sessions.Remove(sessionId, out var state))
                state.Channel.Writer.TryComplete();
            if (_early.Remove(sessionId, out var queued))
                _earlyBytes -= queued.Sum(static chunk => chunk.Data.Length);
        }
    }

    internal void Close(DaemonError error)
    {
        lock (_gate)
        {
            if (_closed)
                return;
            _closed = true;
            foreach (var state in _sessions.Values)
                state.Channel.Writer.TryComplete(new ConsoleSessionClosedException(error));
            _sessions.Clear();
            _early.Clear();
            _earlyBytes = 0;
        }
    }

    private bool TryRouteLocked(Guid sessionId, ConsoleState state, DaemonConsoleOutput chunk)
    {
        if (chunk.Data.Length > state.MaximumChunkSize)
        {
            FailSessionLocked(sessionId, state, "The console output chunk exceeds the session maximum.");
            return false;
        }

        if (state.ExpectedOutputOffset is { } expected && chunk.Offset != expected)
        {
            FailSessionLocked(sessionId, state, "The console output stream offset is not contiguous.");
            return false;
        }

        if (!state.Channel.Writer.TryWrite(chunk))
        {
            FailSessionLocked(sessionId, state, "The console output consumer is too slow.");
            return false;
        }

        state.ExpectedOutputOffset = checked(chunk.Offset + chunk.Data.Length);
        return true;
    }

    private void BufferEarlyLocked(Guid sessionId, DaemonConsoleOutput chunk)
    {
        if (chunk.Data.Length > MaximumEarlyBytes)
        {
            protocolFault("The early console output chunk exceeds the bounded buffer.");
            return;
        }

        if (!_early.TryGetValue(sessionId, out var queue))
        {
            if (_early.Count >= MaximumEarlySessions)
            {
                protocolFault("Too many unclaimed console output sessions were received.");
                return;
            }
            queue = new Queue<DaemonConsoleOutput>();
            _early.Add(sessionId, queue);
        }

        if (queue.Count >= MaximumEarlyChunksPerSession ||
            _earlyBytes + chunk.Data.Length > MaximumEarlyBytes)
        {
            protocolFault("The early console output buffer is full.");
            return;
        }

        queue.Enqueue(chunk);
        _earlyBytes += chunk.Data.Length;
    }

    private void FailSessionLocked(Guid sessionId, ConsoleState state, string message)
    {
        _sessions.Remove(sessionId);
        state.Channel.Writer.TryComplete(new InvalidDataException(message));
        protocolFault(message);
    }

    private sealed class ConsoleState(
        int maximumChunkSize,
        Channel<DaemonConsoleOutput> channel)
    {
        internal int MaximumChunkSize { get; } = maximumChunkSize;
        internal Channel<DaemonConsoleOutput> Channel { get; } = channel;
        internal long? ExpectedOutputOffset { get; set; }
        internal long InputOffset { get; set; }
    }
}

internal sealed class ConsoleSessionClosedException(DaemonError error)
    : IOException(error.Message)
{
    internal DaemonError Error { get; } = error;
}
