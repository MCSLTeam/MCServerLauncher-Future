using System.Collections.Concurrent;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

namespace MCServerLauncher.Daemon.Management;

/// <summary>
/// Tracks open console sessions bound to a running instance process.
/// Output fan-out is performed by the attach handler supplied at open time.
/// </summary>
internal sealed class ConsoleSessionCoordinator(IInstanceManager instanceManager)
{
    public const int DefaultMaxChunkSize = 64 * 1024;
    public static readonly TimeSpan DefaultLease = TimeSpan.FromHours(6);

    private readonly ConcurrentDictionary<Guid, ConsoleLease> _leases = new();

    public Result<ConsoleSession, DaemonError> Open(
        ConsoleOpenRequest request,
        Func<ReadOnlyMemory<byte>, long, CancellationToken, Task> outputHandler,
        TimeProvider? timeProvider = null) =>
        Open(request, Guid.CreateVersion7(), outputHandler, timeProvider);

    public Result<ConsoleSession, DaemonError> Open(
        ConsoleOpenRequest request,
        Guid sessionId,
        Func<ReadOnlyMemory<byte>, long, CancellationToken, Task> outputHandler,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(outputHandler);
        if (sessionId == Guid.Empty)
        {
            return Result.Err<ConsoleSession, DaemonError>(
                new ValidationDaemonError("console.session_id_invalid", "Console session id cannot be empty."));
        }

        if (request.Columns == 0 || request.Rows == 0)
        {
            return Result.Err<ConsoleSession, DaemonError>(
                new ValidationDaemonError("console.size_invalid", "Console columns and rows must be greater than zero."));
        }

        if (!instanceManager.Instances.ContainsKey(request.InstanceId))
        {
            return Result.Err<ConsoleSession, DaemonError>(
                new NotFoundDaemonError("instance.not_found", $"Instance '{request.InstanceId}' was not found."));
        }

        if (!instanceManager.RunningInstances.ContainsKey(request.InstanceId))
        {
            return Result.Err<ConsoleSession, DaemonError>(
                new ConflictDaemonError("instance.not_running", "The instance is not running."));
        }

        // Apply initial size before attaching so the first paint matches.
        if (!instanceManager.TryResizeConsole(request.InstanceId, request.Columns, request.Rows))
        {
            return Result.Err<ConsoleSession, DaemonError>(
                new ConflictDaemonError("instance.not_running", "The instance is not running."));
        }

        var subscriberId = instanceManager.AttachConsole(request.InstanceId, outputHandler);
        if (subscriberId is null)
        {
            return Result.Err<ConsoleSession, DaemonError>(
                new ConflictDaemonError("instance.not_running", "The instance is not running."));
        }

        var clock = timeProvider ?? TimeProvider.System;
        var expires = clock.GetUtcNow().Add(DefaultLease);
        var lease = new ConsoleLease(
            sessionId,
            request.InstanceId,
            subscriberId.Value,
            expires,
            DefaultMaxChunkSize,
            request.Columns,
            request.Rows);
        if (!_leases.TryAdd(sessionId, lease))
        {
            instanceManager.DetachConsole(request.InstanceId, subscriberId.Value);
            return Result.Err<ConsoleSession, DaemonError>(
                new InternalDaemonError("console.session_create_failed", "The console session could not be created."));
        }

        return Result.Ok<ConsoleSession, DaemonError>(new ConsoleSession(
            sessionId,
            request.InstanceId,
            expires,
            DefaultMaxChunkSize,
            request.Columns,
            request.Rows));
    }

    public Result<Unit, DaemonError> Resize(ConsoleResizeRequest request)
    {
        if (!_leases.TryGetValue(request.SessionId, out var lease))
        {
            return Result.Err<Unit, DaemonError>(
                new NotFoundDaemonError("console.session_not_found", "The console session was not found."));
        }

        if (request.Columns == 0 || request.Rows == 0)
        {
            return Result.Err<Unit, DaemonError>(
                new ValidationDaemonError("console.size_invalid", "Console columns and rows must be greater than zero."));
        }

        if (!instanceManager.TryResizeConsole(lease.InstanceId, request.Columns, request.Rows))
        {
            return Result.Err<Unit, DaemonError>(
                new ConflictDaemonError("instance.not_running", "The instance is not running."));
        }

        _leases[request.SessionId] = lease with { Columns = request.Columns, Rows = request.Rows };
        return Result.Ok<Unit, DaemonError>(Unit.Default);
    }

    public Result<Unit, DaemonError> Write(Guid sessionId, ReadOnlyMemory<byte> data)
    {
        if (!_leases.TryGetValue(sessionId, out var lease))
        {
            return Result.Err<Unit, DaemonError>(
                new NotFoundDaemonError("console.session_not_found", "The console session was not found."));
        }

        if (data.Length > lease.MaxChunkSize)
        {
            return Result.Err<Unit, DaemonError>(
                new ValidationDaemonError("console.chunk_too_large", "The console input chunk exceeds the session maximum."));
        }

        if (!instanceManager.TryWriteConsole(lease.InstanceId, data))
        {
            return Result.Err<Unit, DaemonError>(
                new ConflictDaemonError("instance.not_running", "The instance is not running."));
        }

        return Result.Ok<Unit, DaemonError>(Unit.Default);
    }

    public Result<Unit, DaemonError> Close(ConsoleSessionReference request)
    {
        if (!_leases.TryRemove(request.SessionId, out var lease))
            return Result.Ok<Unit, DaemonError>(Unit.Default);

        instanceManager.DetachConsole(lease.InstanceId, lease.SubscriberId);
        return Result.Ok<Unit, DaemonError>(Unit.Default);
    }

    public void CloseAllForInstance(Guid instanceId)
    {
        foreach (var pair in _leases)
        {
            if (pair.Value.InstanceId != instanceId)
                continue;
            if (_leases.TryRemove(pair.Key, out var lease))
                instanceManager.DetachConsole(lease.InstanceId, lease.SubscriberId);
        }
    }

    public bool TryGet(Guid sessionId, out ConsoleLease lease) => _leases.TryGetValue(sessionId, out lease!);

    internal sealed record ConsoleLease(
        Guid SessionId,
        Guid InstanceId,
        Guid SubscriberId,
        DateTimeOffset ExpiresAt,
        int MaxChunkSize,
        ushort Columns,
        ushort Rows);
}
