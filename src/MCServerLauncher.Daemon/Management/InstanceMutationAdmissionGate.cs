namespace MCServerLauncher.Daemon.Management;

/// <summary>
/// Separates externally requested instance mutations from process-owned status callbacks so shutdown can drain both phases.
/// </summary>
internal sealed class InstanceMutationAdmissionGate
{
    private readonly object _gate = new();
    private bool _externalOpen = true;
    private bool _producerOpen = true;
    private int _activeExternal;
    private int _activeProducers;
    private TaskCompletionSource? _externalDrained;
    private TaskCompletionSource? _producersDrained;

    internal IDisposable EnterExternal()
    {
        lock (_gate)
        {
            if (!_externalOpen)
                throw new InvalidOperationException("Instance mutation admission has stopped for daemon shutdown.");

            _activeExternal++;
            return new AdmissionLease(this, producer: false);
        }
    }

    internal bool TryEnterProducer(out IDisposable? lease)
    {
        lock (_gate)
        {
            if (!_producerOpen)
            {
                lease = null;
                return false;
            }

            _activeProducers++;
            lease = new AdmissionLease(this, producer: true);
            return true;
        }
    }

    internal Task StopExternalAdmissionAndDrainAsync()
    {
        lock (_gate)
        {
            _externalOpen = false;
            if (_activeExternal == 0)
                return Task.CompletedTask;

            return (_externalDrained ??= CreateCompletion()).Task;
        }
    }

    internal Task StopProducerAdmissionAndDrainAsync()
    {
        lock (_gate)
        {
            _producerOpen = false;
            if (_activeProducers == 0)
                return Task.CompletedTask;

            return (_producersDrained ??= CreateCompletion()).Task;
        }
    }

    private static TaskCompletionSource CreateCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void Exit(bool producer)
    {
        TaskCompletionSource? completion = null;
        lock (_gate)
        {
            if (producer)
            {
                if (--_activeProducers == 0 && !_producerOpen)
                    completion = _producersDrained;
            }
            else if (--_activeExternal == 0 && !_externalOpen)
            {
                completion = _externalDrained;
            }
        }

        completion?.TrySetResult();
    }

    private sealed class AdmissionLease(InstanceMutationAdmissionGate owner, bool producer) : IDisposable
    {
        private InstanceMutationAdmissionGate? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Exit(producer);
        }
    }
}
