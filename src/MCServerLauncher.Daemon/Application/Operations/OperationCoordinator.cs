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
    private static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(7);
    private static readonly TimeSpan ProgressPersistInterval = TimeSpan.FromMilliseconds(200);
    private const long DefaultMaxBytes = 268_435_456;

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
    private DateTimeOffset _lastProgressPersist = DateTimeOffset.MinValue;
    private long _persistenceWriteCount;
    private int _disposed;

    public OperationCoordinator(
        TimeProvider? timeProvider = null,
        string? rootDirectory = null,
        IServiceScopeFactory? scopeFactory = null,
        ILogger<OperationCoordinator>? logger = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<OperationCoordinator>.Instance;
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
        _root = Path.GetFullPath(rootDirectory ?? Path.Combine(FileManager.Root, "operations"));
        Directory.CreateDirectory(_root);
        _indexPath = Path.Combine(_root, "index.json");
        LoadIndex();
        MarkInterruptedOnStartup();
    }

    internal long PersistenceWriteCount => Interlocked.Read(ref _persistenceWriteCount);

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

        if (!runtime.TryRequestCancellation(_timeProvider.GetUtcNow()))
        {
            return Task.FromResult(Result.Ok<OperationCancelResult, DaemonError>(
                new OperationCancelResult(request.OperationId, CancelRequested: false)));
        }

        try
        {
            Persist();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Failed to persist cancellation for operation {OperationId}.", request.OperationId);
            runtime.SignalCancellation(_logger);
            return Task.FromResult(Result.Err<OperationCancelResult, DaemonError>(
                new InternalDaemonError("operation.persist_failed", "The cancellation request could not be persisted.")));
        }

        runtime.SignalCancellation(_logger);

        return Task.FromResult(Result.Ok<OperationCancelResult, DaemonError>(
            new OperationCancelResult(request.OperationId, CancelRequested: true)));
    }

    /// <summary>
    /// Persists and accepts a daemon operation, then schedules its execution independently of
    /// the request lifetime. The returned snapshot is always the accepted queued snapshot.
    /// </summary>
    internal Task<Result<OperationSnapshot, DaemonError>> StartAsync(
        string kind,
        string? target,
        string ownerPrincipal,
        Func<IServiceProvider, IOperationContext, CancellationToken, Task<Result<string, DaemonError>>> executor,
        CancellationToken cancellationToken,
        Action<OperationSnapshot>? completionCallback = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerPrincipal);
        ArgumentNullException.ThrowIfNull(executor);
        cancellationToken.ThrowIfCancellationRequested();

        var now = _timeProvider.GetUtcNow();
        var id = Guid.NewGuid();
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

        lock (_lifecycleGate)
        {
            try
            {
                lock (_persistGate)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (Volatile.Read(ref _disposed) != 0)
                    {
                        return Task.FromResult(Result.Err<OperationSnapshot, DaemonError>(
                            new ConflictDaemonError("operation.coordinator_stopped", "The operation coordinator is stopping.")));
                    }

                    if (!_operations.TryAdd(id, runtime))
                    {
                        return Task.FromResult(Result.Err<OperationSnapshot, DaemonError>(
                            new InternalDaemonError("operation.create_failed", "The operation could not be created.")));
                    }

                    try
                    {
                        PersistUnderLock();
                    }
                    catch
                    {
                        _operations.TryRemove(id, out _);
                        runtime.Dispose();
                        throw;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(exception, "Failed to persist operation {OperationId} admission.", id);
                return Task.FromResult(Result.Err<OperationSnapshot, DaemonError>(
                    new InternalDaemonError("operation.persist_failed", "The operation could not be persisted.")));
            }

            try
            {
                _supervisor.Schedule(
                    $"operation:{id:D}",
                    shutdownToken => RunOperationAsync(runtime, executor, completionCallback, shutdownToken));
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to schedule accepted operation {OperationId}.", id);
                if (TryComplete(runtime, OperationCompletion.Interrupted()))
                    NotifyCompletion(runtime, completionCallback);
                runtime.Dispose();
            }

            return Task.FromResult(Result.Ok<OperationSnapshot, DaemonError>(accepted));
        }
    }

    private async Task RunOperationAsync(
        OperationRuntime runtime,
        Func<IServiceProvider, IOperationContext, CancellationToken, Task<Result<string, DaemonError>>> executor,
        Action<OperationSnapshot>? completionCallback,
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
            if (!runtime.TryStart(_timeProvider.GetUtcNow()))
            {
                completion = OperationCompletion.Cancelled();
            }
            else
            {
                Persist();
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

        if (TryComplete(runtime, completion))
            NotifyCompletion(runtime, completionCallback);
        runtime.Dispose();
        EnforceRetention();
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
        Mutate(
            runtime,
            current =>
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
            },
            forcePersist: stage is not null);
    }

    private bool TryComplete(OperationRuntime runtime, OperationCompletion completion)
    {
        if (!runtime.TryComplete(completion, _timeProvider.GetUtcNow()))
            return false;

        Persist();
        return true;
    }

    private void NotifyCompletion(OperationRuntime runtime, Action<OperationSnapshot>? completionCallback)
    {
        if (completionCallback is null)
            return;

        try
        {
            completionCallback(runtime.Snapshot);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Operation {OperationId} completion callback failed.",
                runtime.Snapshot.OperationId);
        }
    }

    private void MarkInterruptedOnStartup()
    {
        var changed = false;
        foreach (var runtime in _operations.Values)
        {
            if (IsTerminal(runtime.Snapshot.Status))
                continue;

            if (!runtime.Update(current => current with
            {
                Status = OperationStatus.Interrupted,
                Stage = OperationStage.Interrupted,
                CompletedAt = _timeProvider.GetUtcNow(),
                Cancellable = false,
                Version = current.Version + 1,
                UpdatedAt = _timeProvider.GetUtcNow(),
            }))
            {
                continue;
            }

            changed = true;
        }

        if (changed)
            Persist();
    }

    private void Mutate(OperationRuntime runtime, Func<OperationSnapshot, OperationSnapshot> mutator, bool forcePersist)
    {
        if (!runtime.Update(mutator))
            return;

        if (forcePersist)
        {
            Persist();
            return;
        }

        PersistProgressIfDue();
    }

    private void Persist()
    {
        lock (_persistGate)
            PersistUnderLock();
    }

    private void PersistProgressIfDue()
    {
        lock (_persistGate)
        {
            var now = _timeProvider.GetUtcNow();
            if (now - _lastProgressPersist < ProgressPersistInterval)
                return;

            PersistUnderLock();
        }
    }

    private void PersistUnderLock()
    {
        var snapshots = _operations.Values.Select(static runtime => runtime.Snapshot).ToArray();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            snapshots,
            OperationPersistenceJsonContext.Default.OperationSnapshotArray);
        var temp = _indexPath + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, _indexPath, overwrite: true);
        _lastProgressPersist = _timeProvider.GetUtcNow();
        Interlocked.Increment(ref _persistenceWriteCount);
    }

    private void LoadIndex()
    {
        if (!File.Exists(_indexPath))
            return;

        try
        {
            var bytes = File.ReadAllBytes(_indexPath);
            var snapshots = JsonSerializer.Deserialize(bytes, OperationPersistenceJsonContext.Default.OperationSnapshotArray);
            if (snapshots is null)
                return;

            foreach (var snapshot in snapshots)
            {
                _operations[snapshot.OperationId] = new OperationRuntime(snapshot, recoverOnly: true);
            }
        }
        catch (JsonException)
        {
            // Corrupt index is non-fatal; start empty and rewrite on next mutation.
        }
    }

    private void EnforceRetention()
    {
        var cutoff = _timeProvider.GetUtcNow() - DefaultRetention;
        var terminal = _operations.Values
            .Where(static runtime => IsTerminal(runtime.Snapshot.Status))
            .OrderBy(static runtime => runtime.Snapshot.CompletedAt ?? runtime.Snapshot.UpdatedAt)
            .ToList();
        var changed = false;

        foreach (var runtime in terminal)
        {
            var completedAt = runtime.Snapshot.CompletedAt ?? runtime.Snapshot.UpdatedAt;
            if (completedAt < cutoff && _operations.TryRemove(runtime.Snapshot.OperationId, out var removed))
            {
                removed.Dispose();
                changed = true;
            }
        }

        try
        {
            if (File.Exists(_indexPath) && new FileInfo(_indexPath).Length > DefaultMaxBytes)
            {
                foreach (var runtime in terminal.Take(Math.Max(1, terminal.Count / 4)))
                {
                    if (_operations.TryRemove(runtime.Snapshot.OperationId, out var removed))
                    {
                        removed.Dispose();
                        changed = true;
                    }
                }
            }
        }
        catch (IOException)
        {
        }

        if (changed)
            Persist();
    }

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
        private bool _cancellationRequested;
        private int _disposed;

        public OperationRuntime(OperationSnapshot snapshot, bool recoverOnly = false)
        {
            _snapshot = snapshot;
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

        public bool TryRequestCancellation(DateTimeOffset now)
        {
            lock (_gate)
            {
                if (_cancellationRequested || IsTerminal(_snapshot.Status) || !_snapshot.Cancellable)
                    return false;

                _cancellationRequested = true;
                _snapshot = _snapshot with
                {
                    Cancellable = false,
                    Version = _snapshot.Version + 1,
                    UpdatedAt = now,
                };
                return true;
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

        public bool TryStart(DateTimeOffset now)
        {
            lock (_gate)
            {
                if (_cancellationRequested || _snapshot.Status != OperationStatus.Queued)
                    return false;

                _snapshot = _snapshot with
                {
                    Status = OperationStatus.Running,
                    Stage = OperationStage.Resolving,
                    Version = _snapshot.Version + 1,
                    UpdatedAt = now,
                };
                return true;
            }
        }

        public bool TryComplete(OperationCompletion completion, DateTimeOffset now)
        {
            lock (_gate)
            {
                if (IsTerminal(_snapshot.Status))
                    return false;

                if (_cancellationRequested && completion.Status is not OperationStatus.Interrupted)
                    completion = OperationCompletion.Cancelled();

                _snapshot = _snapshot with
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

        public bool Update(Func<OperationSnapshot, OperationSnapshot> mutator)
        {
            lock (_gate)
            {
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
        internal static OperationCompletion Succeeded(string resultReference) =>
            new(OperationStatus.Succeeded, OperationStage.Succeeded, null, null, resultReference);

        internal static OperationCompletion Failed(string errorCode, string errorMessage) =>
            new(OperationStatus.Failed, OperationStage.Failed, errorCode, errorMessage, null);

        internal static OperationCompletion Cancelled() =>
            new(
                OperationStatus.Cancelled,
                OperationStage.Cancelled,
                "operation.cancelled",
                "The operation was cancelled.",
                null);

        internal static OperationCompletion Interrupted() =>
            new(
                OperationStatus.Interrupted,
                OperationStage.Interrupted,
                "operation.interrupted",
                "The operation was interrupted before execution could start.",
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
internal partial class OperationPersistenceJsonContext : JsonSerializerContext;
