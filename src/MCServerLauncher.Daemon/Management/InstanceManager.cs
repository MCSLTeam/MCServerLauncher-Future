using System.Collections.Concurrent;
using System.Threading;
using MCServerLauncher.Common.Detection;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.State;
using MCServerLauncher.Daemon.ApplicationCore;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.Management.Communicate;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using RustyOptions.Async;
using Serilog;
using InstanceConfiguration = MCServerLauncher.Common.Contracts.Instances.InstanceConfiguration;
using InstanceFactoryConfiguration = MCServerLauncher.Common.Contracts.Instances.InstanceFactoryConfiguration;
using InstanceSettingsResult = MCServerLauncher.Common.Contracts.Instances.InstanceSettingsResult;
using UpdateInstanceSettingsRequest = MCServerLauncher.Common.Contracts.Instances.UpdateInstanceSettingsRequest;
using UpdateInstanceSettingsResult = MCServerLauncher.Common.Contracts.Instances.UpdateInstanceSettingsResult;

namespace MCServerLauncher.Daemon.Management;

internal class InstanceManager : IInstanceManager
{
    private readonly InstanceUpdateCoordinator _instanceUpdateCoordinator;
    private readonly Func<InstanceFactoryConfiguration, Task<Result<InstanceConfiguration, Error>>> _applyInstanceFactory;
    private readonly Func<InstanceConfig, IInstance> _instanceFactory;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _instanceMutationGates = new();
    private readonly Lock _mutationLock = new();
    private readonly Dictionary<Guid, InstanceCreationReservation> _creationReservations = [];
    private readonly InstanceCatalogCommitFeed _catalogCommitFeed = new();
    private readonly InstanceMutationAdmissionGate _mutationAdmission = new();
    private AuthoritativeInstanceSnapshotSource _snapshotSource;

    public ConcurrentDictionary<Guid, IInstance> Instances { get; } = new();
    public ConcurrentDictionary<Guid, IInstance> RunningInstances { get; } = new();

    /// <summary>
    /// Raw daemon-internal lifecycle stream for a later domain-event bridge.
    /// </summary>
    internal event Func<Guid, string, CancellationToken, Task>? InstanceLogReceived;

    /// <summary>
    /// Raw daemon-internal lifecycle stream for a later domain-event bridge.
    /// </summary>
    internal event Func<Guid, InstanceStatus, CancellationToken, Task>? InstanceStatusChanged;

    internal IInstanceSnapshotSource InstanceSnapshotSource => _snapshotSource;
    internal InstanceCatalogCommitFeed CatalogCommitFeed => _catalogCommitFeed;
    internal InstanceMutationAdmissionGate MutationAdmission => _mutationAdmission;

    public InstanceManager()
        : this(
            static config => config.CreateInstance(),
            static setting => setting.ApplyInstanceFactory())
    {
    }

    internal InstanceManager(Func<InstanceConfig, IInstance> instanceFactory)
        : this(instanceFactory, static setting => setting.ApplyInstanceFactory())
    {
    }

    internal InstanceManager(
        Func<InstanceConfig, IInstance> instanceFactory,
        Func<InstanceFactoryConfiguration, Task<Result<InstanceConfiguration, Error>>> applyInstanceFactory)
    {
        ArgumentNullException.ThrowIfNull(instanceFactory);
        ArgumentNullException.ThrowIfNull(applyInstanceFactory);
        _instanceFactory = instanceFactory;
        _applyInstanceFactory = applyInstanceFactory;
        _snapshotSource = new AuthoritativeInstanceSnapshotSource(Instances, _catalogCommitFeed);
        _instanceUpdateCoordinator = new InstanceUpdateCoordinator(this, instanceFactory);
    }

    public async Task<Result<InstanceConfiguration, Error>> TryAddInstance(
        InstanceFactoryConfiguration setting,
        CancellationToken ct = default)
    {
        using var admission = _mutationAdmission.EnterExternal();
        ct.ThrowIfCancellationRequested();
        var requestedConfig = setting.Configuration;
        if (!InstanceTargetPathValidator.TryResolveTargetFile(
                requestedConfig.GetWorkingDirectory(),
                requestedConfig.Target,
                out _,
                out var targetError))
        {
            return ResultExt.Err<InstanceConfiguration>(targetError!);
        }

        using var reservation = ReserveInstanceCreation(requestedConfig.InstanceId);
        var reservedSetting = setting with
        {
            Configuration = InstanceConfigurationMapper.WithInstanceId(requestedConfig, reservation.InstanceId)
        };

        var reservedConfig = reservedSetting.Configuration;
        var instanceRoot = reservedConfig.GetWorkingDirectory();
        try
        {
            Directory.CreateDirectory(instanceRoot);
        }
        catch (Exception exception)
        {
            return FailCreate(new Error("Instance manager failed to create instance directory").CauseBy(exception));
        }

        Log.Information(
            "[InstanceManager] Running InstanceFactory({0}) for instance '{1}'",
            reservedConfig.InstanceType.ToString(),
            reservedConfig.Name);

        Result<InstanceConfiguration, Error> appliedFactoryResult;
        try
        {
            appliedFactoryResult = await _applyInstanceFactory(reservedSetting);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            FileManager.TryRemove(instanceRoot);
            throw;
        }
        catch (Exception exception)
        {
            return FailCreate(new Error("Instance manager failed to run instance factory").CauseBy(exception));
        }

        if (appliedFactoryResult.IsErr(out var factoryError))
            return FailCreate(new Error("Instance manager failed to run instance factory").WithInner(factoryError));

        try
        {
            ct.ThrowIfCancellationRequested();
            var reconciledConfig = InstanceVersionDetector.Reconcile(
                InstanceConfigurationMapper.ToInstanceConfig(appliedFactoryResult.Unwrap()),
                instanceRoot);
            reconciledConfig.Uuid = reservation.InstanceId;

            using var mutation = await AcquireInstanceMutationAsync(reconciledConfig.Uuid, ct);
            ct.ThrowIfCancellationRequested();

            var validation = reconciledConfig.ValidateConfig();
            if (validation.IsErr(out var validationError))
                return FailCreate(new Error("Instance manager received an invalid instance config").WithInner(validationError));

            FileManager.WriteJsonAndBackup(
                Path.Combine(instanceRoot, InstanceConfig.FileName),
                reconciledConfig);

            var instance = _instanceFactory(reconciledConfig);
            try
            {
                CommitCreatedInstance(reservation, instance);
            }
            catch
            {
                TryDisposeInstance(instance, reconciledConfig.Uuid, "created");
                throw;
            }

            return ResultExt.Ok(InstanceConfigurationMapper.ToContract(reconciledConfig));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            FileManager.TryRemove(instanceRoot);
            throw;
        }
        catch (Exception exception)
        {
            return FailCreate(Error.FromException(exception));
        }

        Result<InstanceConfiguration, Error> FailCreate(Error error)
        {
            Log.Error("[InstanceManager] Failed to create instance '{0}': \n{1}", reservedConfig.Name, error);
            FileManager.TryRemove(instanceRoot);
            return ResultExt.Err<InstanceConfiguration>(error);
        }
    }

    public async Task<bool> TryRemoveInstance(Guid instanceId, CancellationToken ct = default)
    {
        using var admission = _mutationAdmission.EnterExternal();
        using var mutation = await AcquireInstanceMutationAsync(instanceId, ct);
        IInstance removedInstance;
        string? removedDirectory;
        lock (_mutationLock)
        {
            if (RunningInstances.ContainsKey(instanceId))
                return false;

            if (!Instances.TryGetValue(instanceId, out var instance))
                return false;

            var instanceDirectory = Path.Combine(FileManager.InstancesRoot, instanceId.ToString());
            removedDirectory = null;
            try
            {
                if (Directory.Exists(instanceDirectory))
                {
                    removedDirectory = instanceDirectory + ".removing-" + Guid.NewGuid().ToString("N");
                    Directory.Move(instanceDirectory, removedDirectory);
                }
            }
            catch (Exception exception)
            {
                Log.Error("[InstanceManager] Failed to stage removal for instance '{0}': {1}", instanceId, exception);
                return false;
            }

            if (!Instances.TryRemove(instanceId, out var removed))
            {
                RestoreStagedDirectory(instanceDirectory, removedDirectory);
                return false;
            }

            removedInstance = removed;
            DetachInstance(removedInstance);
            _snapshotSource.Remove(instanceId);
        }

        try
        {
            removedInstance.Dispose();
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[InstanceManager] Failed to dispose removed instance '{InstanceId}'", instanceId);
        }

        if (removedDirectory is not null)
        {
            try
            {
                Directory.Delete(removedDirectory, true);
            }
            catch (Exception exception)
            {
                Log.Warning(
                    exception,
                    "[InstanceManager] Instance '{InstanceId}' was removed but staged storage cleanup failed at '{RemovalDirectory}'",
                    instanceId,
                    removedDirectory);
            }
        }

        Log.Information("[InstanceManager] Removed instance '{0}'", instanceId);
        return true;
    }

    public async Task<IInstance?> TryStartInstance(Guid instanceId, CancellationToken ct = default)
    {
        using var admission = _mutationAdmission.EnterExternal();
        ct.ThrowIfCancellationRequested();
        using var mutation = await AcquireInstanceMutationAsync(instanceId, ct);
        if (RunningInstances.ContainsKey(instanceId))
            return null;

        var target = Instances.GetValueOrDefault(instanceId);
        if (target is null)
            return null;

        try
        {
            AttachInstance(target);
            if (!RunningInstances.TryAdd(instanceId, target))
            {
                Log.Warning("[InstanceManager] Cannot start a already running instance(Uuid={0})", target.Config.Uuid);
                return null;
            }

            if (await target.StartAsync(ct: ct))
            {
                _snapshotSource.Upsert(target);
                return target;
            }

            RunningInstances.TryRemove(instanceId, out _);
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            RunningInstances.TryRemove(instanceId, out _);
            try
            {
                target.Process?.KillProcess();
            }
            catch (Exception exception)
            {
                Log.Warning(
                    exception,
                    "[InstanceManager] Failed to clean up canceled start for instance '{0}'",
                    target.Config.Uuid);
            }

            throw;
        }
        catch (Exception exception)
        {
            RunningInstances.TryRemove(instanceId, out _);
            target.Process?.KillProcess();
            Log.Error("[InstanceManager] Error occurred when starting instance '{0}': {1}", target.Config.Name, exception);
            return null;
        }
    }

    public async Task<bool> TryStopInstance(Guid instanceId, CancellationToken ct = default)
    {
        using var admission = _mutationAdmission.EnterExternal();
        using var mutation = await AcquireInstanceMutationAsync(instanceId, ct);
        if (!RunningInstances.TryRemove(instanceId, out var instance))
            return false;

        instance.Stop();
        // Graceful shutdown is intentionally asynchronous and status is updated by the process hook.
        return true;
    }

    public bool SendToInstance(Guid instanceId, string message)
    {
        if (!RunningInstances.TryGetValue(instanceId, out var instance))
            return false;

        instance.Process!.WriteLine(message);
        return true;
    }

    public void KillInstance(Guid instanceId)
    {
        using var admission = _mutationAdmission.EnterExternal();
        if (!Instances.TryGetValue(instanceId, out var instance))
            return;

        var process = instance.Process;
        process?.KillProcess();
    }

    public async Task<InstanceReport?> GetInstanceReport(Guid instanceId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!Instances.TryGetValue(instanceId, out var instance))
            return null;

        return await instance.GetReportAsync(ct);
    }

    public async Task<Dictionary<Guid, InstanceReport>> GetAllReports(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var tasks = Instances.ToDictionary(pair => pair.Key, pair => pair.Value.GetReportAsync(ct));
        await Task.WhenAll(tasks.Values);
        return tasks.ToDictionary(pair => pair.Key, pair => pair.Value.Result);
    }

    public bool TryGetInstanceLog(Guid instanceId, out IReadOnlyList<string> logs)
    {
        if (Instances.TryGetValue(instanceId, out var instance))
        {
            logs = instance.GetLogHistory();
            return true;
        }

        logs = [];
        return false;
    }

    public async Task<Result<InstanceSettingsResult, Error>> GetInstanceSettings(
        Guid instanceId,
        CancellationToken ct = default)
    {
        using var mutation = await AcquireInstanceMutationAsync(instanceId, ct);
        return _instanceUpdateCoordinator.GetInstanceSettings(instanceId);
    }

    public async Task<Result<UpdateInstanceSettingsResult, Error>> UpdateInstanceSettings(
        UpdateInstanceSettingsRequest request,
        CancellationToken ct = default)
    {
        using var admission = _mutationAdmission.EnterExternal();
        return await _instanceUpdateCoordinator.UpdateInstanceSettings(request, ct);
    }

    public async Task StopAllInstances(CancellationToken ct = default)
    {
        _ = ct;
        var failures = new List<Exception>();
        var processes = new HashSet<InstanceProcess>();
        var runningProcesses = new HashSet<InstanceProcess>();

        foreach (var instance in Instances.Values)
            TryCaptureProcess(instance, processes, failures);
        foreach (var instance in RunningInstances.Values)
        {
            TryCaptureProcess(instance, runningProcesses, failures);
            TryCaptureProcess(instance, processes, failures);
        }

        foreach (var process in runningProcesses)
        {
            try
            {
                process.WriteLine("stop");
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                Log.Warning(exception, "[InstanceManager] Failed to signal instance process shutdown");
            }
        }

        try
        {
            await Task.WhenAll(processes.Select(static process => process.WaitForExitAsync(CancellationToken.None)));
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (failures.Count != 0)
            throw new AggregateException("One or more instance shutdown operations failed.", failures);
    }

    internal void DetachInstanceEventProducers()
    {
        lock (_mutationLock)
        {
            foreach (var instance in Instances.Values)
                DetachInstance(instance);
        }
    }

    private static void TryCaptureProcess(
        IInstance instance,
        ISet<InstanceProcess> processes,
        ICollection<Exception> failures)
    {
        try
        {
            if (instance.Process is { } process)
                processes.Add(process);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    internal void ReplaceInstance(Guid instanceId, IInstance replacement)
    {
        using var admission = _mutationAdmission.EnterExternal();
        ReplaceInstanceWithinAdmission(instanceId, replacement);
    }

    internal void ReplaceInstanceWithinAdmission(Guid instanceId, IInstance replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        IInstance? replacedInstance = null;

        lock (_mutationLock)
        {
            var hasExisting = Instances.TryGetValue(instanceId, out var existing);
            AttachInstance(replacement);
            Instances[instanceId] = replacement;
            try
            {
                _snapshotSource.Upsert(replacement);
            }
            catch
            {
                DetachInstance(replacement);
                if (hasExisting)
                    Instances[instanceId] = existing!;
                else
                    Instances.TryRemove(instanceId, out _);

                throw;
            }

            if (hasExisting)
            {
                DetachInstance(existing!);
                replacedInstance = existing;
            }
        }

        if (replacedInstance is not null)
            TryDisposeInstance(replacedInstance, instanceId, "replaced");
    }

    private void CommitCreatedInstance(InstanceCreationReservation reservation, IInstance instance)
    {
        var instanceId = reservation.InstanceId;
        lock (_mutationLock)
        {
            if (!_creationReservations.TryGetValue(instanceId, out var activeReservation) ||
                !ReferenceEquals(activeReservation, reservation))
            {
                throw new InvalidOperationException(
                    $"Instance '{instanceId}' no longer has an active creation reservation.");
            }

            if (!Instances.TryAdd(instanceId, instance))
                throw new InvalidOperationException($"Instance '{instanceId}' already exists.");

            var attached = false;
            try
            {
                AttachInstance(instance);
                attached = true;
                _snapshotSource.Upsert(instance);
            }
            catch
            {
                if (attached)
                {
                    try
                    {
                        DetachInstance(instance);
                    }
                    catch (Exception exception)
                    {
                        Log.Warning(
                            exception,
                            "[InstanceManager] Failed to detach uncommitted instance '{InstanceId}'",
                            instanceId);
                    }
                }

                Instances.TryRemove(instanceId, out _);
                throw;
            }
        }
    }

    /// <summary>
    /// Reserves an instance identifier before any directory or factory side effects occur. The
    /// reservation remains active through cleanup so a second create cannot share its storage.
    /// </summary>
    private InstanceCreationReservation ReserveInstanceCreation(Guid requestedInstanceId)
    {
        lock (_mutationLock)
        {
            var instanceId = requestedInstanceId;
            var wasReallocated = false;
            while (Instances.ContainsKey(instanceId) ||
                   _creationReservations.ContainsKey(instanceId) ||
                   IsInstanceStorageOccupied(instanceId))
            {
                instanceId = Guid.NewGuid();
                wasReallocated = true;
            }

            if (wasReallocated)
            {
                Log.Warning(
                    "[InstanceManager] Instance '{0}' is already committed, being created, or has existing storage; allocating a new uuid",
                    requestedInstanceId);
            }

            var reservation = new InstanceCreationReservation(this, instanceId);
            _creationReservations.Add(instanceId, reservation);
            return reservation;
        }
    }

    private void ReleaseInstanceCreationReservation(InstanceCreationReservation reservation)
    {
        lock (_mutationLock)
        {
            if (_creationReservations.TryGetValue(reservation.InstanceId, out var activeReservation) &&
                ReferenceEquals(activeReservation, reservation))
            {
                _creationReservations.Remove(reservation.InstanceId);
            }
        }
    }

    private static bool IsInstanceStorageOccupied(Guid instanceId)
    {
        var instanceRoot = Path.Combine(FileManager.InstancesRoot, instanceId.ToString());
        return Directory.Exists(instanceRoot) || File.Exists(instanceRoot);
    }

    private static void TryDisposeInstance(IInstance instance, Guid instanceId, string operation)
    {
        try
        {
            instance.Dispose();
        }
        catch (Exception exception)
        {
            Log.Error(
                exception,
                "[InstanceManager] Failed to dispose {Operation} instance '{InstanceId}'",
                operation,
                instanceId);
        }
    }

    /// <summary>
    /// Serializes daemon-internal mutations for one instance. Callers must acquire this gate
    /// before taking <see cref="_mutationLock"/>; no code may wait for this gate while holding
    /// that lock.
    /// </summary>
    public IDisposable AcquireInstanceMutation(Guid instanceId)
    {
        var gate = _instanceMutationGates.GetOrAdd(instanceId, static _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        return new InstanceMutationLease(gate);
    }

    /// <summary>
    /// Asynchronously serializes daemon-internal mutations for one instance.
    /// </summary>
    public async ValueTask<IDisposable> AcquireInstanceMutationAsync(
        Guid instanceId,
        CancellationToken ct = default)
    {
        var gate = _instanceMutationGates.GetOrAdd(instanceId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return new InstanceMutationLease(gate);
    }

    private void AttachInstance(IInstance instance)
    {
        instance.OnLog -= OnInstanceLogAsync;
        instance.OnLog += OnInstanceLogAsync;
        instance.OnStatusChanged -= OnInstanceStatusChangedAsync;
        instance.OnStatusChanged += OnInstanceStatusChangedAsync;
    }

    private void DetachInstance(IInstance instance)
    {
        instance.OnLog -= OnInstanceLogAsync;
        instance.OnStatusChanged -= OnInstanceStatusChangedAsync;
    }

    private Task OnInstanceLogAsync(Guid instanceId, string log, CancellationToken cancellationToken)
    {
        return InvokeAsync(InstanceLogReceived, instanceId, log, cancellationToken);
    }

    private async Task OnInstanceStatusChangedAsync(
        Guid instanceId,
        InstanceStatus status,
        CancellationToken cancellationToken)
    {
        if (!_mutationAdmission.TryEnterProducer(out var admission))
            return;
        using (admission)
        {
            Log.Debug("[InstanceManager] Instance '{0}' status changed to {1}", instanceId, status.ToString());

            lock (_mutationLock)
            {
                if (status.IsStoppedOrCrashed())
                    RunningInstances.TryRemove(instanceId, out _);

                if (Instances.TryGetValue(instanceId, out var instance))
                    _snapshotSource.Upsert(instance);
            }

            await InvokeAsync(InstanceStatusChanged, instanceId, status, cancellationToken);
        }
    }

    private static async Task InvokeAsync<T>(
        Func<Guid, T, CancellationToken, Task>? handlers,
        Guid instanceId,
        T value,
        CancellationToken cancellationToken)
    {
        if (handlers is null)
            return;

        foreach (var handler in handlers.GetInvocationList().Cast<Func<Guid, T, CancellationToken, Task>>())
            await handler(instanceId, value, cancellationToken);
    }

    private void ReinitializeSnapshotSource()
    {
        _snapshotSource = new AuthoritativeInstanceSnapshotSource(Instances, _catalogCommitFeed);
    }

    private static void RestoreStagedDirectory(string instanceDirectory, string? removedDirectory)
    {
        if (removedDirectory is null || !Directory.Exists(removedDirectory))
            return;

        try
        {
            Directory.Move(removedDirectory, instanceDirectory);
        }
        catch (Exception exception)
        {
            Log.Error(
                "[InstanceManager] Failed to restore staged instance directory '{0}': {1}",
                removedDirectory,
                exception);
        }
    }

    internal sealed class InstanceMutationLease : IDisposable
    {
        private SemaphoreSlim? _gate;

        internal InstanceMutationLease(SemaphoreSlim gate)
        {
            _gate = gate;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _gate, null)?.Release();
        }
    }

    private sealed class InstanceCreationReservation : IDisposable
    {
        private InstanceManager? _owner;

        internal InstanceCreationReservation(InstanceManager owner, Guid instanceId)
        {
            _owner = owner;
            InstanceId = instanceId;
        }

        internal Guid InstanceId { get; }

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.ReleaseInstanceCreationReservation(this);
        }
    }

    internal static IInstanceManager Create()
    {
        return Create(Directory.GetDirectories(FileManager.InstancesRoot, "*", SearchOption.TopDirectoryOnly));
    }

    internal static IInstanceManager Create(IEnumerable<string> directories)
    {
        ArgumentNullException.ThrowIfNull(directories);
        var instanceManager = new InstanceManager();

        foreach (var directory in directories)
        {
            try
            {
                var dir = new DirectoryInfo(directory);
                var serverConfig = dir.GetFiles(InstanceConfig.FileName, SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (serverConfig is null)
                    continue;

                var config = FileManager.ReadJson<InstanceConfig>(serverConfig.FullName)!;
                var reconciledConfig = InstanceVersionDetector.Reconcile(config, dir.FullName);
                if (!InstanceTargetPathValidator.TryResolveTargetFile(
                        reconciledConfig.GetWorkingDirectory(),
                        reconciledConfig.Target,
                        out _,
                        out var targetError))
                {
                    Log.Warning(
                        "[InstanceManager] Ignored instance configuration at '{0}' with an invalid target: {1}",
                        serverConfig.FullName,
                        targetError!.Cause);
                    continue;
                }

                if (!ReferenceEquals(reconciledConfig, config) &&
                    (reconciledConfig.InstanceType != config.InstanceType || reconciledConfig.Version != config.Version))
                {
                    var persist = ResultExt.Try(static state =>
                    {
                        var (path, cfg) = state;
                        FileManager.WriteJsonAndBackup(path, cfg);
                    }, (serverConfig.FullName, reconciledConfig));

                    if (persist.IsErr(out var persistError))
                    {
                        Log.Debug(
                            "[InstanceManager] Failed to persist reconciled config at '{0}', ignored: {1}",
                            serverConfig.FullName,
                            persistError?.Message ?? "unknown error");
                    }
                }

                var instance = reconciledConfig.CreateInstance();
                if (instanceManager.Instances.TryAdd(reconciledConfig.Uuid, instance))
                {
                    instanceManager.AttachInstance(instance);
                    Log.Debug("[InstanceManager] Loaded instance '{0}'({1})", reconciledConfig.Name, reconciledConfig.Uuid);
                }
                else
                {
                    instance.Dispose();
                    Log.Warning("[InstanceManager] Ignored duplicate instance '{0}'", reconciledConfig.Uuid);
                }
            }
            catch (Exception exception)
            {
                Log.Error(
                    "[InstanceManager] Failed to load instance at '{0}', ignored: {1}",
                    Path.Combine(FileManager.InstancesRoot, directory),
                    exception);
            }
        }

        instanceManager.ReinitializeSnapshotSource();
        Log.Debug("[InstanceManager] Loaded {0} instances.", instanceManager.Instances.Count);

        return instanceManager;
    }
}
