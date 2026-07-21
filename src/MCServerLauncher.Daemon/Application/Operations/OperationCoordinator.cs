using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Storage;
using RustyOptions;

namespace MCServerLauncher.Daemon.ApplicationCore.Operations;

/// <summary>
/// Daemon-owned long-running operation coordinator.
/// Progress is coalesced in memory; index persistence is rate-limited for progress and
/// immediate for stage/terminal transitions. Per-operation DI scopes are owned by domain
/// adapters that call <see cref="ExecuteAsync"/> (e.g. provisioning), not this store.
/// </summary>
internal sealed class OperationCoordinator : IOperationApplication, IAsyncDisposable
{
    private static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(7);
    private static readonly TimeSpan ProgressPersistInterval = TimeSpan.FromMilliseconds(200);
    private const long DefaultMaxBytes = 268_435_456;

    private readonly ConcurrentDictionary<Guid, OperationRuntime> _operations = new();
    private readonly object _persistGate = new();
    private readonly string _root;
    private readonly string _indexPath;
    private readonly TimeProvider _timeProvider;
    private DateTimeOffset _lastProgressPersist = DateTimeOffset.MinValue;
    private int _disposed;

    public OperationCoordinator(TimeProvider? timeProvider = null, string? rootDirectory = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _root = Path.GetFullPath(rootDirectory ?? Path.Combine(FileManager.Root, "operations"));
        Directory.CreateDirectory(_root);
        _indexPath = Path.Combine(_root, "index.json");
        LoadIndex();
        MarkInterruptedOnStartup();
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

        if (!snapshot.Cancellable || IsTerminal(snapshot.Status))
        {
            return Task.FromResult(Result.Ok<OperationCancelResult, DaemonError>(
                new OperationCancelResult(request.OperationId, CancelRequested: false)));
        }

        runtime.RequestCancel();
        // Keep status Running until the executor observes cancellation; mark non-cancellable.
        Mutate(
            runtime,
            current =>
            {
                if (IsTerminal(current.Status) || !current.Cancellable)
                    return current;

                return current with
                {
                    Cancellable = false,
                    Version = current.Version + 1,
                    UpdatedAt = _timeProvider.GetUtcNow(),
                };
            },
            forcePersist: true);

        return Task.FromResult(Result.Ok<OperationCancelResult, DaemonError>(
            new OperationCancelResult(request.OperationId, CancelRequested: true)));
    }

    /// <summary>
    /// Creates and starts a daemon operation. Domain adapters own any per-operation DI scope
    /// and pass an executor that already captured scoped services.
    /// </summary>
    public async Task<Result<OperationSnapshot, DaemonError>> ExecuteAsync(
        string kind,
        string? target,
        string ownerPrincipal,
        Func<IOperationContext, CancellationToken, Task<Result<string, DaemonError>>> executor,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerPrincipal);
        ArgumentNullException.ThrowIfNull(executor);

        var now = _timeProvider.GetUtcNow();
        var id = Guid.NewGuid();
        var runtime = new OperationRuntime(new OperationSnapshot(
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
            ResultReference: null));

        if (!_operations.TryAdd(id, runtime))
        {
            return Result.Err<OperationSnapshot, DaemonError>(
                new InternalDaemonError("operation.create_failed", "The operation could not be created."));
        }

        Persist();
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, runtime.Cancellation.Token);
        Mutate(
            runtime,
            current => current with
            {
                Status = OperationStatus.Running,
                Stage = OperationStage.Resolving,
                Version = current.Version + 1,
                UpdatedAt = _timeProvider.GetUtcNow(),
            },
            forcePersist: true);

        try
        {
            var context = new CoordinatorOperationContext(runtime, this);
            var result = await executor(context, linked.Token).ConfigureAwait(false);
            if (result.IsErr(out var error))
            {
                TryComplete(runtime, OperationStatus.Failed, OperationStage.Failed, error!.Code, error.Message, resultReference: null);
                return Result.Err<OperationSnapshot, DaemonError>(error!);
            }

            TryComplete(runtime, OperationStatus.Succeeded, OperationStage.Succeeded, errorCode: null, errorMessage: null, resultReference: result.Unwrap());
            return Result.Ok<OperationSnapshot, DaemonError>(runtime.Snapshot);
        }
        catch (OperationCanceledException) when (runtime.Cancellation.IsCancellationRequested)
        {
            TryComplete(runtime, OperationStatus.Cancelled, OperationStage.Cancelled, errorCode: "operation.cancelled", errorMessage: "The operation was cancelled.", resultReference: null);
            return Result.Err<OperationSnapshot, DaemonError>(
                new ConflictDaemonError("operation.cancelled", "The operation was cancelled."));
        }
        catch (Exception)
        {
            TryComplete(runtime, OperationStatus.Failed, OperationStage.Failed, errorCode: "operation.failed", errorMessage: "The operation failed due to an internal error.", resultReference: null);
            return Result.Err<OperationSnapshot, DaemonError>(
                new InternalDaemonError("operation.failed", "The operation failed due to an internal error."));
        }
        finally
        {
            linked.Dispose();
            EnforceRetention();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return ValueTask.CompletedTask;

        foreach (var runtime in _operations.Values)
        {
            runtime.RequestCancel();
            runtime.Dispose();
        }
        return ValueTask.CompletedTask;
    }

    internal void ReportProgress(OperationRuntime runtime, OperationStage? stage, OperationProgress progress)
    {
        Mutate(
            runtime,
            current =>
            {
                if (IsTerminal(current.Status))
                    return current;

                return current with
                {
                    Stage = stage ?? current.Stage,
                    Progress = progress,
                    Version = current.Version + 1,
                    UpdatedAt = _timeProvider.GetUtcNow(),
                };
            },
            forcePersist: stage is not null);
    }

    private void TryComplete(
        OperationRuntime runtime,
        OperationStatus status,
        OperationStage stage,
        string? errorCode,
        string? errorMessage,
        string? resultReference)
    {
        Mutate(
            runtime,
            current =>
            {
                if (IsTerminal(current.Status))
                    return current;

                return current with
                {
                    Status = status,
                    Stage = stage,
                    CompletedAt = _timeProvider.GetUtcNow(),
                    Cancellable = false,
                    ErrorCode = errorCode,
                    ErrorMessage = errorMessage,
                    ResultReference = resultReference,
                    Version = current.Version + 1,
                    UpdatedAt = _timeProvider.GetUtcNow(),
                };
            },
            forcePersist: true);
    }

    private void MarkInterruptedOnStartup()
    {
        var changed = false;
        foreach (var runtime in _operations.Values)
        {
            if (IsTerminal(runtime.Snapshot.Status))
                continue;

            runtime.Update(current => current with
            {
                Status = OperationStatus.Interrupted,
                Stage = OperationStage.Interrupted,
                CompletedAt = _timeProvider.GetUtcNow(),
                Cancellable = false,
                Version = current.Version + 1,
                UpdatedAt = _timeProvider.GetUtcNow(),
            });
            changed = true;
        }

        if (changed)
            Persist();
    }

    private void Mutate(OperationRuntime runtime, Func<OperationSnapshot, OperationSnapshot> mutator, bool forcePersist)
    {
        runtime.Update(mutator);
        if (forcePersist)
        {
            Persist();
            return;
        }

        var now = _timeProvider.GetUtcNow();
        if (now - _lastProgressPersist < ProgressPersistInterval)
            return;

        _lastProgressPersist = now;
        Persist();
    }

    private void Persist()
    {
        lock (_persistGate)
        {
            var snapshots = _operations.Values.Select(static runtime => runtime.Snapshot).ToArray();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshots, OperationPersistenceJsonContext.Default.OperationSnapshotArray);
            var temp = _indexPath + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, _indexPath, overwrite: true);
            _lastProgressPersist = _timeProvider.GetUtcNow();
        }
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

        foreach (var runtime in terminal)
        {
            var completedAt = runtime.Snapshot.CompletedAt ?? runtime.Snapshot.UpdatedAt;
            if (completedAt < cutoff)
                _operations.TryRemove(runtime.Snapshot.OperationId, out _);
        }

        try
        {
            if (File.Exists(_indexPath) && new FileInfo(_indexPath).Length > DefaultMaxBytes)
            {
                foreach (var runtime in terminal.Take(Math.Max(1, terminal.Count / 4)))
                    _operations.TryRemove(runtime.Snapshot.OperationId, out _);
            }
        }
        catch (IOException)
        {
        }

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
        private int _disposed;

        public OperationRuntime(OperationSnapshot snapshot, bool recoverOnly = false)
        {
            _snapshot = snapshot;
            if (recoverOnly)
                _cancellation.Cancel();
        }

        public OperationSnapshot Snapshot
        {
            get
            {
                lock (_gate)
                    return _snapshot;
            }
        }

        public CancellationTokenSource Cancellation => _cancellation;

        public void RequestCancel()
        {
            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Update(Func<OperationSnapshot, OperationSnapshot> mutator)
        {
            lock (_gate)
                _snapshot = mutator(_snapshot);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            _cancellation.Dispose();
        }
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
            _coordinator.ReportProgress(_runtime, stage, _runtime.Snapshot.Progress);

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
