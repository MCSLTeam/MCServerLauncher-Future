using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.API.State;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.ApplicationCore.Auth;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.Plugins.Configuration;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Events;
using Microsoft.Extensions.Logging;
using RustyOptions;

namespace MCServerLauncher.Daemon.Plugins;

internal enum PluginRuntimeState
{
    Discovered,
    Admitted,
    Configured,
    Validated,
    Started,
    Committed,
    Active,
    Stopping,
    Stopped,
    Failed
}

internal sealed class PluginHost
{
    internal const string HostApiVersion = "2.0.0";
    private static readonly TimeSpan DefaultStartTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RollbackCleanupTimeout = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly IInstanceSnapshotSource _instances;
    private readonly ISystemApplication? _system;
    private readonly CallerContextFactory _callerContexts;
    private readonly VerifiedPrincipalAuthority _verifiedPrincipals;
    private readonly IInstanceApplication? _instanceApplication;
    private readonly IOperationApplication? _operationApplication;
    private readonly IProvisioningApplication? _provisioningApplication;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PluginHost> _logger;
    private readonly IPluginEventBus _eventBus;
    private readonly string _pluginsRoot;
    private readonly TimeSpan _startTimeout;
    private readonly DaemonPluginsConfig _pluginsConfig;
    private readonly PluginAdmissionPreflight _preflight;
    private readonly PluginHttpEndpointRegistry _httpEndpoints;
    private readonly List<PluginRuntime> _runtimes = [];
    private readonly List<PluginRuntime> _started = [];
    private readonly OwnedTaskSupervisor _failedStartCleanupSupervisor;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private bool _prepared;
    private bool _catalogAdmissionComplete;
    private bool _stopping;

    public PluginHost(
        IInstanceSnapshotSource instances,
        ILoggerFactory loggerFactory,
        ILogger<PluginHost> logger,
        IPluginEventBus eventBus)
        : this(
            instances,
            loggerFactory,
            logger,
            Path.Combine(AppContext.BaseDirectory, "plugins"),
            eventBus,
            DefaultStartTimeout,
            DaemonPluginsConfig.Default,
            new PluginHttpEndpointRegistry(),
            system: null)
    {
    }

    internal PluginHost(
        IInstanceSnapshotSource instances,
        ILoggerFactory loggerFactory,
        ILogger<PluginHost> logger,
        string pluginsRoot,
        IPluginEventBus eventBus)
        : this(
            instances,
            loggerFactory,
            logger,
            pluginsRoot,
            eventBus,
            DefaultStartTimeout,
            DaemonPluginsConfig.Default,
            new PluginHttpEndpointRegistry(),
            system: null)
    {
    }

    internal PluginHost(
        IInstanceSnapshotSource instances,
        ILoggerFactory loggerFactory,
        ILogger<PluginHost> logger,
        string pluginsRoot,
        IPluginEventBus eventBus,
        TimeSpan startTimeout)
        : this(
            instances,
            loggerFactory,
            logger,
            pluginsRoot,
            eventBus,
            startTimeout,
            DaemonPluginsConfig.Default,
            new PluginHttpEndpointRegistry(),
            system: null)
    {
    }

    internal PluginHost(
        IInstanceSnapshotSource instances,
        ILoggerFactory loggerFactory,
        ILogger<PluginHost> logger,
        string pluginsRoot,
        IPluginEventBus eventBus,
        TimeSpan startTimeout,
        DaemonPluginsConfig pluginsConfig,
        PluginHttpEndpointRegistry httpEndpoints,
        ISystemApplication? system = null,
        ICallerContextFactory? callerContexts = null,
        IInstanceApplication? instanceApplication = null,
        IOperationApplication? operationApplication = null,
        IProvisioningApplication? provisioningApplication = null,
        PluginAdmissionPreflight? preflight = null,
        VerifiedPrincipalAuthority? verifiedPrincipals = null)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _failedStartCleanupSupervisor = new OwnedTaskSupervisor(nameof(PluginHost), _logger);
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsRoot);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(startTimeout, TimeSpan.Zero);
        _pluginsRoot = Path.GetFullPath(pluginsRoot);
        _startTimeout = startTimeout;
        _pluginsConfig = pluginsConfig ?? DaemonPluginsConfig.Default;
        _preflight = preflight ?? PluginAdmissionPreflight.CreateNonInteractive(_pluginsConfig);
        _httpEndpoints = httpEndpoints ?? new PluginHttpEndpointRegistry();
        _system = system;
        if (callerContexts is not null && callerContexts is not CallerContextFactory)
        {
            throw new ArgumentException(
                "PluginHost requires the daemon caller-context factory so verified principal provenance remains enforceable.",
                nameof(callerContexts));
        }

        var daemonCallerContexts = (CallerContextFactory?)callerContexts;
        _verifiedPrincipals = verifiedPrincipals
            ?? daemonCallerContexts?.VerifiedPrincipals
            ?? new VerifiedPrincipalAuthority();
        if (daemonCallerContexts is not null &&
            !ReferenceEquals(daemonCallerContexts.VerifiedPrincipals, _verifiedPrincipals))
        {
            throw new ArgumentException(
                "The caller-context factory and principal authority must belong to the same host.",
                nameof(callerContexts));
        }

        _callerContexts = daemonCallerContexts ?? new CallerContextFactory(_verifiedPrincipals);
        _instanceApplication = instanceApplication;
        _operationApplication = operationApplication;
        _provisioningApplication = provisioningApplication;
    }

    /// <summary>
    /// HTTP endpoint registry shared with the daemon listener setup so plugin listeners can
    /// reserve IP:port against the daemon's own /api/v2 port before any plugin opens a socket.
    /// </summary>
    internal PluginHttpEndpointRegistry HttpEndpoints => _httpEndpoints;


    internal ImmutableArray<(string Id, PluginRuntimeState State)> States
    {
        get
        {
            lock (_gate)
                return _runtimes.Select(static runtime => (runtime.Manifest.Identity.Id, runtime.State)).ToImmutableArray();
        }
    }

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_prepared)
                return;

            var discovery = new PluginDiscovery(HostApiVersion).Discover(_pluginsRoot);
            foreach (var failure in discovery.Failures)
                _logger.LogError(failure.Exception, "Skipping plugin bundle {Bundle}: {Code} {Message}", failure.BundleDirectory, failure.Code, failure.Message);

            // Complete admission for every bundle before loading any entry assembly. This keeps
            // policy prompts and permanent-decision persistence independent from plugin DI and code.
            var admitted = new List<PluginManifest>();
            foreach (var manifest in discovery.Plugins)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    // Generated metadata agreement precedes policy prompts and permanent admission
                    // writes. PE metadata decoding does not load the assembly or execute plugin IL.
                    GeneratedPluginMetadataReader.Validate(manifest);
                }
                catch (PluginManifestException exception)
                {
                    _logger.LogError(
                        exception,
                        "Plugin {PluginId} failed generated metadata preflight ({Code}): {Message} Skipping bundle.",
                        manifest.Identity.Id,
                        exception.Code,
                        exception.Message);
                    continue;
                }

                var outcome = _preflight.Evaluate(manifest);
                if (!outcome.IsAdmitted)
                {
                    _logger.LogWarning(
                        "Plugin {PluginId} failed preflight ({Code}): {Message} Skipping bundle.",
                        manifest.Identity.Id,
                        outcome.Code,
                        outcome.Message);
                    continue;
                }

                admitted.Add(manifest);
            }

            foreach (var manifest in admitted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var runtime = await TryLoadAsync(manifest).ConfigureAwait(false);
                if (runtime is null)
                    continue;

                runtime.State = PluginRuntimeState.Admitted;
                _runtimes.Add(runtime);
                await ConfigureAsync(runtime).ConfigureAwait(false);
            }

            await ValidateGlobalDraftsAsync().ConfigureAwait(false);
            foreach (var runtime in _runtimes.Where(static runtime => runtime.State == PluginRuntimeState.Validated))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await StartPluginAsync(runtime, cancellationToken).ConfigureAwait(false);
            }

            _prepared = true;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    internal void AddCatalogContributions(ProtocolCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        lock (_gate)
        {
            if (!_prepared)
                throw new InvalidOperationException("Plugin host startup must complete before catalog admission.");

            if (_catalogAdmissionComplete)
                throw new InvalidOperationException("Plugin catalog admission has already completed.");

            foreach (var runtime in _started)
            {
                if (runtime.State != PluginRuntimeState.Started)
                    throw new InvalidOperationException($"Plugin '{runtime.Manifest.Identity.Id}' is not in the Started state during catalog admission.");

                // Drafts were fully validated and globally admitted before any StartAsync call.
                // An exception here is a host invariant failure, not a recoverable plugin conflict;
                // the catalog builder cannot roll back a partially-added registration.
                runtime.Draft.AddTo(builder);
                runtime.State = PluginRuntimeState.Committed;
            }

            _catalogAdmissionComplete = true;
        }
    }

    internal void Activate(FrozenProtocolCatalog catalog, V2RemoteEventBridge remoteEvents)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(remoteEvents);
        lock (_gate)
        {
            if (!_catalogAdmissionComplete)
                throw new InvalidOperationException("Plugin catalog admission must complete before activation.");

            foreach (var runtime in _started)
            {
                if (runtime.State != PluginRuntimeState.Committed)
                    throw new InvalidOperationException($"Plugin '{runtime.Manifest.Identity.Id}' is not Committed during activation.");

                runtime.Draft.Attach(catalog, remoteEvents, runtime.EventOwner);
                runtime.State = PluginRuntimeState.Active;
                runtime.Activation.TrySetResult();
            }
        }
    }

    internal async Task StopAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (_stopping)
                    return;
                _stopping = true;
            }

            foreach (var runtime in _started.AsEnumerable().Reverse())
            {
                if (runtime.State is not (PluginRuntimeState.Active or PluginRuntimeState.Committed or PluginRuntimeState.Started))
                    continue;

                runtime.State = PluginRuntimeState.Stopping;
                CloseHttpEndpointAdmission(runtime);
                CancelLifetime(runtime, "stop");
                // Stop accepting plugin-originated events before invoking plugin code.
                runtime.Draft.Clear();
                runtime.EventOwner.Dispose(_logger, runtime.Manifest.Identity.Id);
                using var stopTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                stopTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    var result = await runtime.Plugin.StopAsync(stopTimeout.Token)
                        .WaitAsync(stopTimeout.Token)
                        .ConfigureAwait(false);
                    if (result.IsErr(out var error))
                        LogReturnedError(runtime, "stop", error!);
                }
                catch (OperationCanceledException) when (stopTimeout.IsCancellationRequested)
                {
                    _logger.LogError("Plugin {PluginId} stop timed out.", runtime.Manifest.Identity.Id);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Plugin {PluginId} stop threw an unexpected exception.", runtime.Manifest.Identity.Id);
                }
                finally
                {
                    runtime.Activation.TrySetCanceled();
                    if (await DisposePluginAsync(runtime).ConfigureAwait(false))
                        ReleaseHttpEndpoints(runtime);
                    DisposeLifetime(runtime);
                    runtime.State = PluginRuntimeState.Stopped;
                }
            }

            foreach (var runtime in _runtimes.Where(static runtime => runtime.State is PluginRuntimeState.Configured or PluginRuntimeState.Validated or PluginRuntimeState.Admitted))
            {
                CloseHttpEndpointAdmission(runtime);
                CancelLifetime(runtime, "cleanup");
                runtime.Draft.Clear();
                runtime.EventOwner.Dispose(_logger, runtime.Manifest.Identity.Id);
                runtime.Activation.TrySetCanceled();
                if (await DisposePluginAsync(runtime).ConfigureAwait(false))
                    ReleaseHttpEndpoints(runtime);
                DisposeLifetime(runtime);
                runtime.State = PluginRuntimeState.Stopped;
            }

            // Timed-out starts are not part of _started, but their Stop/Dispose work is still
            // daemon-owned. Drain it before shutdown returns so it cannot outlive the host.
            await _failedStartCleanupSupervisor.DrainAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The daemon plugin product is an untrimmed JIT host and resolves the manifest entry type at startup.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072",
        Justification = "The daemon plugin product is an untrimmed JIT host and requires a public parameterless plugin constructor.")]
    private async Task<PluginRuntime?> TryLoadAsync(PluginManifest manifest)
    {
        IDaemonPlugin? plugin = null;
        CancellationTokenSource? lifetime = null;
        PluginHttpEndpointPolicy? httpPolicy = null;
        var ownershipTransferred = false;
        try
        {
            // Load only the immutable bytes whose metadata was validated. Dependency resolution
            // remains rooted at the bundle path, but replacing that path cannot change entry IL.
            var entryAssemblyImage = GeneratedPluginMetadataReader.ReadValidatedImage(manifest);
            var loadContext = new PluginLoadContext(manifest.EntryAssemblyPath, manifest.Identity.Id);
            var assembly = loadContext.LoadEntryAssembly(entryAssemblyImage);
            var pluginType = assembly.GetType(manifest.EntryType, throwOnError: true, ignoreCase: false);
            if (pluginType is null || !typeof(IGeneratedDaemonPluginAdapter).IsAssignableFrom(pluginType))
            {
                throw new InvalidOperationException(
                    $"Entry type '{manifest.EntryType}' does not implement the generated adapter contract.");
            }

            if (Activator.CreateInstance(pluginType) is not IDaemonPlugin createdPlugin)
                throw new InvalidOperationException($"Entry type '{manifest.EntryType}' could not be constructed.");
            plugin = createdPlugin;

            var owner = ProtocolExecutionOwner.ForPlugin(
                new ProtocolOwnerIdentity(manifest.Identity.Id, manifest.Identity.Version));
            var errors = new PluginErrorFactory(manifest.Identity);
            var pluginLogger = _loggerFactory.CreateLogger($"Plugin.{manifest.Identity.Id}");
            var draft = new PluginRegistrationDraft(manifest, owner, errors, _eventBus, pluginLogger);
            lifetime = new CancellationTokenSource();
            var activation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var eventOwner = new PluginEventOwnerLedger();

            PluginConfiguration configuration;
            try
            {
                configuration = new PluginConfiguration(manifest.BundleDirectory, errors);
            }
            catch (PluginManifestException exception)
            {
                throw new InvalidOperationException(exception.Message, exception);
            }

            IPluginPrivateStorage? storage = manifest.HasFeature(PluginFeature.StoragePrivate)
                ? new PluginPrivateStorage(manifest.Identity, _pluginsConfig, errors)
                : null;
            httpPolicy = manifest.HasFeature(PluginFeature.NetworkHttpListen)
                ? new PluginHttpEndpointPolicy(manifest.Identity.Id, _httpEndpoints, errors)
                : null;
            IPluginAuthentication? authentication = manifest.HasFeature(PluginFeature.AuthVerify)
                ? new PluginAuthentication(errors, _verifiedPrincipals)
                : null;
            ISystemQueryApplication? system = null;
            if (manifest.HasFeature(PluginFeature.SystemQuery))
            {
                if (_system is null)
                {
                    throw new InvalidOperationException(
                        "Plugin declared system.query but the host was constructed without ISystemApplication.");
                }

                system = new PluginSystemQueryApplication(_system);
            }

            var applications = new PluginApplicationAuthorizer(
                manifest.Identity,
                manifest.Features.Select(static feature => feature.Value),
                _callerContexts,
                manifest.HasFeature(PluginFeature.InstanceQuery) ? _instances : null,
                manifest.HasFeature(PluginFeature.InstanceQuery) ? _instanceApplication : null,
                system,
                manifest.HasFeature(PluginFeature.InstanceManage) ? _instanceApplication : null,
                manifest.HasFeature(PluginFeature.OperationQuery) ? _operationApplication : null,
                manifest.HasFeature(PluginFeature.OperationCancel) ? _operationApplication : null,
                manifest.HasFeature(PluginFeature.ProvisioningManage) ? _provisioningApplication : null);
            var context = new PluginContext(
                manifest.Identity,
                pluginLogger,
                errors,
                draft,
                applications,
                configuration,
                storage,
                httpPolicy,
                authentication,
                activation.Task,
                lifetime.Token);
            var runtime = new PluginRuntime(
                manifest,
                loadContext,
                plugin,
                context,
                draft,
                lifetime,
                activation,
                eventOwner,
                httpPolicy);
            ownershipTransferred = true;
            return runtime;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load plugin {PluginId}; skipping bundle.", manifest.Identity.Id);
            return null;
        }
        finally
        {
            if (!ownershipTransferred)
                await CleanupFailedLoadAsync(manifest, plugin, lifetime, httpPolicy).ConfigureAwait(false);
        }
    }

    private async Task CleanupFailedLoadAsync(
        PluginManifest manifest,
        IDaemonPlugin? plugin,
        CancellationTokenSource? lifetime,
        PluginHttpEndpointPolicy? httpPolicy)
    {
        httpPolicy?.Close();
        if (lifetime is not null)
        {
            try
            {
                await lifetime.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Plugin {PluginId} lifetime cancellation failed during load cleanup; continuing cleanup.",
                    manifest.Identity.Id);
            }
        }

        var disposeCompleted = true;
        if (plugin is not null)
        {
            try
            {
                switch (plugin)
                {
                    case IAsyncDisposable asyncDisposable:
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                        break;
                    case IDisposable disposable:
                        disposable.Dispose();
                        break;
                }
            }
            catch (Exception exception)
            {
                disposeCompleted = false;
                _logger.LogError(
                    exception,
                    "Plugin {PluginId} dispose threw during load cleanup; continuing cleanup.",
                    manifest.Identity.Id);
            }
        }

        if (disposeCompleted)
            httpPolicy?.ReleaseAll();
        lifetime?.Dispose();
    }

    private async Task ConfigureAsync(PluginRuntime runtime)
    {
        try
        {
            var result = runtime.Plugin.Configure(runtime.Context);
            runtime.Draft.Close();
            if (result.IsErr(out var error))
            {
                await FailAsync(runtime, "configure_returned_error", "Plugin Configure returned an error.", error!)
                    .ConfigureAwait(false);
                return;
            }

            runtime.State = PluginRuntimeState.Configured;
            runtime.Draft.Validate();
            runtime.State = PluginRuntimeState.Validated;
        }
        catch (Exception exception)
        {
            await FailAsync(runtime, "configure_threw", "Plugin Configure threw an unexpected exception.", exception)
                .ConfigureAwait(false);
        }
        finally
        {
            runtime.Draft.Close();
        }
    }

    private async Task ValidateGlobalDraftsAsync()
    {
        var candidates = _runtimes
            .Where(static runtime => runtime.State == PluginRuntimeState.Validated)
            .ToArray();
        var builtInNames = BuiltInProtocolDefinitions.Rpcs
            .Select(static descriptor => descriptor.Method.Value)
            .Concat(BuiltInProtocolDefinitions.Events.Select(static descriptor => descriptor.Name.Value))
            .ToHashSet(StringComparer.Ordinal);
        var conflicts = new HashSet<PluginRuntime>();
        var names = candidates
            .SelectMany(runtime => runtime.Draft.WireNames.Select(name => (runtime, name)))
            .GroupBy(static registration => registration.name, StringComparer.Ordinal);

        foreach (var group in names)
        {
            var owners = group.Select(static registration => registration.runtime).Distinct().ToArray();
            if (builtInNames.Contains(group.Key) || owners.Length > 1)
            {
                foreach (var owner in owners)
                    conflicts.Add(owner);
            }
        }

        foreach (var runtime in conflicts.OrderBy(static runtime => runtime.Manifest.Identity.Id, StringComparer.Ordinal))
        {
            await FailAsync(
                runtime,
                "catalog_conflict",
                "The plugin registration draft conflicts with a built-in or another plugin protocol name.")
                .ConfigureAwait(false);
        }
    }

    private async Task StartPluginAsync(PluginRuntime runtime, CancellationToken cancellationToken)
    {
        var startCancellation = new CancellationTokenSource();
        var disposeStartCancellation = true;
        var startTask = Task.Factory.StartNew(
                () => runtime.Plugin.StartAsync(startCancellation.Token),
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                TaskScheduler.Default)
            .Unwrap();
        try
        {
            // Keep the supervisor timer independent from the token supplied to plugin code. A
            // plugin can register a blocking callback on that token; CancelAsync notifies it in
            // the background, while this deadline still returns daemon startup promptly.
            var startDeadline = Task.Delay(_startTimeout);
            var hostCancellation = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            var lifetimeCancellation = Task.Delay(Timeout.InfiniteTimeSpan, runtime.Lifetime.Token);
            var completed = await Task.WhenAny(
                    startTask,
                    startDeadline,
                    hostCancellation,
                    lifetimeCancellation)
                .ConfigureAwait(false);
            if (!ReferenceEquals(completed, startTask))
            {
                ObserveLateStartFault(runtime, startTask);
                CancelStartAndDisposeLater(runtime, startCancellation);
                disposeStartCancellation = false;
                if (ReferenceEquals(completed, startDeadline))
                {
                    // Listener readiness is governed by the StartAsync deadline. Revoke all
                    // host-owned admissions immediately, then supervise plugin Stop/Dispose in
                    // the background while endpoint ownership remains fail-closed until disposal.
                    FailWithoutDisposal(
                        runtime,
                        "start_timed_out",
                        $"Plugin StartAsync exceeded the startup deadline of {_startTimeout}.");
                    ScheduleFailedStartCleanup(runtime);
                    return;
                }

                // Roll back plugin-owned resources (e.g. a Kestrel listener bound during Start)
                // BEFORE declaring failure: Fail sets the state to Failed, and the rollback guard
                // would otherwise return immediately, leaving the listener serving.
                await StopRollbackAsync(runtime).ConfigureAwait(false);
                await FailStartCancellationAsync(runtime, cancellationToken).ConfigureAwait(false);
                return;
            }

            var result = await startTask.ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested || runtime.Lifetime.IsCancellationRequested)
            {
                ObserveLateStartFault(runtime, startTask);
                await StopRollbackAsync(runtime).ConfigureAwait(false);
                await FailStartCancellationAsync(runtime, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (result.IsErr(out var error))
            {
                await StopRollbackAsync(runtime).ConfigureAwait(false);
                await FailAsync(runtime, "start_returned_error", "Plugin StartAsync returned an error.", error!)
                    .ConfigureAwait(false);
                return;
            }

            runtime.State = PluginRuntimeState.Started;
            _started.Add(runtime);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested || runtime.Lifetime.IsCancellationRequested)
        {
            ObserveLateStartFault(runtime, startTask);
            await StopRollbackAsync(runtime).ConfigureAwait(false);
            await FailStartCancellationAsync(runtime, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await StopRollbackAsync(runtime).ConfigureAwait(false);
            await FailAsync(runtime, "start_threw", "Plugin StartAsync threw an unexpected exception.", exception)
                .ConfigureAwait(false);
        }
        finally
        {
            if (disposeStartCancellation)
                startCancellation.Dispose();
        }
    }

    private async Task FailStartCancellationAsync(PluginRuntime runtime, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            await FailAsync(runtime, "start_canceled", "Plugin StartAsync was canceled before it completed.")
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (runtime.Lifetime.IsCancellationRequested)
        {
            await FailAsync(runtime, "start_canceled", "Plugin StartAsync was canceled before it completed.")
                .ConfigureAwait(false);
            return;
        }

        await FailAsync(
            runtime,
            "start_timed_out",
            $"Plugin StartAsync exceeded the startup deadline of {_startTimeout}.")
            .ConfigureAwait(false);
    }

    private void ObserveLateStartFault(PluginRuntime runtime, Task startTask)
    {
        _ = startTask.ContinueWith(
            static (task, state) =>
            {
                var (logger, pluginId) = ((ILogger<PluginHost> Logger, string PluginId))state!;
                logger.LogWarning(
                    task.Exception,
                    "Plugin {PluginId} StartAsync faulted after startup supervision had already ended.",
                    pluginId);
            },
            (_logger, runtime.Manifest.Identity.Id),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void CancelStartAndDisposeLater(PluginRuntime runtime, CancellationTokenSource startCancellation)
    {
        try
        {
            var cancellation = startCancellation.CancelAsync();
            if (cancellation.IsCompleted)
            {
                LogStartCancellationFailure(runtime, cancellation);
                startCancellation.Dispose();
                return;
            }

            _ = cancellation.ContinueWith(
                static (task, state) =>
                {
                    var (host, pluginRuntime, source) =
                        ((PluginHost Host, PluginRuntime Runtime, CancellationTokenSource Source))state!;
                    host.LogStartCancellationFailure(pluginRuntime, task);
                    source.Dispose();
                },
                (this, runtime, startCancellation),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Plugin {PluginId} startup cancellation could not be supervised after the deadline.",
                runtime.Manifest.Identity.Id);
            startCancellation.Dispose();
        }
    }

    private void LogStartCancellationFailure(PluginRuntime runtime, Task cancellation)
    {
        if (cancellation.Exception is not { } exception)
            return;

        _logger.LogWarning(
            exception,
            "Plugin {PluginId} startup cancellation callbacks failed after the deadline.",
            runtime.Manifest.Identity.Id);
    }

    private async Task StopRollbackAsync(PluginRuntime runtime)
    {
        // Best-effort: give the plugin a bounded chance to StopAsync so it can release plugin-owned
        // resources (Kestrel, file handles) before the host marks it failed. A plugin that ignores
        // cancellation is bounded by the stop deadline; failures here are logged, never rethrown.
        if (runtime.State == PluginRuntimeState.Stopped)
            return;

        CloseHttpEndpointAdmission(runtime);
        using var stopTimeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
        stopTimeout.CancelAfter(RollbackCleanupTimeout);
        try
        {
            var result = await runtime.Plugin.StopAsync(stopTimeout.Token)
                .WaitAsync(stopTimeout.Token)
                .ConfigureAwait(false);
            if (result.IsErr(out var error))
                LogReturnedError(runtime, "rollback", error!);
        }
        catch (OperationCanceledException) when (stopTimeout.IsCancellationRequested)
        {
            _logger.LogWarning("Plugin {PluginId} rollback stop timed out.", runtime.Manifest.Identity.Id);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Plugin {PluginId} rollback stop threw; continuing cleanup.", runtime.Manifest.Identity.Id);
        }
    }

    private void ScheduleFailedStartCleanup(PluginRuntime runtime)
    {
        if (Interlocked.Exchange(ref runtime.FailedStartCleanupScheduled, 1) != 0)
            return;

        _failedStartCleanupSupervisor.Schedule(
            $"failed-start-cleanup:{runtime.Manifest.Identity.Id}",
            _ => CompleteFailedStartCleanupAsync(runtime));
    }

    private async Task CompleteFailedStartCleanupAsync(PluginRuntime runtime)
    {
        await StopRollbackAsync(runtime).ConfigureAwait(false);

        var disposeTask = DisposePluginAsync(runtime);
        try
        {
            if (await disposeTask.WaitAsync(RollbackCleanupTimeout).ConfigureAwait(false))
            {
                ReleaseHttpEndpoints(runtime);
                return;
            }

            _logger.LogWarning(
                "Plugin {PluginId} timed-out start cleanup could not dispose the plugin; endpoint ownership remains fail-closed.",
                runtime.Manifest.Identity.Id);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "Plugin {PluginId} timed-out start cleanup exceeded {Timeout}; endpoint ownership remains fail-closed until disposal completes.",
                runtime.Manifest.Identity.Id,
                RollbackCleanupTimeout);
            ObserveLateDisposeCompletion(runtime, disposeTask);
        }
    }

    private void ObserveLateDisposeCompletion(PluginRuntime runtime, Task<bool> disposeTask)
    {
        _ = disposeTask.ContinueWith(
            static (task, state) =>
            {
                var (host, pluginRuntime) = ((PluginHost Host, PluginRuntime Runtime))state!;
                if (task.Status == TaskStatus.RanToCompletion && task.Result)
                {
                    ReleaseHttpEndpoints(pluginRuntime);
                    host._logger.LogInformation(
                        "Plugin {PluginId} completed delayed timed-out start disposal; endpoint ownership was released.",
                        pluginRuntime.Manifest.Identity.Id);
                    return;
                }

                if (task.Exception is { } exception)
                {
                    host._logger.LogError(
                        exception,
                        "Plugin {PluginId} delayed timed-out start disposal faulted; endpoint ownership remains fail-closed.",
                        pluginRuntime.Manifest.Identity.Id);
                }
            },
            (this, runtime),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void FailWithoutDisposal(
        PluginRuntime runtime,
        string stage,
        string message)
    {
        if (runtime.State is PluginRuntimeState.Failed or PluginRuntimeState.Stopped)
            return;

        CloseHttpEndpointAdmission(runtime);
        _logger.LogError(
            "Plugin {PluginId} version {PluginVersion} failed at {Stage}: {Message}",
            runtime.Manifest.Identity.Id,
            runtime.Manifest.Identity.Version,
            stage,
            message);
        CancelLifetime(runtime, stage);
        runtime.Draft.Clear();
        runtime.EventOwner.Dispose(_logger, runtime.Manifest.Identity.Id);
        runtime.Activation.TrySetCanceled();
        runtime.State = PluginRuntimeState.Failed;
        DisposeLifetime(runtime);
    }

    private async Task FailAsync(
        PluginRuntime runtime,
        string stage,
        string message,
        Exception? exception = null)
    {
        if (runtime.State is PluginRuntimeState.Failed or PluginRuntimeState.Stopped)
            return;

        CloseHttpEndpointAdmission(runtime);
        if (exception is PluginErrorException pluginErrorException)
            _logger.LogError(
                exception,
                "Plugin {PluginId} version {PluginVersion} failed at {Stage}: {Code} {Message} Details={Details}",
                runtime.Manifest.Identity.Id,
                runtime.Manifest.Identity.Version,
                stage,
                pluginErrorException.Error.Code,
                pluginErrorException.Error.Message,
                pluginErrorException.Error.Details);
        else if (exception is not null)
            _logger.LogError(
                exception,
                "Plugin {PluginId} version {PluginVersion} failed at {Stage}: {Message}",
                runtime.Manifest.Identity.Id,
                runtime.Manifest.Identity.Version,
                stage,
                message);
        else
            _logger.LogError(
                "Plugin {PluginId} version {PluginVersion} failed at {Stage}: {Message}",
                runtime.Manifest.Identity.Id,
                runtime.Manifest.Identity.Version,
                stage,
                message);

        CancelLifetime(runtime, stage);
        runtime.Draft.Clear();
        runtime.EventOwner.Dispose(_logger, runtime.Manifest.Identity.Id);
        runtime.Activation.TrySetCanceled();
        if (await DisposePluginAsync(runtime).ConfigureAwait(false))
            ReleaseHttpEndpoints(runtime);
        DisposeLifetime(runtime);
        runtime.State = PluginRuntimeState.Failed;
    }

    private async Task FailAsync(
        PluginRuntime runtime,
        string stage,
        string message,
        DaemonError error)
    {
        CloseHttpEndpointAdmission(runtime);
        _logger.LogError(
            "Plugin {PluginId} version {PluginVersion} returned error at {Stage}: {Code} {Message} Details={Details}",
            runtime.Manifest.Identity.Id,
            runtime.Manifest.Identity.Version,
            stage,
            error.Code,
            error.Message,
            error.Details);
        CancelLifetime(runtime, stage);
        runtime.Draft.Clear();
        runtime.EventOwner.Dispose(_logger, runtime.Manifest.Identity.Id);
        runtime.Activation.TrySetCanceled();
        if (await DisposePluginAsync(runtime).ConfigureAwait(false))
            ReleaseHttpEndpoints(runtime);
        DisposeLifetime(runtime);
        runtime.State = PluginRuntimeState.Failed;
    }

    private void LogReturnedError(PluginRuntime runtime, string stage, DaemonError error)
    {
        _logger.LogError(
            "Plugin {PluginId} returned error at {Stage}: {Code} {Message}",
            runtime.Manifest.Identity.Id,
            stage,
            error.Code,
            error.Message);
    }

    private void CancelLifetime(PluginRuntime runtime, string stage)
    {
        if (runtime.LifetimeCancellation is not null)
            return;

        try
        {
            runtime.LifetimeCancellationStage = stage;
            runtime.LifetimeCancellation = runtime.Lifetime.CancelAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Plugin {PluginId} lifetime cancellation callbacks failed at {Stage}; continuing cleanup.",
                runtime.Manifest.Identity.Id,
                stage);
        }
    }

    private async Task<bool> DisposePluginAsync(PluginRuntime runtime)
    {
        try
        {
            switch (runtime.Plugin)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Plugin {PluginId} dispose threw an unexpected exception.",
                runtime.Manifest.Identity.Id);
            return false;
        }
    }

    private static void CloseHttpEndpointAdmission(PluginRuntime runtime) =>
        runtime.HttpEndpointPolicy?.Close();

    private static void ReleaseHttpEndpoints(PluginRuntime runtime) =>
        runtime.HttpEndpointPolicy?.ReleaseAll();

    private void DisposeLifetime(PluginRuntime runtime)
    {
        if (Interlocked.Exchange(ref runtime.LifetimeDisposeScheduled, 1) != 0)
            return;

        var cancellation = runtime.LifetimeCancellation;
        if (cancellation is null || cancellation.IsCompleted)
        {
            LogLifetimeCancellationFailure(runtime, cancellation, runtime.LifetimeCancellationStage);
            runtime.Lifetime.Dispose();
            return;
        }

        _ = cancellation.ContinueWith(
            static (task, state) =>
            {
                var (host, runtime, stage) = ((PluginHost Host, PluginRuntime Runtime, string? Stage))state!;
                host.LogLifetimeCancellationFailure(runtime, task, stage);
                runtime.Lifetime.Dispose();
            },
            (this, runtime, runtime.LifetimeCancellationStage),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void LogLifetimeCancellationFailure(PluginRuntime runtime, Task? cancellation, string? stage)
    {
        if (cancellation?.Exception is not { } exception)
            return;

        _logger.LogError(
            exception,
            "Plugin {PluginId} lifetime cancellation callbacks failed at {Stage}; cleanup continued.",
            runtime.Manifest.Identity.Id,
            stage ?? "cleanup");
    }

    private sealed class PluginRuntime(
        PluginManifest manifest,
        PluginLoadContext loadContext,
        IDaemonPlugin plugin,
        PluginContext context,
        PluginRegistrationDraft draft,
        CancellationTokenSource lifetime,
        TaskCompletionSource activation,
        PluginEventOwnerLedger eventOwner,
        PluginHttpEndpointPolicy? httpEndpointPolicy)
    {
        internal PluginManifest Manifest { get; } = manifest;
        internal PluginLoadContext LoadContext { get; } = loadContext;
        internal IDaemonPlugin Plugin { get; } = plugin;
        internal PluginContext Context { get; } = context;
        internal PluginRegistrationDraft Draft { get; } = draft;
        internal CancellationTokenSource Lifetime { get; } = lifetime;
        internal Task? LifetimeCancellation { get; set; }
        internal string? LifetimeCancellationStage { get; set; }
        internal int LifetimeDisposeScheduled;
        internal int FailedStartCleanupScheduled;
        internal TaskCompletionSource Activation { get; } = activation;
        internal PluginEventOwnerLedger EventOwner { get; } = eventOwner;
        internal PluginHttpEndpointPolicy? HttpEndpointPolicy { get; } = httpEndpointPolicy;
        internal PluginRuntimeState State { get; set; } = PluginRuntimeState.Discovered;
    }
}

internal sealed class PluginErrorException(PluginError error, Exception? innerException = null)
    : Exception(error.Message, innerException)
{
    internal PluginError Error { get; } = error ?? throw new ArgumentNullException(nameof(error));
}
