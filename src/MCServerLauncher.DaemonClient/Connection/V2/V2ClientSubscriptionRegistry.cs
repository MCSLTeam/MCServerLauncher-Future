using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Events;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.DaemonClient.Protocol;
using RustyOptions;

namespace MCServerLauncher.DaemonClient.Connection.V2;

/// <summary>
/// Owns caller-held logical subscriptions independently from physical V2 connection epochs.
/// </summary>
internal sealed class V2ClientSubscriptionRegistry : IAsyncDisposable
{
    internal const int PendingEventCapacity = 256;
    internal const string NotReadyCode = "client.not_ready";
    internal const string UnsupportedEventCode = "client.event.unsupported";
    internal const string InvalidFilterCode = "client.event.filter_invalid";
    internal const string BufferOverflowCode = "client.event.buffer_overflow";

    private static readonly DescriptorBinding DaemonReportBinding =
        new DescriptorBinding<DaemonReportEventData, EmptyRequest>(V2ClientProtocol.DaemonReport);
    private static readonly DescriptorBinding InstanceLogBinding =
        new DescriptorBinding<InstanceLogEventData, InstanceLogEventMeta>(V2ClientProtocol.InstanceLog);
    private static readonly DescriptorBinding NotificationBinding =
        new DescriptorBinding<NotificationEventData, NotificationEventMeta>(V2ClientProtocol.Notification);

    private readonly object _gate = new();
    private readonly SemaphoreSlim _mutationLane = new(1, 1);
    private readonly Action<V2ClientConnectionCoordinator, DaemonError> _invalidateEpoch;
    private readonly Action<V2ClientDiagnostic>? _diagnostic;
    private readonly Func<Action, bool> _queueDeliveryDrain;
    private readonly Dictionary<SubscriptionKey, SubscriptionGroup> _groups =
        new(SubscriptionKeyComparer.Instance);
    private readonly Queue<Delivery> _deliveries = new();
    private V2ClientConnectionCoordinator? _boundEpoch;
    private V2ClientConnectionCoordinator? _candidateEpoch;
    private readonly ConditionalWeakTable<V2ClientConnectionCoordinator, InvalidEpochMarker> _invalidEpochs = new();
    private long _nextRegistration;
    private long _nextRoute;
    private bool _deliveryDraining;
    private bool _disposed;

    internal V2ClientSubscriptionRegistry(
        Action<V2ClientConnectionCoordinator, DaemonError> invalidateEpoch,
        Action<V2ClientDiagnostic>? diagnostic = null,
        Func<Action, bool>? queueDeliveryDrain = null)
    {
        _invalidateEpoch = invalidateEpoch ?? throw new ArgumentNullException(nameof(invalidateEpoch));
        _diagnostic = diagnostic;
        _queueDeliveryDrain = queueDeliveryDrain ?? QueueDeliveryDrain;
    }

    internal static bool HandleRetainsResources(IAsyncDisposable handle) =>
        handle is SubscriptionHandle subscription && subscription.RetainsResources;

    internal async Task<Result<IAsyncDisposable, DaemonError>> SubscribeAsync<TData, TMeta>(
        EventDescriptor<TData, TMeta> descriptor,
        DaemonEventFilter<TMeta> filter,
        Func<DaemonEvent<TData, TMeta>, Task> callback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(callback);

        if (!IsSupportedDescriptor(descriptor))
            return Result.Err<IAsyncDisposable, DaemonError>(UnsupportedEvent());

        var prepared = PrepareFilter(descriptor, filter);
        if (prepared.IsErr(out var filterError))
            return Result.Err<IAsyncDisposable, DaemonError>(filterError!);
        var preparedFilter = prepared.Unwrap();

        await _mutationLane.WaitAsync(cancellationToken).ConfigureAwait(false);
        var laneHeld = true;
        try
        {
            V2ClientConnectionCoordinator? epoch;
            SubscriptionGroup? existing;
            lock (_gate)
            {
                if (_disposed ||
                    _boundEpoch is not { IsReady: true } ready ||
                    IsInvalidEpoch(ready))
                {
                    return Result.Err<IAsyncDisposable, DaemonError>(NotReady());
                }

                epoch = ready;
                if (GetBuiltInBinding(descriptor.Name.Value) is { } builtInBinding &&
                    !ReferenceEquals(builtInBinding.Descriptor, descriptor))
                {
                    return Result.Err<IAsyncDisposable, DaemonError>(DescriptorConflict());
                }

                if (_groups.Values.Any(group =>
                        StringComparer.Ordinal.Equals(group.Key.Event, preparedFilter.Key.Event) &&
                        !ReferenceEquals(group.Binding.Descriptor, descriptor)))
                {
                    return Result.Err<IAsyncDisposable, DaemonError>(DescriptorConflict());
                }

                if (_groups.TryGetValue(preparedFilter.Key, out existing))
                {
                    if (!ReferenceEquals(existing.Binding.Descriptor, descriptor))
                        return Result.Err<IAsyncDisposable, DaemonError>(DescriptorConflict());

                    var duplicate = AddHandleLocked(existing, callback);
                    return Result.Ok<IAsyncDisposable, DaemonError>(duplicate);
                }

                var binding = new DescriptorBinding<TData, TMeta>(descriptor);
                var group = new SubscriptionGroup(preparedFilter.Key, preparedFilter.Request, binding)
                {
                    State = EpochBindingState.Pending,
                    Epoch = epoch
                };
                existing = group;
                _groups.Add(group.Key, group);
                AddHandleLocked(group, callback);
            }

            Result<Unit, DaemonError> result;
            try
            {
                result = await epoch.Core.InvokeUnitAsync(
                    V2ClientProtocol.SubscribeEvent,
                    existing.Request,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                RemoveFailedSubscription(existing);
                var invalidation = MarkInvalid(epoch, AmbiguousCancellationError());
                ReleaseLane(ref laneHeld);
                NotifyInvalidation(invalidation);
                throw;
            }

            if (result.IsErr(out var error))
            {
                RemoveFailedSubscription(existing);
                if (IsAmbiguous(error!))
                {
                    var invalidation = MarkInvalid(epoch, error!);
                    ReleaseLane(ref laneHeld);
                    NotifyInvalidation(invalidation);
                }
                else
                {
                    ResumeDeliveryDrain();
                }
                return Result.Err<IAsyncDisposable, DaemonError>(error!);
            }

            SubscriptionHandle handle;
            lock (_gate)
            {
                if (_disposed || !ReferenceEquals(_boundEpoch, epoch) || IsInvalidEpoch(epoch))
                {
                    RemoveGroupLocked(existing);
                    return Result.Err<IAsyncDisposable, DaemonError>(NotReady());
                }

                handle = existing.Handles[0];
                existing.State = EpochBindingState.Draining;
            }

            ReleaseLane(ref laneHeld);
            DrainAndActivate(epoch, [existing]);
            return Result.Ok<IAsyncDisposable, DaemonError>(handle);
        }
        finally
        {
            if (laneHeld)
                _mutationLane.Release();
        }
    }

    internal async Task<Result<Unit, DaemonError>> BindReadyEpochAsync(
        V2ClientConnectionCoordinator epoch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        await _mutationLane.WaitAsync(cancellationToken).ConfigureAwait(false);
        var laneHeld = true;
        try
        {
            lock (_gate)
            {
                if (_disposed || IsInvalidEpoch(epoch))
                    return Result.Err<Unit, DaemonError>(NotReady());
                if (ReferenceEquals(_boundEpoch, epoch) && epoch.IsReady)
                    return Result.Ok<Unit, DaemonError>(Unit.Default);
                if (_boundEpoch is not null || _candidateEpoch is not null)
                    return Result.Err<Unit, DaemonError>(NotReady());
            }

            Result<Unit, DaemonError> readiness;
            try
            {
                readiness = await epoch.WaitForReadyAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                var invalidation = MarkInvalid(epoch, AmbiguousCancellationError(), allowUnattached: true);
                ReleaseLane(ref laneHeld);
                NotifyInvalidation(invalidation);
                throw;
            }

            if (readiness.IsErr(out var readinessError))
                return Result.Err<Unit, DaemonError>(readinessError!);

            SubscriptionGroup[] replay;
            lock (_gate)
            {
                if (_disposed || IsInvalidEpoch(epoch))
                    return Result.Err<Unit, DaemonError>(NotReady());

                _candidateEpoch = epoch;
                replay = _groups.Values
                    .OrderBy(static group => group.FirstRegistration)
                    .ToArray();
                foreach (var group in replay)
                {
                    group.State = EpochBindingState.Pending;
                    group.Epoch = epoch;
                    group.Buffer.Clear();
                }
            }

            foreach (var group in replay)
            {
                Result<Unit, DaemonError> subscribed;
                try
                {
                    subscribed = await epoch.Core.InvokeUnitAsync(
                        V2ClientProtocol.SubscribeEvent,
                        group.Request,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    var invalidation = MarkInvalid(epoch, AmbiguousCancellationError());
                    ReleaseLane(ref laneHeld);
                    NotifyInvalidation(invalidation);
                    throw;
                }

                if (subscribed.IsErr(out var error))
                {
                    if (IsAmbiguous(error!))
                    {
                        var invalidation = MarkInvalid(epoch, error!);
                        ReleaseLane(ref laneHeld);
                        NotifyInvalidation(invalidation);
                    }
                    else
                        DetachCandidate(epoch);
                    return Result.Err<Unit, DaemonError>(error!);
                }
            }

            lock (_gate)
            {
                if (_disposed || !ReferenceEquals(_candidateEpoch, epoch) || IsInvalidEpoch(epoch))
                {
                    DetachCandidateLocked(epoch);
                    return Result.Err<Unit, DaemonError>(NotReady());
                }

                _candidateEpoch = null;
                _boundEpoch = epoch;
                foreach (var group in replay)
                    group.State = EpochBindingState.Draining;
            }

            ReleaseLane(ref laneHeld);
            DrainAndActivate(epoch, replay);
            return Result.Ok<Unit, DaemonError>(Unit.Default);
        }
        finally
        {
            if (laneHeld)
                _mutationLane.Release();
        }
    }

    internal void DetachEpoch(V2ClientConnectionCoordinator epoch)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        var startDeliveryDrain = false;
        lock (_gate)
        {
            if (ReferenceEquals(_boundEpoch, epoch))
                _boundEpoch = null;
            DetachCandidateLocked(epoch);
            foreach (var group in _groups.Values.Where(group => ReferenceEquals(group.Epoch, epoch)))
                UnbindGroupLocked(group);
            startDeliveryDrain = TryStartDeliveryDrainLocked();
        }

        StartDeliveryDrain(startDeliveryDrain);
    }

    internal void Route(
        V2ClientConnectionCoordinator epoch,
        JsonRpcRemoteEventNotification notification)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        ArgumentNullException.ThrowIfNull(notification);

        var binding = GetBinding(notification.Method);
        if (binding is null)
            return;

        var materialized = binding.Materialize(notification);
        if (materialized.IsErr(out var error))
        {
            Invalidate(epoch, error!);
            return;
        }

        var value = materialized.Unwrap();
        var callbacks = new List<SubscriptionHandle>();
        var pendingGroups = new List<SubscriptionGroup>();
        var overflow = false;
        var startDeliveryDrain = false;
        lock (_gate)
        {
            if (_disposed || IsInvalidEpoch(epoch) ||
                (!ReferenceEquals(_boundEpoch, epoch) && !ReferenceEquals(_candidateEpoch, epoch)))
            {
                return;
            }

            var routeOrder = ++_nextRoute;

            foreach (var group in _groups.Values)
            {
                if (!ReferenceEquals(group.Epoch, epoch) ||
                    !ReferenceEquals(group.Binding.Descriptor, binding.Descriptor) ||
                    !binding.Matches(group.Key, value))
                {
                    continue;
                }

                if (group.State is EpochBindingState.Pending or EpochBindingState.Draining)
                {
                    if (group.Buffer.Count == PendingEventCapacity)
                    {
                        overflow = true;
                        break;
                    }
                    pendingGroups.Add(group);
                }

                callbacks.AddRange(group.Handles.Where(static handle => !handle.IsDisposed));
            }

            if (!overflow && callbacks.Count != 0)
            {
                foreach (var group in pendingGroups)
                    group.Buffer.Add(new BufferedEvent(routeOrder));
                callbacks.Sort(static (left, right) => left.Registration.CompareTo(right.Registration));
                startDeliveryDrain = EnqueueDeliveryLocked(new(routeOrder, epoch, value, callbacks));
            }
        }

        if (overflow)
        {
            Invalidate(epoch, BufferOverflow());
            return;
        }

        StartDeliveryDrain(startDeliveryDrain);
    }

    public async ValueTask DisposeAsync()
    {
        await _mutationLane.WaitAsync().ConfigureAwait(false);
        var laneHeld = true;
        try
        {
            V2ClientConnectionCoordinator? epoch;
            SubscriptionGroup[] active;
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                epoch = _boundEpoch;
                active = epoch is null
                    ? []
                    : _groups.Values
                        .Where(group => group.State is EpochBindingState.Active or EpochBindingState.Draining &&
                                        ReferenceEquals(group.Epoch, epoch))
                        .OrderBy(static group => group.FirstRegistration)
                        .ToArray();
                foreach (var group in _groups.Values)
                {
                    foreach (var handle in group.Handles)
                        handle.MarkDisposed();
                    group.Handles.Clear();
                    UnbindGroupLocked(group);
                }
                _groups.Clear();
                _deliveries.Clear();
                _boundEpoch = null;
                _candidateEpoch = null;
            }

            if (epoch is null)
                return;

            foreach (var group in active)
            {
                var result = await epoch.Core.InvokeUnitAsync(
                    V2ClientProtocol.UnsubscribeEvent,
                    group.Request).ConfigureAwait(false);
                if (result.IsErr(out var error))
                {
                    var invalidation = MarkInvalid(epoch, error!, allowUnattached: true);
                    ReleaseLane(ref laneHeld);
                    NotifyInvalidation(invalidation);
                    return;
                }
            }
        }
        finally
        {
            if (laneHeld)
                _mutationLane.Release();
        }
    }

    private async ValueTask RemoveHandleAsync(SubscriptionHandle handle)
    {
        if (!handle.TryBeginDispose(out var ownedGroup))
            return;

        ResumeDeliveryDrain();
        await _mutationLane.WaitAsync().ConfigureAwait(false);
        var laneHeld = true;
        try
        {
            V2ClientConnectionCoordinator? epoch = null;
            EventSubscriptionRequest? request = null;
            lock (_gate)
            {
                if (!_groups.TryGetValue(ownedGroup.Key, out var current) ||
                    !ReferenceEquals(current, ownedGroup))
                {
                    return;
                }

                current.Handles.Remove(handle);
                if (current.Handles.Count != 0)
                    return;

                _groups.Remove(current.Key);
                if (current.State is EpochBindingState.Active or EpochBindingState.Draining &&
                    current.Epoch is not null)
                {
                    epoch = current.Epoch;
                    request = current.Request;
                }
                UnbindGroupLocked(current);
            }

            if (epoch is null)
                return;

            var result = await epoch.Core.InvokeUnitAsync(
                V2ClientProtocol.UnsubscribeEvent,
                request!).ConfigureAwait(false);
            if (result.IsErr(out var error))
            {
                var invalidation = MarkInvalid(epoch, error!, allowUnattached: true);
                ReleaseLane(ref laneHeld);
                NotifyInvalidation(invalidation);
            }
        }
        finally
        {
            if (laneHeld)
                _mutationLane.Release();
        }
    }

    private SubscriptionHandle AddHandleLocked<TData, TMeta>(
        SubscriptionGroup group,
        Func<DaemonEvent<TData, TMeta>, Task> callback)
    {
        var binding = (DescriptorBinding<TData, TMeta>)group.Binding;
        var handle = new SubscriptionHandle(
            this,
            group,
            ++_nextRegistration,
            value => callback(binding.GetTyped(value)));
        group.Handles.Add(handle);
        return handle;
    }

    private void RemoveFailedSubscription(SubscriptionGroup group)
    {
        lock (_gate)
        {
            if (_groups.TryGetValue(group.Key, out var current) && ReferenceEquals(current, group))
                RemoveGroupLocked(group);
        }
    }

    private void RemoveGroupLocked(SubscriptionGroup group)
    {
        _groups.Remove(group.Key);
        foreach (var handle in group.Handles)
            handle.MarkDisposed();
        group.Handles.Clear();
        UnbindGroupLocked(group);
    }

    private void DetachCandidate(V2ClientConnectionCoordinator epoch)
    {
        var startDeliveryDrain = false;
        lock (_gate)
        {
            DetachCandidateLocked(epoch);
            startDeliveryDrain = TryStartDeliveryDrainLocked();
        }

        StartDeliveryDrain(startDeliveryDrain);
    }

    private void DetachCandidateLocked(V2ClientConnectionCoordinator epoch)
    {
        if (ReferenceEquals(_candidateEpoch, epoch))
            _candidateEpoch = null;
        foreach (var group in _groups.Values.Where(group => ReferenceEquals(group.Epoch, epoch)))
            UnbindGroupLocked(group);
    }

    private static void UnbindGroupLocked(SubscriptionGroup group)
    {
        group.State = EpochBindingState.Unbound;
        group.Epoch = null;
        group.Buffer.Clear();
    }

    private void DrainAndActivate(
        V2ClientConnectionCoordinator epoch,
        IReadOnlyCollection<SubscriptionGroup> groups)
    {
        var startDeliveryDrain = false;
        lock (_gate)
        {
            if (_disposed || IsInvalidEpoch(epoch) ||
                (!ReferenceEquals(_boundEpoch, epoch) && !ReferenceEquals(_candidateEpoch, epoch)))
            {
                return;
            }

            var current = groups
                .Where(group => ReferenceEquals(group.Epoch, epoch) &&
                                group.State == EpochBindingState.Draining)
                .ToArray();
            if (current.Length != groups.Count)
                return;

            foreach (var group in current)
            {
                group.Buffer.Clear();
                group.State = EpochBindingState.Active;
            }
            startDeliveryDrain = TryStartDeliveryDrainLocked();
        }

        StartDeliveryDrain(startDeliveryDrain);
    }

    private bool EnqueueDeliveryLocked(Delivery delivery)
    {
        _deliveries.Enqueue(delivery);
        return TryStartDeliveryDrainLocked();
    }

    private bool TryStartDeliveryDrainLocked()
    {
        if (_deliveryDraining ||
            !_deliveries.TryPeek(out var delivery) ||
            IsDeliveryBlockedLocked(delivery))
        {
            return false;
        }

        _deliveryDraining = true;
        return true;
    }

    private bool IsDeliveryBlockedLocked(Delivery delivery)
    {
        foreach (var handle in delivery.Handles)
        {
            if (handle.IsDisposed ||
                !handle.TryGetGroup(out var group) ||
                !_groups.TryGetValue(group.Key, out var current) ||
                !ReferenceEquals(current, group) ||
                !ReferenceEquals(group.Epoch, delivery.Epoch))
            {
                continue;
            }

            if (group.State is EpochBindingState.Pending or EpochBindingState.Draining)
                return true;
        }

        return false;
    }

    private void ResumeDeliveryDrain()
    {
        bool startDeliveryDrain;
        lock (_gate)
            startDeliveryDrain = TryStartDeliveryDrainLocked();
        StartDeliveryDrain(startDeliveryDrain);
    }

    private void StartDeliveryDrain(bool start)
    {
        if (!start)
            return;

        try
        {
            if (_queueDeliveryDrain(StartDeliveryDrainWorker))
                return;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Delivery scheduling cannot own the transport receive path.
        }

        lock (_gate)
        {
            ClearDeliveriesLocked();
            _deliveryDraining = false;
        }
    }

    private void StartDeliveryDrainWorker() => _ = DrainDeliveriesAsync();

    private async Task DrainDeliveriesAsync()
    {
        try
        {
            while (true)
            {
                Delivery delivery;
                lock (_gate)
                {
                    if (!_deliveries.TryPeek(out delivery!) || IsDeliveryBlockedLocked(delivery))
                    {
                        _deliveryDraining = false;
                        return;
                    }

                    _deliveries.Dequeue();
                }

                foreach (var handle in delivery.Handles)
                {
                    Func<MaterializedEvent, Task>? callback;
                    lock (_gate)
                    {
                        callback = TryGetCallbackLocked(handle, delivery.Epoch);
                    }

                    if (callback is null)
                        continue;

                    try
                    {
                        await callback(delivery.Event).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        EmitDiagnostic(new(
                            V2ClientDiagnosticKind.ConsumerFault,
                            "A typed V2 event subscription callback failed."));
                    }
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            lock (_gate)
            {
                ClearDeliveriesLocked();
                _deliveryDraining = false;
            }
        }
    }

    private void ClearDeliveriesLocked()
    {
        _deliveries.Clear();
        foreach (var group in _groups.Values)
            group.Buffer.Clear();
    }

    private Func<MaterializedEvent, Task>? TryGetCallbackLocked(
        SubscriptionHandle handle,
        V2ClientConnectionCoordinator epoch)
    {
        if (_disposed || handle.IsDisposed ||
            !handle.TryGetGroup(out var group) ||
            !_groups.TryGetValue(group.Key, out var current) ||
            !ReferenceEquals(current, group) ||
            !ReferenceEquals(group.Epoch, epoch) ||
            (!ReferenceEquals(_boundEpoch, epoch) && !ReferenceEquals(_candidateEpoch, epoch)))
        {
            return null;
        }

        return handle.Callback;
    }

    private static bool QueueDeliveryDrain(Action drain) =>
        ThreadPool.QueueUserWorkItem(static callback => callback(), drain, preferLocal: false);

    private void Invalidate(V2ClientConnectionCoordinator epoch, DaemonError error)
    {
        NotifyInvalidation(MarkInvalid(epoch, error));
    }

    private EpochInvalidation? MarkInvalid(
        V2ClientConnectionCoordinator epoch,
        DaemonError error,
        bool allowUnattached = false)
    {
        EpochInvalidation? invalidation;
        var startDeliveryDrain = false;
        lock (_gate)
        {
            if (IsInvalidEpoch(epoch) ||
                (!allowUnattached &&
                 !ReferenceEquals(_boundEpoch, epoch) &&
                 !ReferenceEquals(_candidateEpoch, epoch)))
            {
                return null;
            }

            _invalidEpochs.GetValue(epoch, static _ => InvalidEpochMarker.Instance);
            if (ReferenceEquals(_boundEpoch, epoch))
                _boundEpoch = null;
            if (ReferenceEquals(_candidateEpoch, epoch))
                _candidateEpoch = null;
            foreach (var group in _groups.Values.Where(group => ReferenceEquals(group.Epoch, epoch)))
                UnbindGroupLocked(group);
            invalidation = new(epoch, error);
            startDeliveryDrain = TryStartDeliveryDrainLocked();
        }

        StartDeliveryDrain(startDeliveryDrain);
        return invalidation;
    }

    private void NotifyInvalidation(EpochInvalidation? invalidation)
    {
        if (invalidation is null)
            return;
        try
        {
            _invalidateEpoch(invalidation.Epoch, invalidation.Error);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            EmitDiagnostic(new(
                V2ClientDiagnosticKind.ConsumerFault,
                "The V2 epoch invalidation consumer failed."));
        }
    }

    private void ReleaseLane(ref bool laneHeld)
    {
        if (!laneHeld)
            return;
        laneHeld = false;
        _mutationLane.Release();
    }

    private void EmitDiagnostic(V2ClientDiagnostic diagnostic)
    {
        try
        {
            _diagnostic?.Invoke(diagnostic);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
    }

    private static bool IsAmbiguous(DaemonError error) =>
        error.Kind is not (DaemonErrorKind.Validation or DaemonErrorKind.Permission);

    private bool IsInvalidEpoch(V2ClientConnectionCoordinator epoch) =>
        _invalidEpochs.TryGetValue(epoch, out _);

    private static bool IsSupportedDescriptor(EventDescriptor descriptor)
    {
        var name = descriptor.Name.Value;
        return StringComparer.Ordinal.Equals(name, V2ClientProtocol.DaemonReport.Name.Value) ||
               StringComparer.Ordinal.Equals(name, V2ClientProtocol.InstanceLog.Name.Value) ||
               StringComparer.Ordinal.Equals(name, V2ClientProtocol.Notification.Name.Value) ||
               name.StartsWith("plugin.", StringComparison.Ordinal);
    }

    private DescriptorBinding? GetBinding(string method)
    {
        var builtInBinding = GetBuiltInBinding(method);
        if (builtInBinding is not null)
            return builtInBinding;

        lock (_gate)
        {
            return _groups.Values
                .Select(static group => group.Binding)
                .FirstOrDefault(binding => StringComparer.Ordinal.Equals(binding.Descriptor.Name.Value, method));
        }
    }

    private static DescriptorBinding? GetBuiltInBinding(string method) =>
        StringComparer.Ordinal.Equals(method, V2ClientProtocol.DaemonReport.Name.Value)
            ? DaemonReportBinding
            : StringComparer.Ordinal.Equals(method, V2ClientProtocol.InstanceLog.Name.Value)
                ? InstanceLogBinding
                : StringComparer.Ordinal.Equals(method, V2ClientProtocol.Notification.Name.Value)
                    ? NotificationBinding
                    : null;

    private static Result<PreparedFilter, DaemonError> PrepareFilter<TData, TMeta>(
        EventDescriptor<TData, TMeta> descriptor,
        DaemonEventFilter<TMeta> filter)
    {
        try
        {
            EventMetaFilter wire;
            ImmutableArray<byte> canonical;
            var metaTypeInfo = descriptor.MetaTypeInfo;
            switch (filter.Kind)
            {
                case DaemonEventFieldKind.Missing:
                    wire = EventMetaFilter.Missing;
                    canonical = [];
                    break;
                case DaemonEventFieldKind.ExplicitNull when
                    descriptor.MetaPresence == OpenRpcEventFieldPresence.Optional:
                    wire = EventMetaFilter.ExplicitNull;
                    canonical = [];
                    break;
                case DaemonEventFieldKind.Value when
                    descriptor.MetaPresence != OpenRpcEventFieldPresence.Omitted &&
                    metaTypeInfo is not null &&
                    filter.Value is { } exactValue &&
                    exactValue.GetType() == metaTypeInfo.Type:
                    canonical = JsonSerializer.SerializeToUtf8Bytes(
                        exactValue,
                        metaTypeInfo).ToImmutableArray();
                    wire = EventMetaFilter.FromObject(canonical.AsSpan());
                    break;
                default:
                    return Result.Err<PreparedFilter, DaemonError>(InvalidFilter());
            }

            var key = new SubscriptionKey(descriptor.Name.Value, filter.Kind, canonical);
            return Result.Ok<PreparedFilter, DaemonError>(new PreparedFilter(
                key,
                new EventSubscriptionRequest(descriptor.Name.Value, wire)));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            return Result.Err<PreparedFilter, DaemonError>(InvalidFilter());
        }
    }

    private static ValidationDaemonError NotReady() =>
        new(NotReadyCode, "The daemon client has no bound ready V2 connection epoch.");

    private static ValidationDaemonError UnsupportedEvent() =>
        new(UnsupportedEventCode, "The event descriptor is not caller-subscribable.");

    private static ConflictDaemonError DescriptorConflict() =>
        new("client.event.descriptor_conflict", "The event name is already subscribed with a different descriptor instance.");

    private static ValidationDaemonError InvalidFilter() =>
        new(InvalidFilterCode, "The typed event metadata filter is invalid for its descriptor.");

    private static TransportDaemonError BufferOverflow() =>
        new(BufferOverflowCode, "The pending typed event subscription buffer overflowed.");

    private static TransportDaemonError AmbiguousCancellationError() =>
        new("client.subscription_canceled", "A V2 subscription mutation was canceled after it may have reached the daemon.");

    private sealed record PreparedFilter(SubscriptionKey Key, EventSubscriptionRequest Request);

    private sealed record EpochInvalidation(
        V2ClientConnectionCoordinator Epoch,
        DaemonError Error);

    private sealed class InvalidEpochMarker
    {
        internal static InvalidEpochMarker Instance { get; } = new();
        private InvalidEpochMarker()
        {
        }
    }

    private sealed record SubscriptionKey(
        string Event,
        DaemonEventFieldKind FilterKind,
        ImmutableArray<byte> CanonicalUtf8);

    private sealed class SubscriptionKeyComparer : IEqualityComparer<SubscriptionKey>
    {
        internal static SubscriptionKeyComparer Instance { get; } = new();

        public bool Equals(SubscriptionKey? left, SubscriptionKey? right) =>
            ReferenceEquals(left, right) ||
            (left is not null && right is not null &&
             StringComparer.Ordinal.Equals(left.Event, right.Event) &&
             left.FilterKind == right.FilterKind &&
             left.CanonicalUtf8.AsSpan().SequenceEqual(right.CanonicalUtf8.AsSpan()));

        public int GetHashCode(SubscriptionKey value)
        {
            var hash = new HashCode();
            hash.Add(value.Event, StringComparer.Ordinal);
            hash.Add(value.FilterKind);
            foreach (var item in value.CanonicalUtf8)
                hash.Add(item);
            return hash.ToHashCode();
        }
    }

    private enum EpochBindingState
    {
        Unbound,
        Pending,
        Draining,
        Active
    }

    private sealed class SubscriptionGroup(
        SubscriptionKey key,
        EventSubscriptionRequest request,
        DescriptorBinding binding)
    {
        internal SubscriptionKey Key { get; } = key;
        internal EventSubscriptionRequest Request { get; } = request;
        internal DescriptorBinding Binding { get; } = binding;
        internal List<SubscriptionHandle> Handles { get; } = [];
        internal List<BufferedEvent> Buffer { get; } = [];
        internal EpochBindingState State { get; set; }
        internal V2ClientConnectionCoordinator? Epoch { get; set; }
        internal long FirstRegistration => Handles.Count == 0 ? long.MaxValue : Handles[0].Registration;
    }

    private sealed class SubscriptionHandle(
        V2ClientSubscriptionRegistry owner,
        SubscriptionGroup group,
        long registration,
        Func<MaterializedEvent, Task> callback) : IAsyncDisposable
    {
        private V2ClientSubscriptionRegistry? _owner = owner;
        private SubscriptionGroup? _group = group;
        private Func<MaterializedEvent, Task>? _callback = callback;
        private int _disposed;
        internal long Registration { get; } = registration;
        internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;
        internal bool RetainsResources => Volatile.Read(ref _owner) is not null ||
                                          Volatile.Read(ref _group) is not null ||
                                          Volatile.Read(ref _callback) is not null;
        internal Func<MaterializedEvent, Task>? Callback => Volatile.Read(ref _callback);
        internal bool TryGetGroup(out SubscriptionGroup group)
        {
            group = Volatile.Read(ref _group)!;
            return group is not null;
        }
        internal bool TryBeginDispose(out SubscriptionGroup group)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                group = null!;
                return false;
            }

            group = Interlocked.Exchange(ref _group, null) ??
                    throw new InvalidOperationException("A live subscription handle must retain its group.");
            Interlocked.Exchange(ref _callback, null);
            Interlocked.Exchange(ref _owner, null);
            return true;
        }
        internal void MarkDisposed()
        {
            Interlocked.Exchange(ref _disposed, 1);
            Interlocked.Exchange(ref _group, null);
            Interlocked.Exchange(ref _callback, null);
            Interlocked.Exchange(ref _owner, null);
        }
        public ValueTask DisposeAsync()
        {
            var current = Volatile.Read(ref _owner);
            return current is null ? ValueTask.CompletedTask : current.RemoveHandleAsync(this);
        }
    }

    private sealed record BufferedEvent(long RouteOrder);

    private sealed record Delivery(
        long RouteOrder,
        V2ClientConnectionCoordinator Epoch,
        MaterializedEvent Event,
        List<SubscriptionHandle> Handles);

    private abstract class MaterializedEvent
    {
    }

    private sealed class MaterializedEvent<TData, TMeta>(
        DaemonEvent<TData, TMeta> value,
        ImmutableArray<byte> canonicalMeta) : MaterializedEvent
    {
        internal DaemonEvent<TData, TMeta> Value { get; } = value;
        internal ImmutableArray<byte> CanonicalMeta { get; } = canonicalMeta;
    }

    private abstract class DescriptorBinding
    {
        internal abstract EventDescriptor Descriptor { get; }
        internal abstract Result<MaterializedEvent, DaemonError> Materialize(JsonRpcRemoteEventNotification notification);
        internal abstract bool Matches(SubscriptionKey key, MaterializedEvent value);
    }

    private sealed class DescriptorBinding<TData, TMeta>(EventDescriptor<TData, TMeta> descriptor)
        : DescriptorBinding
    {
        internal override EventDescriptor Descriptor => descriptor;

        internal override Result<MaterializedEvent, DaemonError> Materialize(
            JsonRpcRemoteEventNotification notification)
        {
            var result = V2ClientEventMaterializer.Materialize(descriptor, notification);
            if (result.IsErr(out var error))
                return Result.Err<MaterializedEvent, DaemonError>(error!);

            var value = result.Unwrap();
            ImmutableArray<byte> canonical = [];
            if (value.Meta.Kind == DaemonEventFieldKind.Value)
            {
                var typeInfo = descriptor.MetaTypeInfo ??
                    throw new InvalidOperationException("A materialized metadata value requires descriptor metadata.");
                canonical = JsonSerializer.SerializeToUtf8Bytes(value.Meta.Value, typeInfo).ToImmutableArray();
            }

            return Result.Ok<MaterializedEvent, DaemonError>(
                new MaterializedEvent<TData, TMeta>(value, canonical));
        }

        internal override bool Matches(SubscriptionKey key, MaterializedEvent value)
        {
            var typed = (MaterializedEvent<TData, TMeta>)value;
            return key.FilterKind switch
            {
                DaemonEventFieldKind.Missing => true,
                DaemonEventFieldKind.ExplicitNull =>
                    typed.Value.Meta.Kind == DaemonEventFieldKind.ExplicitNull,
                DaemonEventFieldKind.Value =>
                    typed.Value.Meta.Kind == DaemonEventFieldKind.Value &&
                    key.CanonicalUtf8.AsSpan().SequenceEqual(typed.CanonicalMeta.AsSpan()),
                _ => false
            };
        }

        internal DaemonEvent<TData, TMeta> GetTyped(MaterializedEvent value) =>
            ((MaterializedEvent<TData, TMeta>)value).Value;
    }
}
