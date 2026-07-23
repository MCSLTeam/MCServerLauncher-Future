using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RustyOptions;

namespace MCServerLauncher.Daemon.ApplicationCore.Operations;

/// <summary>
/// Daemon-owned long-running operation coordinator.
/// Progress is coalesced in memory; index persistence is rate-limited for progress and
/// immediate for admission, stage, cancellation, and terminal transitions. The coordinator
/// supervises execution and owns exactly one DI scope per accepted operation.
/// </summary>
internal sealed class OperationCoordinator : IOperationApplication, IAsyncDisposable
{
    private static readonly TimeSpan ProgressPersistInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan TerminalCommitRetryDelay = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<Guid, OperationRuntime> _operations = new();
    private readonly object _lifecycleGate = new();
    private readonly object _persistGate = new();
    private readonly string _root;
    private readonly string _indexPath;
    private readonly TimeProvider _timeProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ServiceProvider? _ownedScopeProvider;
    private readonly OwnedTaskSupervisor _supervisor;
    private readonly ILogger<OperationCoordinator> _logger;
    private readonly TimeSpan _retention;
    private readonly long _maximumBytes;
    private readonly Action<byte[]> _indexWriter;
    private DateTimeOffset _lastProgressPersist = DateTimeOffset.MinValue;
    private long _persistenceWriteCount;
    private int _disposed;

    public OperationCoordinator(
        TimeProvider? timeProvider = null,
        string? rootDirectory = null,
        IServiceScopeFactory? scopeFactory = null,
        ILogger<OperationCoordinator>? logger = null,
        DaemonOperationsConfig? config = null)
        : this(timeProvider, rootDirectory, scopeFactory, logger, config, indexWriter: null)
    {
    }

    internal OperationCoordinator(
        Action<byte[]> indexWriter,
        TimeProvider? timeProvider = null,
        string? rootDirectory = null,
        IServiceScopeFactory? scopeFactory = null,
        ILogger<OperationCoordinator>? logger = null,
        DaemonOperationsConfig? config = null)
        : this(timeProvider, rootDirectory, scopeFactory, logger, config, indexWriter)
    {
    }

    private OperationCoordinator(
        TimeProvider? timeProvider,
        string? rootDirectory,
        IServiceScopeFactory? scopeFactory,
        ILogger<OperationCoordinator>? logger,
        DaemonOperationsConfig? config,
        Action<byte[]>? indexWriter)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<OperationCoordinator>.Instance;
        config ??= new DaemonOperationsConfig();
        config.Validate();
        _retention = TimeSpan.FromDays(config.RetentionDays);
        _maximumBytes = config.MaximumBytes;
        _root = Path.GetFullPath(rootDirectory ?? Path.Combine(FileManager.Root, "operations"));
        Directory.CreateDirectory(_root);
        _indexPath = Path.Combine(_root, "index.json");
        _indexWriter = indexWriter ?? WriteIndex;
        LoadIndex();

        if (scopeFactory is null)
        {
            _ownedScopeProvider = new ServiceCollection().BuildServiceProvider();
            _scopeFactory = _ownedScopeProvider.GetRequiredService<IServiceScopeFactory>();
        }
        else
        {
            _scopeFactory = scopeFactory;
        }

        _supervisor = new OwnedTaskSupervisor(nameof(OperationCoordinator), _logger);
    }

    internal long PersistenceWriteCount => Interlocked.Read(ref _persistenceWriteCount);

    internal bool HasAcceptedOperation(string kind, string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        return _operations.Values.Any(runtime =>
        {
            var snapshot = runtime.Snapshot;
            return string.Equals(snapshot.Kind, kind, StringComparison.Ordinal) &&
                string.Equals(snapshot.Target, target, StringComparison.Ordinal);
        });
    }

    public Task<Result<OperationListResult, DaemonError>> ListOperationsAsync(
        OperationListQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // OwnerPrincipal is the authenticated caller subject.
        // Trusted admin marker "*" (main token / full grant) sees all operations.
        var owner = request.OwnerPrincipal;
        if (string.IsNullOrWhiteSpace(owner))
        {
            return Task.FromResult(Result.Ok<OperationListResult, DaemonError>(
                new OperationListResult(ImmutableArray<OperationSnapshot>.Empty)));
        }

        var items = _operations.Values
            .Select(static runtime => runtime.Snapshot)
            .Where(snapshot =>
                string.Equals(owner, "*", StringComparison.Ordinal) ||
                string.Equals(snapshot.OwnerPrincipal, owner, StringComparison.Ordinal))
            .OrderByDescending(static snapshot => snapshot.CreatedAt)
            .ToImmutableArray();
        return Task.FromResult(Result.Ok<OperationListResult, DaemonError>(new OperationListResult(items)));
    }

    public Task<Result<OperationSnapshot, DaemonError>> GetOperationAsync(
        OperationReference request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_operations.TryGetValue(request.OperationId, out var runtime))
        {
            return Task.FromResult(Result.Err<OperationSnapshot, DaemonError>(
                new NotFoundDaemonError("operation.not_found", "The operation was not found.")));
        }

        if (!IsVisibleTo(request.OwnerPrincipal, runtime.Snapshot.OwnerPrincipal))
        {
            return Task.FromResult(Result.Err<OperationSnapshot, DaemonError>(
                new PermissionDaemonError("operation.forbidden", "The caller cannot view this operation.")));
        }

        return Task.FromResult(Result.Ok<OperationSnapshot, DaemonError>(runtime.Snapshot));
    }

    public Task<Result<OperationCancelResult, DaemonError>> CancelOperationAsync(
        OperationCancelRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_operations.TryGetValue(request.OperationId, out var runtime))
        {
            return Task.FromResult(Result.Err<OperationCancelResult, DaemonError>(
                new NotFoundDaemonError("operation.not_found", "The operation was not found.")));
        }

        var snapshot = runtime.Snapshot;
        if (!IsVisibleTo(request.OwnerPrincipal, snapshot.OwnerPrincipal))
        {
            return Task.FromResult(Result.Err<OperationCancelResult, DaemonError>(
                new PermissionDaemonError("operation.forbidden", "The caller cannot cancel this operation.")));
        }

        try
        {
            lock (_persistGate)
            {
                if (!runtime.TryCreateCancellationSnapshot(_timeProvider.GetUtcNow(), out var candidate))
                {
                    return Task.FromResult(Result.Ok<OperationCancelResult, DaemonError>(
                        new OperationCancelResult(request.OperationId, CancelRequested: false)));
                }

                var snapshots = CaptureSnapshots(runtime, candidate);
                var persisted = PersistSnapshotsUnderLock(snapshots);
                if (!persisted.RetainedOperationIds.Contains(candidate.OperationId))
                    throw new InvalidOperationException("A non-terminal cancellation candidate was pruned during persistence.");

                runtime.PublishCancellation(candidate);
                PrunePublishedOperations(persisted.RetainedOperationIds);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Failed to persist cancellation for operation {OperationId}.", request.OperationId);
            return Task.FromResult(Result.Err<OperationCancelResult, DaemonError>(
                new InternalDaemonError("operation.persist_failed", "The cancellation request could not be persisted.")));
        }

        runtime.SignalCancellation(_logger);

        return Task.FromResult(Result.Ok<OperationCancelResult, DaemonError>(
            new OperationCancelResult(request.OperationId, CancelRequested: true)));
    }

    /// <summary>
    /// Persists and accepts a daemon operation, then schedules its execution independently of
    /// the request lifetime. The returned snapshot is always the accepted queued snapshot. A
    /// terminal commit must atomically persist linked domain state before returning and must be
    /// safe to retry. A failed commit publishes Interrupted and remains supervised for in-process
    /// reconciliation until it succeeds or daemon shutdown begins.
    /// </summary>
    internal Task<Result<OperationSnapshot, DaemonError>> StartAsync(
        string kind,
        string? target,
        string ownerPrincipal,
        Func<IServiceProvider, IOperationContext, CancellationToken, Task<Result<string, DaemonError>>> executor,
        CancellationToken cancellationToken,
        Func<OperationCompletion, Result<Unit, DaemonError>>? terminalCommit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerPrincipal);
        ArgumentNullException.ThrowIfNull(executor);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lifecycleGate)
        {
            var now = _timeProvider.GetUtcNow();
            Guid id;
            do
            {
                id = Guid.NewGuid();
            } while (_operations.ContainsKey(id));

            var accepted = new OperationSnapshot(
                OperationId: id,
                Kind: kind,
                Target: target,
                OwnerPrincipal: ownerPrincipal,
                Status: OperationStatus.Queued,
                Stage: OperationStage.Queued,
                Progress: new OperationProgress(true, null, null, null, null, null, null),
                Version: 1,
                CreatedAt: now,
                UpdatedAt: now,
                CompletedAt: null,
                Cancellable: true,
                ErrorCode: null,
                ErrorMessage: null,
                ResultReference: null);
            var runtime = new OperationRuntime(accepted);

            try
            {
                lock (_persistGate)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (Volatile.Read(ref _disposed) != 0)
                    {
                        runtime.Dispose();
                        return Task.FromResult(Result.Err<OperationSnapshot, DaemonError>(
                            new ConflictDaemonError("operation.coordinator_stopped", "The operation coordinator is stopping.")));
                    }

                    var snapshots = _operations.Values
                        .Select(static current => current.Snapshot)
                        .Append(accepted)
                        .ToArray();
                    var persisted = PersistSnapshotsUnderLock(snapshots);
                    PrunePublishedOperations(persisted.RetainedOperationIds);

                    // Start admissions are serialized by _lifecycleGate. The id was checked while
                    // holding that gate, so publication cannot conflict after the candidate index
                    // has become durable.
                    if (!_operations.TryAdd(id, runtime))
                    {
                        runtime.Dispose();
                        throw new InvalidOperationException("A durable operation id collided during publication.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                runtime.Dispose();
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                runtime.Dispose();
                _logger.LogError(exception, "Failed to persist operation {OperationId} admission.", id);
                return Task.FromResult(Result.Err<OperationSnapshot, DaemonError>(
                    new InternalDaemonError("operation.persist_failed", "The operation could not be persisted.")));
            }

            try
            {
                _supervisor.Schedule(
                    $"operation:{id:D}",
                    shutdownToken => RunOperationAsync(runtime, executor, terminalCommit, shutdownToken));
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to schedule accepted operation {OperationId}.", id);
                Complete(runtime, OperationCompletion.Interrupted(), terminalCommit);
                runtime.Dispose();
            }

            return Task.FromResult(Result.Ok<OperationSnapshot, DaemonError>(accepted));
        }
    }

    private async Task RunOperationAsync(
        OperationRuntime runtime,
        Func<IServiceProvider, IOperationContext, CancellationToken, Task<Result<string, DaemonError>>> executor,
        Func<OperationCompletion, Result<Unit, DaemonError>>? terminalCommit,
        CancellationToken shutdownToken)
    {
        await Task.Yield();
        OperationCompletion completion;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            shutdownToken,
            runtime.CancellationToken);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            if (!TryStartAndPersist(runtime))
            {
                completion = OperationCompletion.Cancelled();
            }
            else
            {
                var context = new CoordinatorOperationContext(runtime, this);
                var result = await executor(scope.ServiceProvider, context, linked.Token).ConfigureAwait(false);
                if (linked.IsCancellationRequested || runtime.CancellationRequested)
                {
                    completion = OperationCompletion.Cancelled();
                }
                else if (result.IsErr(out var error))
                {
                    completion = OperationCompletion.Failed(error!.Code, error.Message);
                }
                else
                {
                    completion = OperationCompletion.Succeeded(result.Unwrap());
                }
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            completion = OperationCompletion.Cancelled();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Operation {OperationId} failed with an unexpected exception.", runtime.Snapshot.OperationId);
            completion = OperationCompletion.Failed(
                "operation.failed",
                "The operation failed due to an internal error.");
        }

        try
        {
            Complete(runtime, completion, terminalCommit);
        }
        finally
        {
            runtime.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_lifecycleGate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
        }

        try
        {
            await _supervisor.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            foreach (var runtime in _operations.Values)
                runtime.Dispose();

            if (_ownedScopeProvider is not null)
                await _ownedScopeProvider.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal void ReportProgress(OperationRuntime runtime, OperationStage? stage, OperationProgress? progress)
    {
        if (stage is not null)
        {
            PersistAndPublishTransition(
                runtime,
                current => CreateProgressSnapshot(current, stage, progress));
            return;
        }

        Mutate(
            runtime,
            current => CreateProgressSnapshot(current, stage: null, progress));
    }

    private void Complete(
        OperationRuntime runtime,
        OperationCompletion completion,
        Func<OperationCompletion, Result<Unit, DaemonError>>? terminalCommit)
    {
        OperationCompletion prepared;
        lock (_persistGate)
        {
            if (!runtime.TryPrepareCompletion(completion, _timeProvider.GetUtcNow(), out prepared))
                return;
        }

        if (terminalCommit is not null)
        {
            Result<Unit, DaemonError> committed;
            try
            {
                // The hook is a required part of the terminal commit. It must atomically persist
                // linked domain state before returning success. It deliberately runs outside the
                // operation runtime lock.
                committed = terminalCommit(prepared);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Operation {OperationId} terminal commit threw {ExceptionType}; publishing interrupted recovery.",
                    runtime.Snapshot.OperationId,
                    exception.GetType().FullName);
                PublishInterruptedAfterTerminalFailure(runtime);
                ScheduleTerminalCommitReconciliation(runtime, prepared, terminalCommit);
                return;
            }

            if (committed.IsErr(out var error))
            {
                _logger.LogError(
                    "Operation {OperationId} terminal commit failed with {ErrorCode}; publishing interrupted recovery.",
                    runtime.Snapshot.OperationId,
                    error!.Code);
                PublishInterruptedAfterTerminalFailure(runtime);
                ScheduleTerminalCommitReconciliation(runtime, prepared, terminalCommit);
                return;
            }
        }

        try
        {
            PersistAndPublishCompletion(runtime, prepared);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(
                exception,
                "Operation {OperationId} terminal persistence failed; publishing interrupted recovery.",
                runtime.Snapshot.OperationId);
            PublishInterruptedAfterTerminalFailure(runtime);
        }
    }

    private void PublishInterruptedAfterTerminalFailure(OperationRuntime runtime)
    {
        var interrupted = OperationCompletion.Interrupted(
            "The operation was interrupted because its terminal outcome could not be committed.");
        try
        {
            PersistAndPublishCompletion(runtime, interrupted);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(
                exception,
                "Operation {OperationId} interrupted recovery could not be persisted; publishing it for the current daemon lifetime.",
                runtime.Snapshot.OperationId);
            PublishCompletionWithoutPersistence(runtime, interrupted);
        }
    }

    private void ScheduleTerminalCommitReconciliation(
        OperationRuntime runtime,
        OperationCompletion completion,
        Func<OperationCompletion, Result<Unit, DaemonError>> terminalCommit)
    {
        var operationId = runtime.Snapshot.OperationId;
        try
        {
            _supervisor.Schedule(
                $"terminal-commit-reconciliation:{operationId:D}",
                cancellationToken => ReconcileTerminalCommitAsync(
                    operationId,
                    completion,
                    terminalCommit,
                    cancellationToken));
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0)
        {
            _logger.LogInformation(
                "Operation {OperationId} terminal commit reconciliation was deferred to startup recovery because the coordinator is stopping.",
                operationId);
        }
    }

    private async Task ReconcileTerminalCommitAsync(
        Guid operationId,
        OperationCompletion completion,
        Func<OperationCompletion, Result<Unit, DaemonError>> terminalCommit,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(TerminalCommitRetryDelay, cancellationToken).ConfigureAwait(false);
            try
            {
                var committed = terminalCommit(completion);
                if (!committed.IsErr(out var error))
                {
                    _logger.LogInformation(
                        "Operation {OperationId} terminal commit reconciliation completed.",
                        operationId);
                    return;
                }

                _logger.LogWarning(
                    "Operation {OperationId} terminal commit reconciliation still failed with {ErrorCode}; retrying.",
                    operationId,
                    error!.Code);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Operation {OperationId} terminal commit reconciliation threw {ExceptionType}; retrying.",
                    operationId,
                    exception.GetType().FullName);
            }
        }
    }

    private void PublishCompletionWithoutPersistence(OperationRuntime runtime, OperationCompletion completion)
    {
        lock (_persistGate)
        {
            if (runtime.TryCreateCompletionSnapshot(completion, _timeProvider.GetUtcNow(), out var candidate))
                runtime.PublishCompletion(candidate);
        }
    }

    private void PersistAndPublishCompletion(OperationRuntime runtime, OperationCompletion completion)
    {
        lock (_persistGate)
        {
            if (!runtime.TryCreateCompletionSnapshot(completion, _timeProvider.GetUtcNow(), out var candidate))
                return;

            // Persist the candidate while the currently published snapshot is still non-terminal.
            // Keeping the persistence gate through publication prevents another operation write
            // from overwriting the durable candidate with this runtime's old snapshot.
            var snapshots = CaptureSnapshots(runtime, candidate);
            var persisted = PersistSnapshotsUnderLock(snapshots);
            if (persisted.RetainedOperationIds.Contains(candidate.OperationId))
                runtime.PublishCompletion(candidate);
            PrunePublishedOperations(persisted.RetainedOperationIds);
        }
    }

    internal void PublishInterruptedAfterPlanReconciliation()
    {
        lock (_persistGate)
        {
            var now = _timeProvider.GetUtcNow();
            var candidates = new Dictionary<OperationRuntime, OperationSnapshot>();
            foreach (var runtime in _operations.Values)
            {
                if (runtime.TryCreateStartupRecoverySnapshot(now, out var candidate))
                    candidates.Add(runtime, candidate);
            }

            if (candidates.Count == 0)
                return;

            var snapshots = CaptureSnapshots(candidates);
            var persisted = PersistSnapshotsUnderLock(snapshots);

            foreach (var (runtime, candidate) in candidates)
            {
                if (persisted.RetainedOperationIds.Contains(candidate.OperationId))
                    runtime.PublishStartupRecovery(candidate);
            }
            PrunePublishedOperations(persisted.RetainedOperationIds);
        }
    }

    private bool TryStartAndPersist(OperationRuntime runtime)
    {
        lock (_persistGate)
        {
            if (!runtime.TryCreateStartSnapshot(_timeProvider.GetUtcNow(), out var candidate))
                return false;

            PersistAndPublishCandidateUnderLock(runtime, candidate, runtime.PublishTransition);
            return true;
        }
    }

    private void PersistAndPublishTransition(
        OperationRuntime runtime,
        Func<OperationSnapshot, OperationSnapshot> mutator)
    {
        lock (_persistGate)
        {
            if (!runtime.TryCreateTransitionSnapshot(mutator, out var candidate))
                return;

            PersistAndPublishCandidateUnderLock(runtime, candidate, runtime.PublishTransition);
        }
    }

    private void PersistAndPublishCandidateUnderLock(
        OperationRuntime runtime,
        OperationSnapshot candidate,
        Action<OperationSnapshot> publish)
    {
        var snapshots = CaptureSnapshots(runtime, candidate);
        var persisted = PersistSnapshotsUnderLock(snapshots);
        if (!persisted.RetainedOperationIds.Contains(candidate.OperationId))
            throw new InvalidOperationException("A non-terminal operation candidate was pruned during persistence.");

        publish(candidate);
        PrunePublishedOperations(persisted.RetainedOperationIds);
    }

    private void Mutate(
        OperationRuntime runtime,
        Func<OperationSnapshot, OperationSnapshot> mutator)
    {
        lock (_persistGate)
        {
            if (!runtime.Update(mutator))
                return;

            PersistProgressIfDueUnderLock();
        }
    }

    private OperationSnapshot CreateProgressSnapshot(
        OperationSnapshot current,
        OperationStage? stage,
        OperationProgress? progress)
    {
        if (IsTerminal(current.Status))
            return current;

        var nextStage = stage ?? current.Stage;
        var nextProgress = progress ?? current.Progress;
        if (nextStage == current.Stage && nextProgress == current.Progress)
            return current;

        return current with
        {
            Stage = nextStage,
            Progress = nextProgress,
            Version = current.Version + 1,
            UpdatedAt = _timeProvider.GetUtcNow(),
        };
    }

    private void PersistProgressIfDueUnderLock()
    {
        var now = _timeProvider.GetUtcNow();
        if (now - _lastProgressPersist < ProgressPersistInterval)
        {
            return;
        }

        PersistCurrentUnderLock();
    }

    private void PersistCurrentUnderLock()
    {
        var snapshots = _operations.Values.Select(static runtime => runtime.Snapshot).ToArray();
        var persisted = PersistSnapshotsUnderLock(snapshots);
        PrunePublishedOperations(persisted.RetainedOperationIds);
    }

    private OperationSnapshot[] CaptureSnapshots(
        OperationRuntime? overrideRuntime,
        OperationSnapshot? overrideSnapshot)
    {
        return _operations.Values
            .Select(runtime => ReferenceEquals(runtime, overrideRuntime) ? overrideSnapshot! : runtime.Snapshot)
            .ToArray();
    }

    private OperationSnapshot[] CaptureSnapshots(
        IReadOnlyDictionary<OperationRuntime, OperationSnapshot> overrides)
    {
        return _operations.Values
            .Select(runtime => overrides.TryGetValue(runtime, out var snapshot) ? snapshot : runtime.Snapshot)
            .ToArray();
    }

    private PersistedOperationIndex PersistSnapshotsUnderLock(OperationSnapshot[] snapshots)
    {
        var prepared = PrepareOperationIndex(snapshots, allowOversizedNonTerminal: false);
        WritePreparedIndexUnderLock(prepared);
        return new PersistedOperationIndex(prepared.RetainedOperationIds);
    }

    private PreparedOperationIndex PrepareOperationIndex(
        OperationSnapshot[] snapshots,
        bool allowOversizedNonTerminal)
    {
        var retained = new Dictionary<Guid, OperationSnapshot>(snapshots.Length);
        foreach (var snapshot in snapshots)
        {
            ValidateSnapshot(snapshot);
            if (!retained.TryAdd(snapshot.OperationId, snapshot))
                throw new InvalidDataException($"The operation index contains duplicate id '{snapshot.OperationId:D}'.");
        }

        var originalCount = retained.Count;
        var cutoff = _timeProvider.GetUtcNow() - _retention;
        foreach (var snapshot in retained.Values.ToArray())
        {
            var completedAt = snapshot.CompletedAt ?? snapshot.UpdatedAt;
            if (IsTerminal(snapshot.Status) && completedAt < cutoff)
                retained.Remove(snapshot.OperationId);
        }

        var itemSizes = retained.Values.ToDictionary(
            static snapshot => snapshot.OperationId,
            static snapshot => (long)JsonSerializer.SerializeToUtf8Bytes(
                snapshot,
                OperationPersistenceJsonContext.Default.OperationSnapshot).Length);
        long encodedLength = 2 + itemSizes.Values.Sum() + Math.Max(0, retained.Count - 1);
        foreach (var snapshot in retained.Values
                     .Where(static snapshot => IsTerminal(snapshot.Status))
                     .OrderBy(static snapshot => snapshot.CompletedAt ?? snapshot.UpdatedAt)
                     .ThenBy(static snapshot => snapshot.OperationId))
        {
            if (encodedLength <= _maximumBytes)
                break;

            retained.Remove(snapshot.OperationId);
            itemSizes.Remove(snapshot.OperationId);
            encodedLength = 2 + itemSizes.Values.Sum() + Math.Max(0, retained.Count - 1);
        }

        var retainedSnapshots = retained.Values
            .OrderBy(static snapshot => snapshot.CreatedAt)
            .ThenBy(static snapshot => snapshot.OperationId)
            .ToArray();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            retainedSnapshots,
            OperationPersistenceJsonContext.Default.OperationSnapshotArray);
        if (bytes.LongLength > _maximumBytes && !allowOversizedNonTerminal)
        {
            throw new IOException(
                $"The operation index requires {bytes.LongLength} bytes, exceeding the configured {_maximumBytes}-byte cap without prunable terminal records.");
        }

        return new PreparedOperationIndex(
            bytes,
            retained.Keys.ToHashSet(),
            originalCount - retained.Count);
    }

    private void WritePreparedIndexUnderLock(PreparedOperationIndex prepared)
    {
        _indexWriter(prepared.Bytes);
        _lastProgressPersist = _timeProvider.GetUtcNow();
        Interlocked.Increment(ref _persistenceWriteCount);
        if (prepared.PrunedCount > 0)
        {
            _logger.LogInformation(
                "Pruned {PrunedCount} retained operation records to satisfy age and byte retention.",
                prepared.PrunedCount);
        }
    }

    private void WriteIndex(byte[] bytes)
    {
        var temp = _indexPath + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, _indexPath, overwrite: true);
    }

    private void PrunePublishedOperations(HashSet<Guid> retainedOperationIds)
    {
        foreach (var (operationId, _) in _operations)
        {
            if (retainedOperationIds.Contains(operationId) ||
                !_operations.TryRemove(operationId, out var removed))
            {
                continue;
            }

            removed.Dispose();
        }
    }

    private void LoadIndex()
    {
        if (!File.Exists(_indexPath))
            return;

        OperationSnapshot[] snapshots;
        try
        {
            var bytes = File.ReadAllBytes(_indexPath);
            snapshots = JsonSerializer.Deserialize(bytes, OperationPersistenceJsonContext.Default.OperationSnapshotArray)
                ?? throw new InvalidDataException("The operation index JSON root cannot be null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The operation index contains invalid JSON.", exception);
        }

        var prepared = PrepareOperationIndex(snapshots, allowOversizedNonTerminal: true);
        if (prepared.PrunedCount > 0 && prepared.Bytes.LongLength <= _maximumBytes)
            WritePreparedIndexUnderLock(prepared);

        foreach (var snapshot in snapshots)
        {
            if (!prepared.RetainedOperationIds.Contains(snapshot.OperationId))
                continue;
            if (!_operations.TryAdd(
                    snapshot.OperationId,
                    new OperationRuntime(snapshot, recoverOnly: !IsTerminal(snapshot.Status))))
            {
                throw new InvalidDataException(
                    $"The operation index contains duplicate id '{snapshot.OperationId:D}'.");
            }
        }
    }

    private static void ValidateSnapshot(OperationSnapshot snapshot)
    {
        if (snapshot is null ||
            snapshot.OperationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(snapshot.Kind) ||
            string.IsNullOrWhiteSpace(snapshot.OwnerPrincipal) ||
            snapshot.Progress is null ||
            !Enum.IsDefined(snapshot.Status) ||
            !Enum.IsDefined(snapshot.Stage) ||
            snapshot.Version < 1 ||
            snapshot.CreatedAt == default ||
            snapshot.UpdatedAt < snapshot.CreatedAt)
        {
            throw new InvalidDataException("The operation index contains an invalid operation record.");
        }

        if (IsTerminal(snapshot.Status))
        {
            if (snapshot.CompletedAt is null ||
                snapshot.CompletedAt < snapshot.CreatedAt ||
                snapshot.CompletedAt > snapshot.UpdatedAt ||
                snapshot.Cancellable ||
                !TerminalStageMatches(snapshot.Status, snapshot.Stage) ||
                !TerminalPayloadMatches(snapshot))
            {
                throw new InvalidDataException(
                    $"The terminal operation '{snapshot.OperationId:D}' has inconsistent terminal state.");
            }
        }
        else if (snapshot.CompletedAt is not null ||
                 TerminalStage(snapshot.Stage) ||
                 !NonTerminalStageMatches(snapshot.Status, snapshot.Stage) ||
                 snapshot.ErrorCode is not null ||
                 snapshot.ErrorMessage is not null ||
                 snapshot.ResultReference is not null)
        {
            throw new InvalidDataException(
                $"The non-terminal operation '{snapshot.OperationId:D}' has inconsistent terminal state.");
        }
    }

    private static bool TerminalPayloadMatches(OperationSnapshot snapshot) => snapshot.Status switch
    {
        OperationStatus.Succeeded =>
            !string.IsNullOrWhiteSpace(snapshot.ResultReference) &&
            snapshot.ErrorCode is null &&
            snapshot.ErrorMessage is null,
        OperationStatus.Failed =>
            !string.IsNullOrWhiteSpace(snapshot.ErrorCode) &&
            !string.IsNullOrWhiteSpace(snapshot.ErrorMessage) &&
            snapshot.ResultReference is null,
        OperationStatus.Cancelled =>
            string.Equals(snapshot.ErrorCode, "operation.cancelled", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(snapshot.ErrorMessage) &&
            snapshot.ResultReference is null,
        OperationStatus.Interrupted =>
            string.Equals(snapshot.ErrorCode, "operation.interrupted", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(snapshot.ErrorMessage) &&
            snapshot.ResultReference is null,
        _ => false,
    };

    private static bool NonTerminalStageMatches(OperationStatus status, OperationStage stage) => status switch
    {
        OperationStatus.Queued => stage == OperationStage.Queued,
        OperationStatus.Running => stage is not OperationStage.Queued && !TerminalStage(stage),
        _ => false,
    };

    private static bool TerminalStageMatches(OperationStatus status, OperationStage stage) =>
        (status, stage) is
            (OperationStatus.Succeeded, OperationStage.Succeeded) or
            (OperationStatus.Failed, OperationStage.Failed) or
            (OperationStatus.Cancelled, OperationStage.Cancelled) or
            (OperationStatus.Interrupted, OperationStage.Interrupted);

    private static bool TerminalStage(OperationStage stage) =>
        stage is OperationStage.Succeeded or OperationStage.Failed or OperationStage.Cancelled or OperationStage.Interrupted;

    private sealed record PreparedOperationIndex(
        byte[] Bytes,
        HashSet<Guid> RetainedOperationIds,
        int PrunedCount);

    private sealed record PersistedOperationIndex(HashSet<Guid> RetainedOperationIds);

    private static bool IsTerminal(OperationStatus status) =>
        status is OperationStatus.Succeeded or OperationStatus.Failed or OperationStatus.Cancelled or OperationStatus.Interrupted;

    /// <summary>
    /// Owner-only by default. Trusted admin principal "*" (main token) sees all.
    /// Empty owner is denied.
    /// </summary>
    private static bool IsVisibleTo(string? callerPrincipal, string ownerPrincipal)
    {
        if (string.IsNullOrWhiteSpace(callerPrincipal))
            return false;
        if (string.Equals(callerPrincipal, "*", StringComparison.Ordinal))
            return true;
        return string.Equals(callerPrincipal, ownerPrincipal, StringComparison.Ordinal);
    }

    internal sealed class OperationRuntime : IDisposable
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _cancellation = new();
        private OperationSnapshot _snapshot;
        private readonly bool _recoverOnly;
        private bool _cancellationRequested;
        private bool _completionPrepared;
        private int _disposed;

        public OperationRuntime(OperationSnapshot snapshot, bool recoverOnly = false)
        {
            _snapshot = snapshot;
            _recoverOnly = recoverOnly;
            if (recoverOnly)
            {
                _cancellationRequested = true;
                _cancellation.Cancel();
            }
        }

        public OperationSnapshot Snapshot
        {
            get
            {
                lock (_gate)
                    return _snapshot;
            }
        }

        public CancellationToken CancellationToken => _cancellation.Token;

        public bool CancellationRequested
        {
            get
            {
                lock (_gate)
                    return _cancellationRequested;
            }
        }

        public bool TryCreateCancellationSnapshot(DateTimeOffset now, out OperationSnapshot candidate)
        {
            lock (_gate)
            {
                if (_cancellationRequested || _completionPrepared || IsTerminal(_snapshot.Status) || !_snapshot.Cancellable)
                {
                    candidate = _snapshot;
                    return false;
                }

                candidate = _snapshot with
                {
                    Cancellable = false,
                    Version = _snapshot.Version + 1,
                    UpdatedAt = now,
                };
                return true;
            }
        }

        public void PublishCancellation(OperationSnapshot candidate)
        {
            lock (_gate)
            {
                if (_cancellationRequested || _completionPrepared || IsTerminal(_snapshot.Status) || !_snapshot.Cancellable)
                    throw new InvalidOperationException("The operation cancellation candidate is no longer publishable.");

                _cancellationRequested = true;
                _snapshot = candidate;
            }
        }

        public void SignalCancellation(ILogger logger)
        {
            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Operation {OperationId} cancellation callbacks failed.", Snapshot.OperationId);
            }
        }

        public bool TryCreateStartSnapshot(DateTimeOffset now, out OperationSnapshot candidate)
        {
            lock (_gate)
            {
                if (_cancellationRequested || _completionPrepared || _snapshot.Status != OperationStatus.Queued)
                {
                    candidate = _snapshot;
                    return false;
                }

                candidate = _snapshot with
                {
                    Status = OperationStatus.Running,
                    Stage = OperationStage.Resolving,
                    Version = _snapshot.Version + 1,
                    UpdatedAt = now,
                };
                return true;
            }
        }

        public bool TryCreateTransitionSnapshot(
            Func<OperationSnapshot, OperationSnapshot> mutator,
            out OperationSnapshot candidate)
        {
            lock (_gate)
            {
                candidate = _snapshot;
                if (_completionPrepared)
                    return false;

                candidate = mutator(_snapshot);
                return candidate != _snapshot;
            }
        }

        public void PublishTransition(OperationSnapshot candidate)
        {
            lock (_gate)
            {
                if (_completionPrepared ||
                    IsTerminal(_snapshot.Status) ||
                    candidate.OperationId != _snapshot.OperationId ||
                    candidate.Version != _snapshot.Version + 1)
                {
                    throw new InvalidOperationException("The operation transition candidate is no longer publishable.");
                }

                _snapshot = candidate;
            }
        }

        public bool TryPrepareCompletion(
            OperationCompletion completion,
            DateTimeOffset now,
            out OperationCompletion prepared)
        {
            lock (_gate)
            {
                prepared = completion;
                if (_completionPrepared || IsTerminal(_snapshot.Status))
                    return false;

                if (_cancellationRequested && completion.Status is not OperationStatus.Interrupted)
                    prepared = OperationCompletion.Cancelled();

                // Freeze the effective outcome before invoking the domain commit. Late cancellation
                // must not change the outcome after the hook has committed against it.
                _completionPrepared = true;
                if (_snapshot.Cancellable)
                {
                    _snapshot = _snapshot with
                    {
                        Cancellable = false,
                        Version = _snapshot.Version + 1,
                        UpdatedAt = now,
                    };
                }
                return true;
            }
        }

        public bool TryCreateCompletionSnapshot(
            OperationCompletion completion,
            DateTimeOffset now,
            out OperationSnapshot candidate)
        {
            lock (_gate)
            {
                if (!_completionPrepared || IsTerminal(_snapshot.Status))
                {
                    candidate = _snapshot;
                    return false;
                }

                candidate = _snapshot with
                {
                    Status = completion.Status,
                    Stage = completion.Stage,
                    CompletedAt = now,
                    Cancellable = false,
                    ErrorCode = completion.ErrorCode,
                    ErrorMessage = completion.ErrorMessage,
                    ResultReference = completion.ResultReference,
                    Version = _snapshot.Version + 1,
                    UpdatedAt = now,
                };
                return true;
            }
        }

        public void PublishCompletion(OperationSnapshot candidate)
        {
            lock (_gate)
            {
                if (!_completionPrepared || IsTerminal(_snapshot.Status))
                    throw new InvalidOperationException("The operation terminal candidate is no longer publishable.");

                _snapshot = candidate;
            }
        }

        public bool TryCreateStartupRecoverySnapshot(
            DateTimeOffset now,
            out OperationSnapshot candidate)
        {
            lock (_gate)
            {
                if (!_recoverOnly || IsTerminal(_snapshot.Status))
                {
                    candidate = _snapshot;
                    return false;
                }

                candidate = _snapshot with
                {
                    Status = OperationStatus.Interrupted,
                    Stage = OperationStage.Interrupted,
                    CompletedAt = now,
                    Cancellable = false,
                    ErrorCode = "operation.interrupted",
                    ErrorMessage = "The operation was interrupted by daemon restart.",
                    ResultReference = null,
                    Version = _snapshot.Version + 1,
                    UpdatedAt = now,
                };
                return true;
            }
        }

        public void PublishStartupRecovery(OperationSnapshot candidate)
        {
            lock (_gate)
            {
                if (!_recoverOnly || IsTerminal(_snapshot.Status))
                    throw new InvalidOperationException("The operation startup recovery candidate is no longer publishable.");

                _snapshot = candidate;
            }
        }

        public bool Update(Func<OperationSnapshot, OperationSnapshot> mutator)
        {
            lock (_gate)
            {
                if (_completionPrepared)
                    return false;

                var updated = mutator(_snapshot);
                if (updated == _snapshot)
                    return false;

                _snapshot = updated;
                return true;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            _cancellation.Dispose();
        }
    }

    internal readonly record struct OperationCompletion(
        OperationStatus Status,
        OperationStage Stage,
        string? ErrorCode,
        string? ErrorMessage,
        string? ResultReference)
    {
        internal static OperationCompletion Succeeded(string resultReference)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resultReference);
            return new(OperationStatus.Succeeded, OperationStage.Succeeded, null, null, resultReference);
        }

        internal static OperationCompletion Failed(string errorCode, string errorMessage) =>
            new(OperationStatus.Failed, OperationStage.Failed, errorCode, errorMessage, null);

        internal static OperationCompletion Cancelled() =>
            new(
                OperationStatus.Cancelled,
                OperationStage.Cancelled,
                "operation.cancelled",
                "The operation was cancelled.",
                null);

        internal static OperationCompletion Interrupted(string? message = null) =>
            new(
                OperationStatus.Interrupted,
                OperationStage.Interrupted,
                "operation.interrupted",
                message ?? "The operation was interrupted before execution could start.",
                null);
    }

    private sealed class CoordinatorOperationContext : IOperationContext
    {
        private readonly OperationRuntime _runtime;
        private readonly OperationCoordinator _coordinator;
        private readonly NestedProgressNode _root = new("root", 1);

        public CoordinatorOperationContext(OperationRuntime runtime, OperationCoordinator coordinator)
        {
            _runtime = runtime;
            _coordinator = coordinator;
        }

        public Guid OperationId => _runtime.Snapshot.OperationId;

        public void SetStage(OperationStage stage) =>
            _coordinator.ReportProgress(_runtime, stage, progress: null);

        public void ReportProgress(OperationProgress progress)
        {
            // Direct root reports are only valid before children exist.
            if (_root.HasChildren)
                return;
            _coordinator.ReportProgress(_runtime, stage: null, progress);
        }

        public IOperationContext CreateChild(string name, double weight) =>
            CreateChild(_root, name, weight);

        private IOperationContext CreateChild(NestedProgressNode parent, string name, double weight)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (weight <= 0)
                throw new ArgumentOutOfRangeException(nameof(weight), weight, "Child weight must be positive.");

            var node = new NestedProgressNode(name, weight);
            parent.AddChild(node);
            return new NestedOperationContext(this, node);
        }

        private void PublishRootProgress(OperationProgress leafHint)
        {
            var aggregate = new OperationProgress(
                Indeterminate: false,
                Completed: _root.Fraction,
                Total: 1,
                Unit: "weight",
                BytesTransferred: leafHint.BytesTransferred,
                BytesTotal: leafHint.BytesTotal,
                Rate: leafHint.Rate);
            _coordinator.ReportProgress(_runtime, stage: null, aggregate);
        }

        private sealed class NestedProgressNode(string name, double weight)
        {
            private readonly object _gate = new();
            private readonly List<NestedProgressNode> _children = [];
            private double _directFraction;

            public string Name { get; } = name;
            public double Weight { get; } = weight;
            public bool HasChildren
            {
                get
                {
                    lock (_gate)
                        return _children.Count > 0;
                }
            }

            public double Fraction
            {
                get
                {
                    lock (_gate)
                    {
                        if (_children.Count == 0)
                            return _directFraction;

                        var totalWeight = _children.Sum(static child => child.Weight);
                        if (totalWeight <= 0)
                            return 0;
                        return _children.Sum(static child => child.Weight * child.Fraction) / totalWeight;
                    }
                }
            }

            public void AddChild(NestedProgressNode child)
            {
                lock (_gate)
                    _children.Add(child);
            }

            public void SetDirectFraction(double fraction)
            {
                lock (_gate)
                    _directFraction = Math.Clamp(fraction, 0d, 1d);
            }
        }

        private sealed class NestedOperationContext(CoordinatorOperationContext owner, NestedProgressNode node) : IOperationContext
        {
            public Guid OperationId => owner.OperationId;

            public void SetStage(OperationStage stage) => _ = stage;

            public void ReportProgress(OperationProgress progress)
            {
                node.SetDirectFraction(ResolveFraction(progress));
                owner.PublishRootProgress(progress);
            }

            public IOperationContext CreateChild(string name, double weight) =>
                owner.CreateChild(node, name, weight);

            private static double ResolveFraction(OperationProgress progress)
            {
                if (progress.Total is > 0 && progress.Completed is not null)
                    return progress.Completed.Value / progress.Total.Value;
                if (progress.BytesTotal is > 0 && progress.BytesTransferred is not null)
                    return (double)progress.BytesTransferred.Value / progress.BytesTotal.Value;
                return progress.Indeterminate ? 0d : 1d;
            }
        }
    }
}
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    Converters = [
        typeof(JsonStringEnumConverter<OperationStatus>),
        typeof(JsonStringEnumConverter<OperationStage>)
    ])]
[JsonSerializable(typeof(OperationSnapshot[]))]
[JsonSerializable(typeof(OperationSnapshot))]
internal partial class OperationPersistenceJsonContext : JsonSerializerContext;
