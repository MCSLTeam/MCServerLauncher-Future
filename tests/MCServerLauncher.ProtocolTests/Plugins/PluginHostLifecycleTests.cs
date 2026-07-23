using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.API.State;
using MCServerLauncher.Daemon.Plugins;
using MCServerLauncher.Daemon.Plugins.Configuration;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.PluginFixtures.InstanceHealth;
using MCServerLauncher.PluginFixtures.MetadataImposter;
using MCServerLauncher.PluginFixtures.MetadataMalformed;
using MCServerLauncher.PluginFixtures.ReturnedError;
using MCServerLauncher.PluginFixtures.SdkGeneratedHealth;
using MCServerLauncher.PluginFixtures.StartReturnedError;
using MCServerLauncher.PluginFixtures.StartHanging;
using MCServerLauncher.PluginFixtures.StartThrowing;
using MCServerLauncher.PluginFixtures.Throwing;
using MCServerLauncher.ExternalCompileFixture;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RustyOptions;
using SdkGeneratedAdapter = MCServerLauncher.PluginFixtures.SdkGeneratedHealth.Generated.DaemonPluginAdapter;

namespace MCServerLauncher.ProtocolTests.Plugins;

public sealed class PluginHostLifecycleTests
{
    [Fact]
    public async Task StartAsync_BoundsNonCooperativeStartsAndRejectsLateCompletion()
    {
        using var fixture = PluginHostFixture.Create(
            ("fixture.start-never-completes", typeof(NeverCompletingStartPlugin).Assembly, typeof(NeverCompletingStartPlugin).FullName!),
            ("fixture.start-blocking-lifetime-cancellation", typeof(BlockingLifetimeCancellationPlugin).Assembly, typeof(BlockingLifetimeCancellationPlugin).FullName!),
            ("fixture.start-blocking-start-cancellation", typeof(BlockingStartCancellationPlugin).Assembly, typeof(BlockingStartCancellationPlugin).FullName!),
            ("fixture.start-ignores-cancellation", typeof(IgnoresCancellationStartPlugin).Assembly, typeof(IgnoresCancellationStartPlugin).FullName!),
            ("fixture.start-late-success", typeof(DelayedRegisteredSuccessPlugin).Assembly, typeof(DelayedRegisteredSuccessPlugin).FullName!),
            ("fixture.start-synchronously-blocks", typeof(SynchronouslyBlockingStartPlugin).Assembly, typeof(SynchronouslyBlockingStartPlugin).FullName!));
        var logger = new RecordingLogger<PluginHost>();
        var services = new ServiceCollection();
        services.AddMessagePipe(options =>
        {
            options.EnableAutoRegistration = false;
            options.DefaultAsyncPublishStrategy = AsyncPublishStrategy.Sequential;
            options.InstanceLifetime = InstanceLifetime.Singleton;
            options.EnableCaptureStackTrace = false;
        });
        using var provider = services.BuildServiceProvider();
        var host = new PluginHost(
            new SnapshotSource(new InstanceCatalogSnapshot([])),
            new RecordingLoggerFactory(logger),
            logger,
            fixture.PluginsRoot,
            new PluginEventBus(provider.GetRequiredService<EventFactory>()),
            TimeSpan.FromMilliseconds(250));

        // Allow scheduler contention from the full protocol suite while still bounding an
        // otherwise unbounded startup. The fixture includes permanently incomplete starts.
        var supervisionBudget = TimeSpan.FromSeconds(8);
        var startedAt = Stopwatch.GetTimestamp();
        await host.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        var elapsed = Stopwatch.GetElapsedTime(startedAt);

        Assert.True(elapsed < supervisionBudget, $"Plugin supervision took {elapsed}.");
        Assert.Contains(logger.Messages, message =>
            message.Contains("fixture.start-never-completes", StringComparison.Ordinal) &&
            message.Contains("start_timed_out", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message =>
            message.Contains("fixture.start-blocking-lifetime-cancellation", StringComparison.Ordinal) &&
            message.Contains("start_timed_out", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message =>
            message.Contains("fixture.start-blocking-start-cancellation", StringComparison.Ordinal) &&
            message.Contains("start_timed_out", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message =>
            message.Contains("fixture.start-ignores-cancellation", StringComparison.Ordinal) &&
            message.Contains("start_timed_out", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message =>
            message.Contains("fixture.start-late-success", StringComparison.Ordinal) &&
            message.Contains("start_timed_out", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message =>
            message.Contains("fixture.start-synchronously-blocks", StringComparison.Ordinal) &&
            message.Contains("start_timed_out", StringComparison.Ordinal));
        AssertStates(
            host.States,
            ("fixture.start-never-completes", PluginRuntimeState.Failed),
            ("fixture.start-blocking-lifetime-cancellation", PluginRuntimeState.Failed),
            ("fixture.start-blocking-start-cancellation", PluginRuntimeState.Failed),
            ("fixture.start-ignores-cancellation", PluginRuntimeState.Failed),
            ("fixture.start-late-success", PluginRuntimeState.Failed),
            ("fixture.start-synchronously-blocks", PluginRuntimeState.Failed));

        var builder = new ProtocolCatalogBuilder(new OpenRpcInfo("plugin-host-timeout-test", "1.0.0"));
        host.AddCatalogContributions(builder);
        var catalog = builder.Freeze();
        Assert.Empty(catalog.Rpcs);
        Assert.Empty(catalog.Events);

        await Task.Delay(TimeSpan.FromMilliseconds(1500));
        AssertStates(
            host.States,
            ("fixture.start-never-completes", PluginRuntimeState.Failed),
            ("fixture.start-blocking-lifetime-cancellation", PluginRuntimeState.Failed),
            ("fixture.start-blocking-start-cancellation", PluginRuntimeState.Failed),
            ("fixture.start-ignores-cancellation", PluginRuntimeState.Failed),
            ("fixture.start-late-success", PluginRuntimeState.Failed),
            ("fixture.start-synchronously-blocks", PluginRuntimeState.Failed));
        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_DoesNotAwaitNonCooperativeRollbackAfterDeadline()
    {
        var previousHangStop = Environment.GetEnvironmentVariable("MCSL_PLUGIN_HANG_STOP");
        Environment.SetEnvironmentVariable("MCSL_PLUGIN_HANG_STOP", "1");
        try
        {
            using var fixture = PluginHostFixture.Create(
                ("fixture.start-never-completes", typeof(NeverCompletingStartPlugin).Assembly, typeof(NeverCompletingStartPlugin).FullName!));
            var logger = new RecordingLogger<PluginHost>();
            var services = new ServiceCollection();
            services.AddMessagePipe(options =>
            {
                options.EnableAutoRegistration = false;
                options.DefaultAsyncPublishStrategy = AsyncPublishStrategy.Sequential;
                options.InstanceLifetime = InstanceLifetime.Singleton;
                options.EnableCaptureStackTrace = false;
            });
            using var provider = services.BuildServiceProvider();
            var host = new PluginHost(
                new SnapshotSource(new InstanceCatalogSnapshot([])),
                new RecordingLoggerFactory(logger),
                logger,
                fixture.PluginsRoot,
                new PluginEventBus(provider.GetRequiredService<EventFactory>()),
                TimeSpan.FromMilliseconds(150));

            var startedAt = Stopwatch.GetTimestamp();
            await host.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
            var elapsed = Stopwatch.GetElapsedTime(startedAt);

            Assert.True(
                elapsed < TimeSpan.FromSeconds(2),
                $"Startup waited for rollback cleanup after the 150 ms deadline: {elapsed}.");
            AssertStates(host.States, ("fixture.start-never-completes", PluginRuntimeState.Failed));
            Assert.Contains(logger.Messages, message =>
                message.Contains("fixture.start-never-completes", StringComparison.Ordinal) &&
                message.Contains("start_timed_out", StringComparison.Ordinal));

            await host.StopAsync(CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCSL_PLUGIN_HANG_STOP", previousHangStop);
        }
    }

    [Fact]
    public async Task StartAsync_AdmitsHealthPluginCatalogAndIsolatesConfigurationFailures()
    {
        using var fixture = PluginHostFixture.Create(
            ("community.instance-health", typeof(InstanceHealthPlugin).Assembly, typeof(InstanceHealthPlugin).FullName!),
            ("external-compile", typeof(ExternalCompilePlugin).Assembly, typeof(ExternalCompilePlugin).FullName!),
            ("fixture.returned-error", typeof(ReturnedErrorPlugin).Assembly, typeof(ReturnedErrorPlugin).FullName!),
            ("fixture.throwing", typeof(ThrowingPlugin).Assembly, typeof(ThrowingPlugin).FullName!),
            ("fixture.start-returned-error", typeof(StartReturnedErrorPlugin).Assembly, typeof(StartReturnedErrorPlugin).FullName!),
            ("fixture.start-throwing", typeof(StartThrowingPlugin).Assembly, typeof(StartThrowingPlugin).FullName!));
        var source = new SnapshotSource(new InstanceCatalogSnapshot(
        [
            new KeyValuePair<Guid, InstanceSnapshot>(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                new InstanceSnapshot(
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    "running",
                    InstanceType.MCJava,
                    "1.21.8",
                    InstanceStatus.Running,
                    ReadyTimedOut: false)),
            new KeyValuePair<Guid, InstanceSnapshot>(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                new InstanceSnapshot(
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    "stopped",
                    InstanceType.MCJava,
                    "1.21.8",
                    InstanceStatus.Stopped,
                    ReadyTimedOut: false))
        ]));
        var logger = new RecordingLogger<PluginHost>();
        var loggerFactory = new RecordingLoggerFactory(logger);
        var services = new ServiceCollection();
        services.AddMessagePipe(options =>
        {
            options.EnableAutoRegistration = false;
            options.DefaultAsyncPublishStrategy = AsyncPublishStrategy.Sequential;
            options.InstanceLifetime = InstanceLifetime.Singleton;
            options.EnableCaptureStackTrace = false;
        });
        using var provider = services.BuildServiceProvider();
        var host = new PluginHost(
            source,
            loggerFactory,
            logger,
            fixture.PluginsRoot,
            new PluginEventBus(provider.GetRequiredService<EventFactory>()));

        await host.StartAsync(CancellationToken.None);
        Assert.Contains(logger.Messages, message =>
            message.Contains("fixture.returned-error", StringComparison.Ordinal) &&
            message.Contains("fixture_returned_error", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message =>
            message.Contains("fixture.throwing", StringComparison.Ordinal) &&
            message.Contains("configure_threw", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message =>
            message.Contains("fixture.start-returned-error", StringComparison.Ordinal) &&
            message.Contains("start_returned_error", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message =>
            message.Contains("fixture.start-throwing", StringComparison.Ordinal) &&
            message.Contains("start_threw", StringComparison.Ordinal));

        var startedStates = host.States;
        Assert.Equal(6, startedStates.Length);
        AssertStates(startedStates,
            ("community.instance-health", PluginRuntimeState.Started),
            ("external-compile", PluginRuntimeState.Started),
            ("fixture.returned-error", PluginRuntimeState.Failed),
            ("fixture.throwing", PluginRuntimeState.Failed),
            ("fixture.start-returned-error", PluginRuntimeState.Failed),
            ("fixture.start-throwing", PluginRuntimeState.Failed));

        var builder = new ProtocolCatalogBuilder(new OpenRpcInfo("plugin-host-test", "1.0.0"));
        host.AddCatalogContributions(builder);
        var catalog = builder.Freeze();

        var rpc = catalog.Rpcs[new RpcMethod("plugin.community.instance-health.rpc.get")].Binding;
        Assert.Equal("MCServerLauncher.PluginFixtures.InstanceHealth.InstanceHealthRequest", rpc.RequestType.FullName);
        Assert.Equal("MCServerLauncher.PluginFixtures.InstanceHealth.InstanceHealthResult", rpc.ResultType.FullName);
        var execution = await rpc.InvokeAsync(
            new ProtocolInvocationContext(ProtocolExecutionOwner.ForPlugin(
                new ProtocolOwnerIdentity("community.instance-health", "1.0.0"))),
            Activator.CreateInstance(rpc.RequestType)!,
            CancellationToken.None);
        Assert.True(execution.Result.IsOk(out var health));
        Assert.NotNull(health);
        Assert.Equal(2, (int)health.GetType().GetProperty("TotalInstances")!.GetValue(health)!);
        Assert.Equal(1, (int)health.GetType().GetProperty("RunningInstances")!.GetValue(health)!);

        var changed = catalog.Events[new EventName("plugin.community.instance-health.event.changed")];
        Assert.Equal(
            "MCServerLauncher.PluginFixtures.InstanceHealth.InstanceHealthChanged",
            changed.Descriptor.DataTypeInfo.Type.FullName);
        Assert.Null(changed.Descriptor.MetaTypeInfo);

        await host.StopAsync(CancellationToken.None);
        var stoppedStates = host.States;
        Assert.Equal(6, stoppedStates.Length);
        AssertStates(stoppedStates,
            ("community.instance-health", PluginRuntimeState.Stopped),
            ("external-compile", PluginRuntimeState.Stopped),
            ("fixture.returned-error", PluginRuntimeState.Failed),
            ("fixture.throwing", PluginRuntimeState.Failed),
            ("fixture.start-returned-error", PluginRuntimeState.Failed),
            ("fixture.start-throwing", PluginRuntimeState.Failed));
        var stopMessages = logger.Messages
            .Where(static message => message.Contains("fixture.", StringComparison.Ordinal) &&
                                     message.EndsWith(".stop", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(["fixture.external_compile.stop", "fixture.instance_health.stop"], stopMessages);
    }

    [Fact]
    public async Task StartAsync_RejectsMissingDuplicateMalformedAndDriftedMetadataBeforePluginCode()
    {
        var sentinelPath = Path.Combine(Path.GetTempPath(), $"mcsl-plugin-metadata-{Guid.NewGuid():N}.sentinel");
        var previousSentinel = Environment.GetEnvironmentVariable("MCSL_PLUGIN_METADATA_PROBE_PATH");
        Environment.SetEnvironmentVariable("MCSL_PLUGIN_METADATA_PROBE_PATH", sentinelPath);
        try
        {
            using var fixture = PluginHostFixture.Create(
                ("fixture.metadata-identity", typeof(IdentityMetadataMismatchPlugin).Assembly, typeof(IdentityMetadataMismatchPlugin).FullName!),
                ("fixture.metadata-api", typeof(ApiMetadataMismatchPlugin).Assembly, typeof(ApiMetadataMismatchPlugin).FullName!),
                ("fixture.metadata-features", typeof(FeatureMetadataMismatchPlugin).Assembly, typeof(FeatureMetadataMismatchPlugin).FullName!),
                ("fixture.metadata-digest", typeof(DigestMetadataMismatchPlugin).Assembly, typeof(DigestMetadataMismatchPlugin).FullName!),
                ("fixture.metadata-manual", typeof(ManualMetadataProbePlugin).Assembly, typeof(ManualMetadataProbePlugin).FullName!),
                ("fixture.metadata-duplicate", typeof(DuplicateMetadataPlugin).Assembly, typeof(DuplicateMetadataPlugin).FullName!),
                ("fixture.metadata-malformed", typeof(MetadataMalformedPlugin).Assembly, typeof(MetadataMalformedPlugin).FullName!),
                ("fixture.metadata-imposter", typeof(MetadataImposterPlugin).Assembly, typeof(MetadataImposterPlugin).FullName!));
            var logger = new RecordingLogger<PluginHost>();
            var services = new ServiceCollection();
            services.AddMessagePipe(options =>
            {
                options.EnableAutoRegistration = false;
                options.DefaultAsyncPublishStrategy = AsyncPublishStrategy.Sequential;
                options.InstanceLifetime = InstanceLifetime.Singleton;
                options.EnableCaptureStackTrace = false;
            });
            using var provider = services.BuildServiceProvider();
            var host = new PluginHost(
                new SnapshotSource(new InstanceCatalogSnapshot([])),
                new RecordingLoggerFactory(logger),
                logger,
                fixture.PluginsRoot,
                new PluginEventBus(provider.GetRequiredService<EventFactory>()));

            await host.StartAsync(CancellationToken.None);

            Assert.False(File.Exists(sentinelPath));
            Assert.Empty(host.States);
            Assert.Contains(logger.Messages, message => message.Contains("'package.id'", StringComparison.Ordinal));
            Assert.Contains(logger.Messages, message => message.Contains("'requires.api'", StringComparison.Ordinal));
            Assert.Contains(logger.Messages, message => message.Contains("'requires.features'", StringComparison.Ordinal));
            Assert.Contains(logger.Messages, message => message.Contains("'manifest digest'", StringComparison.Ordinal));
            Assert.Contains(logger.Messages, message => message.Contains("'entry.type'", StringComparison.Ordinal));
            Assert.Contains(logger.Messages, message => message.Contains("generated_metadata_duplicate", StringComparison.Ordinal));
            Assert.Contains(logger.Messages, message => message.Contains("generated_metadata_invalid", StringComparison.Ordinal));
            Assert.Contains(logger.Messages, message =>
                message.Contains("fixture.metadata-imposter", StringComparison.Ordinal) &&
                message.Contains("does not contain generated plugin metadata", StringComparison.Ordinal));

            await host.StopAsync(CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCSL_PLUGIN_METADATA_PROBE_PATH", previousSentinel);
            if (File.Exists(sentinelPath))
                File.Delete(sentinelPath);
        }
    }

    [Fact]
    public async Task StartAsync_RejectsMetadataMismatchBeforePromptOrPermanentAdmissionWrite()
    {
        using var fixture = PluginHostFixture.CreateWithFeatures(
            (
                "fixture.metadata-identity",
                typeof(IdentityMetadataMismatchPlugin).Assembly,
                typeof(IdentityMetadataMismatchPlugin).FullName!,
                ["network.http.listen"]));
        var logger = new RecordingLogger<PluginHost>();
        var console = new RecordingPreflightConsole(PluginPreflightDecision.ApprovePermanent);
        var store = new RecordingAdmissionStore();
        var preflight = new PluginAdmissionPreflight(DaemonPluginsConfig.Default, console, store);
        var services = new ServiceCollection();
        services.AddMessagePipe(options =>
        {
            options.EnableAutoRegistration = false;
            options.DefaultAsyncPublishStrategy = AsyncPublishStrategy.Sequential;
            options.InstanceLifetime = InstanceLifetime.Singleton;
            options.EnableCaptureStackTrace = false;
        });
        using var provider = services.BuildServiceProvider();
        var host = new PluginHost(
            new SnapshotSource(new InstanceCatalogSnapshot([])),
            new RecordingLoggerFactory(logger),
            logger,
            fixture.PluginsRoot,
            new PluginEventBus(provider.GetRequiredService<EventFactory>()),
            TimeSpan.FromSeconds(1),
            DaemonPluginsConfig.Default,
            new PluginHttpEndpointRegistry(),
            preflight: preflight);

        await host.StartAsync(CancellationToken.None);

        Assert.Equal(0, console.PromptCount);
        Assert.Equal(0, store.CallCount);
        Assert.Empty(host.States);
        Assert.Contains(logger.Messages, message => message.Contains("'package.id'", StringComparison.Ordinal));
        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void ValidatedEntryAssemblySnapshotLoadsOriginalBytesAfterPathIsOverwritten()
    {
        const string pluginId = "fixture.handwritten-adapter";
        const string entryType =
            "MCServerLauncher.PluginFixtures.HandwrittenAdapterProbe.HandwrittenAdapterProbePlugin";
        using var fixture = PluginHostFixture.CreateFromPath(
            pluginId,
            GetHandwrittenAdapterProbeAssemblyPath(),
            entryType,
            ["instance.query"],
            copyPrivateDiDependency: false);
        var bundle = Path.Combine(fixture.PluginsRoot, pluginId);
        var manifest = PluginManifestReader.ReadAndValidate(bundle, PluginHost.HostApiVersion);
        var validatedImage = GeneratedPluginMetadataReader.ReadValidatedImage(manifest);
        var loadContext = new PluginLoadContext(manifest.EntryAssemblyPath, pluginId);

        File.Copy(
            typeof(MetadataImposterPlugin).Assembly.Location,
            manifest.EntryAssemblyPath,
            overwrite: true);
        var assembly = loadContext.LoadEntryAssembly(validatedImage);

        Assert.Equal("HandwrittenAdapterProbe", assembly.GetName().Name);
        Assert.NotNull(assembly.GetType(entryType, throwOnError: false, ignoreCase: false));
        Assert.Null(assembly.GetType(typeof(MetadataImposterPlugin).FullName!, throwOnError: false, ignoreCase: false));
    }

    [Fact]
    public async Task StartAsync_HandwrittenAdapterReceivesOnlyAuthorizedGrantedApplications()
    {
        var assemblyPath = GetHandwrittenAdapterProbeAssemblyPath();
        var probePath = Path.Combine(Path.GetTempPath(), $"mcsl-handwritten-adapter-{Guid.NewGuid():N}.probe");
        var moduleSentinelPath = Path.Combine(Path.GetTempPath(), $"mcsl-handwritten-adapter-{Guid.NewGuid():N}.module");
        var previousProbe = Environment.GetEnvironmentVariable("MCSL_HANDWRITTEN_ADAPTER_PROBE_PATH");
        var previousModuleSentinel = Environment.GetEnvironmentVariable("MCSL_HANDWRITTEN_ADAPTER_MODULE_SENTINEL");
        Environment.SetEnvironmentVariable("MCSL_HANDWRITTEN_ADAPTER_PROBE_PATH", probePath);
        Environment.SetEnvironmentVariable("MCSL_HANDWRITTEN_ADAPTER_MODULE_SENTINEL", moduleSentinelPath);
        try
        {
            using var fixture = PluginHostFixture.CreateFromPath(
                "fixture.handwritten-adapter",
                assemblyPath,
                "MCServerLauncher.PluginFixtures.HandwrittenAdapterProbe.HandwrittenAdapterProbePlugin",
                ["instance.query"],
                copyPrivateDiDependency: true);
            var logger = new RecordingLogger<PluginHost>();
            var rawApplication = new RecordingRawInstanceApplication();
            var rawOperations = new RecordingRawOperationApplication();
            var services = new ServiceCollection();
            services.AddMessagePipe(options =>
            {
                options.EnableAutoRegistration = false;
                options.DefaultAsyncPublishStrategy = AsyncPublishStrategy.Sequential;
                options.InstanceLifetime = InstanceLifetime.Singleton;
                options.EnableCaptureStackTrace = false;
            });
            using var provider = services.BuildServiceProvider();
            var host = new PluginHost(
                new SnapshotSource(new InstanceCatalogSnapshot([])),
                new RecordingLoggerFactory(logger),
                logger,
                fixture.PluginsRoot,
                new PluginEventBus(provider.GetRequiredService<EventFactory>()),
                TimeSpan.FromSeconds(1),
                DaemonPluginsConfig.Default,
                new PluginHttpEndpointRegistry(),
                instanceApplication: rawApplication,
                operationApplication: rawOperations);

            await host.StartAsync(CancellationToken.None);

            AssertStates(host.States, ("fixture.handwritten-adapter", PluginRuntimeState.Started));
            Assert.True(File.Exists(moduleSentinelPath));
            Assert.Equal("module-initializer", File.ReadAllText(moduleSentinelPath));
            var probe = File.ReadAllLines(probePath)
                .Select(static line => line.Split('=', 2))
                .ToDictionary(static parts => parts[0], static parts => parts[1], StringComparer.Ordinal);
            Assert.Equal(
                "MCServerLauncher.Daemon.API.Application.AuthorizedInstanceQueryApplication",
                probe["contextQueries"]);
            Assert.Equal(
                "MCServerLauncher.Daemon.API.Application.AuthorizedInstanceQueryApplication",
                probe["privateQueries"]);
            Assert.Equal("False", probe["contextRawInstance"]);
            Assert.Equal("False", probe["privateRawInstance"]);
            Assert.Equal("False", probe["privateRawContext"]);
            Assert.Equal("True", probe["undeclaredContextDenied"]);
            Assert.Equal("False", probe["privateUndeclared"]);
            Assert.Equal("fixture.raw-instance", probe["contextResult"]);
            Assert.Equal("fixture.raw-instance", probe["privateResult"]);
            Assert.Equal(2, rawApplication.QueryCallCount);
            Assert.Equal(0, rawOperations.CancelCallCount);

            await host.StopAsync(CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCSL_HANDWRITTEN_ADAPTER_PROBE_PATH", previousProbe);
            Environment.SetEnvironmentVariable("MCSL_HANDWRITTEN_ADAPTER_MODULE_SENTINEL", previousModuleSentinel);
            if (File.Exists(probePath))
                File.Delete(probePath);
            if (File.Exists(moduleSentinelPath))
                File.Delete(moduleSentinelPath);
        }
    }

    [Fact]
    public async Task StartAsync_RejectsHandwrittenMetadataBeforeModuleInitializerRuns()
    {
        var assemblyPath = GetHandwrittenAdapterProbeAssemblyPath();
        var moduleSentinelPath = Path.Combine(Path.GetTempPath(), $"mcsl-handwritten-adapter-{Guid.NewGuid():N}.module");
        var previousModuleSentinel = Environment.GetEnvironmentVariable("MCSL_HANDWRITTEN_ADAPTER_MODULE_SENTINEL");
        Environment.SetEnvironmentVariable("MCSL_HANDWRITTEN_ADAPTER_MODULE_SENTINEL", moduleSentinelPath);
        try
        {
            using var fixture = PluginHostFixture.CreateFromPath(
                "fixture.handwritten-adapter-mismatch",
                assemblyPath,
                "MCServerLauncher.PluginFixtures.HandwrittenAdapterProbe.MetadataRejectedProbePlugin",
                ["instance.query"],
                copyPrivateDiDependency: false);
            var logger = new RecordingLogger<PluginHost>();
            var services = new ServiceCollection();
            services.AddMessagePipe(options =>
            {
                options.EnableAutoRegistration = false;
                options.DefaultAsyncPublishStrategy = AsyncPublishStrategy.Sequential;
                options.InstanceLifetime = InstanceLifetime.Singleton;
                options.EnableCaptureStackTrace = false;
            });
            using var provider = services.BuildServiceProvider();
            var host = new PluginHost(
                new SnapshotSource(new InstanceCatalogSnapshot([])),
                new RecordingLoggerFactory(logger),
                logger,
                fixture.PluginsRoot,
                new PluginEventBus(provider.GetRequiredService<EventFactory>()));

            await host.StartAsync(CancellationToken.None);

            Assert.False(File.Exists(moduleSentinelPath));
            Assert.Empty(host.States);
            Assert.Contains(logger.Messages, message =>
                message.Contains("fixture.handwritten-adapter-mismatch", StringComparison.Ordinal) &&
                message.Contains("'package.id'", StringComparison.Ordinal));
            await host.StopAsync(CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCSL_HANDWRITTEN_ADAPTER_MODULE_SENTINEL", previousModuleSentinel);
            if (File.Exists(moduleSentinelPath))
                File.Delete(moduleSentinelPath);
        }
    }

    [Fact]
    public async Task StartAsync_RejectsHighRiskPreflightBeforeConstructionWithoutRuntimeResidue()
    {
        var sentinelPath = Path.Combine(Path.GetTempPath(), $"mcsl-plugin-preflight-{Guid.NewGuid():N}.sentinel");
        var previousSentinel = Environment.GetEnvironmentVariable("MCSL_PLUGIN_METADATA_PROBE_PATH");
        Environment.SetEnvironmentVariable("MCSL_PLUGIN_METADATA_PROBE_PATH", sentinelPath);
        try
        {
            using var fixture = PluginHostFixture.CreateWithFeatures(
                (
                    "fixture.late-http-cleanup",
                    typeof(LateHttpRegistrationPlugin).Assembly,
                    typeof(LateHttpRegistrationPlugin).FullName!,
                    ["network.http.listen"]));
            var logger = new RecordingLogger<PluginHost>();
            var services = new ServiceCollection();
            services.AddMessagePipe(options =>
            {
                options.EnableAutoRegistration = false;
                options.DefaultAsyncPublishStrategy = AsyncPublishStrategy.Sequential;
                options.InstanceLifetime = InstanceLifetime.Singleton;
                options.EnableCaptureStackTrace = false;
            });
            using var provider = services.BuildServiceProvider();
            var config = DaemonPluginsConfig.Default;
            var host = new PluginHost(
                new SnapshotSource(new InstanceCatalogSnapshot([])),
                new RecordingLoggerFactory(logger),
                logger,
                fixture.PluginsRoot,
                new PluginEventBus(provider.GetRequiredService<EventFactory>()),
                TimeSpan.FromSeconds(1),
                config,
                new PluginHttpEndpointRegistry(),
                preflight: PluginAdmissionPreflight.CreateNonInteractive(config));

            await host.StartAsync(CancellationToken.None);

            Assert.False(File.Exists(sentinelPath));
            Assert.Empty(host.States);
            Assert.Contains(logger.Messages, message =>
                message.Contains("fixture.late-http-cleanup", StringComparison.Ordinal) &&
                message.Contains("approval_required_non_interactive", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message =>
                message.Contains("metadata mismatch", StringComparison.OrdinalIgnoreCase));

            var builder = new ProtocolCatalogBuilder(new OpenRpcInfo("plugin-preflight-test", "1.0.0"));
            host.AddCatalogContributions(builder);
            var catalog = builder.Freeze();
            Assert.Empty(catalog.Rpcs);
            Assert.Empty(catalog.Events);

            await host.StopAsync(CancellationToken.None);
            Assert.Empty(host.States);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCSL_PLUGIN_METADATA_PROBE_PATH", previousSentinel);
            if (File.Exists(sentinelPath))
                File.Delete(sentinelPath);
        }
    }

    [Theory]
    [InlineData("success")]
    [InlineData("throw")]
    [InlineData("timeout")]
    public async Task CleanupReleasesGeneratedProviderAndEveryEndpointExactlyOnce(string mode)
    {
        var probePath = Path.Combine(Path.GetTempPath(), $"mcsl-plugin-provider-{Guid.NewGuid():N}.log");
        var disposeReleasePath = Path.Combine(Path.GetTempPath(), $"mcsl-plugin-provider-release-{Guid.NewGuid():N}.signal");
        const int port = 18123;
        var previousMode = Environment.GetEnvironmentVariable("MCSL_SDK_GENERATED_RESOURCE_MODE");
        var previousPort = Environment.GetEnvironmentVariable("MCSL_SDK_GENERATED_RESOURCE_PORT");
        var previousProbe = Environment.GetEnvironmentVariable("MCSL_SDK_GENERATED_RESOURCE_PROBE_PATH");
        var previousDisposeRelease = Environment.GetEnvironmentVariable("MCSL_SDK_GENERATED_RESOURCE_DISPOSE_RELEASE_PATH");
        Environment.SetEnvironmentVariable("MCSL_SDK_GENERATED_RESOURCE_MODE", mode);
        Environment.SetEnvironmentVariable("MCSL_SDK_GENERATED_RESOURCE_PORT", port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable("MCSL_SDK_GENERATED_RESOURCE_PROBE_PATH", probePath);
        Environment.SetEnvironmentVariable("MCSL_SDK_GENERATED_RESOURCE_DISPOSE_RELEASE_PATH", disposeReleasePath);
        try
        {
            var adapterType = typeof(SdkGeneratedAdapter);
            using var fixture = PluginHostFixture.CreateGenerated(
                "fixture.sdk-generated-health",
                adapterType.Assembly,
                "SdkGeneratedHealth.dll",
                adapterType.FullName!,
                ["network.http.listen"]);
            var logger = new RecordingLogger<PluginHost>();
            var services = new ServiceCollection();
            services.AddMessagePipe(options =>
            {
                options.EnableAutoRegistration = false;
                options.DefaultAsyncPublishStrategy = AsyncPublishStrategy.Sequential;
                options.InstanceLifetime = InstanceLifetime.Singleton;
                options.EnableCaptureStackTrace = false;
            });
            using var provider = services.BuildServiceProvider();
            var config = new DaemonPluginsConfig { GrantLevel = "High" };
            var endpoints = new PluginHttpEndpointRegistry();
            var host = new PluginHost(
                new SnapshotSource(new InstanceCatalogSnapshot([])),
                new RecordingLoggerFactory(logger),
                logger,
                fixture.PluginsRoot,
                new PluginEventBus(provider.GetRequiredService<EventFactory>()),
                TimeSpan.FromMilliseconds(150),
                config,
                endpoints,
                preflight: PluginAdmissionPreflight.CreateNonInteractive(config));

            Task cleanupTask;
            PluginRuntimeState expectedState;
            if (mode.Equals("success", StringComparison.Ordinal))
            {
                await host.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
                AssertStates(host.States, ("fixture.sdk-generated-health", PluginRuntimeState.Started));
                cleanupTask = host.StopAsync(CancellationToken.None);
                expectedState = PluginRuntimeState.Stopped;
            }
            else
            {
                cleanupTask = host.StartAsync(CancellationToken.None);
                expectedState = PluginRuntimeState.Failed;
            }

            await WaitForProbeLineAsync(probePath, "disposing", TimeSpan.FromSeconds(5));

            Assert.False(endpoints.TryRegister(
                "fixture.during-provider-dispose",
                new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, port),
                out var conflictOwner));
            Assert.Equal("fixture.sdk-generated-health", conflictOwner);

            if (mode.Equals("timeout", StringComparison.Ordinal))
                await cleanupTask.WaitAsync(TimeSpan.FromSeconds(5));
            File.WriteAllText(disposeReleasePath, string.Empty);
            if (!mode.Equals("timeout", StringComparison.Ordinal))
                await cleanupTask.WaitAsync(TimeSpan.FromSeconds(5));

            AssertStates(host.States, ("fixture.sdk-generated-health", expectedState));
            var lines = File.ReadAllLines(probePath);
            if (mode.Equals("timeout", StringComparison.Ordinal))
                await WaitForProbeLineAsync(probePath, "disposed", TimeSpan.FromSeconds(5));
            lines = File.ReadAllLines(probePath);
            Assert.Equal(1, lines.Count(static line => line == "created"));
            Assert.Equal(1, lines.Count(static line => line == "disposed"));
            await WaitForEndpointRegistrationAsync(
                endpoints,
                "fixture.after-failure",
                new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, port),
                TimeSpan.FromSeconds(5));

            await host.StopAsync(CancellationToken.None);
            lines = File.ReadAllLines(probePath);
            Assert.Equal(1, lines.Count(static line => line == "created"));
            Assert.Equal(1, lines.Count(static line => line == "disposed"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCSL_SDK_GENERATED_RESOURCE_MODE", previousMode);
            Environment.SetEnvironmentVariable("MCSL_SDK_GENERATED_RESOURCE_PORT", previousPort);
            Environment.SetEnvironmentVariable("MCSL_SDK_GENERATED_RESOURCE_PROBE_PATH", previousProbe);
            Environment.SetEnvironmentVariable("MCSL_SDK_GENERATED_RESOURCE_DISPOSE_RELEASE_PATH", previousDisposeRelease);
            File.WriteAllText(disposeReleasePath, string.Empty);
            if (File.Exists(probePath))
                File.Delete(probePath);
            if (File.Exists(disposeReleasePath))
                File.Delete(disposeReleasePath);
        }
    }

    [Theory]
    [InlineData("configure-failure")]
    [InlineData("start-timeout")]
    [InlineData("shutdown")]
    public async Task CleanupClosesHttpPolicyAgainstLateRegistrations(string mode)
    {
        var signalPath = Path.Combine(Path.GetTempPath(), $"mcsl-plugin-late-http-{Guid.NewGuid():N}.signal");
        var resultPath = Path.Combine(Path.GetTempPath(), $"mcsl-plugin-late-http-{Guid.NewGuid():N}.result");
        var disposeEnteredPath = Path.Combine(Path.GetTempPath(), $"mcsl-plugin-late-http-{Guid.NewGuid():N}.disposing");
        var disposeReleasePath = Path.Combine(Path.GetTempPath(), $"mcsl-plugin-late-http-{Guid.NewGuid():N}.release");
        const int ownedPort = 18124;
        const int latePort = 18125;
        var previousMode = Environment.GetEnvironmentVariable("MCSL_LATE_HTTP_MODE");
        var previousPort = Environment.GetEnvironmentVariable("MCSL_LATE_HTTP_PORT");
        var previousOwnedPort = Environment.GetEnvironmentVariable("MCSL_LATE_HTTP_OWNED_PORT");
        var previousSignal = Environment.GetEnvironmentVariable("MCSL_LATE_HTTP_SIGNAL_PATH");
        var previousResult = Environment.GetEnvironmentVariable("MCSL_LATE_HTTP_RESULT_PATH");
        var previousDisposeEntered = Environment.GetEnvironmentVariable("MCSL_LATE_HTTP_DISPOSE_ENTERED_PATH");
        var previousDisposeRelease = Environment.GetEnvironmentVariable("MCSL_LATE_HTTP_DISPOSE_RELEASE_PATH");
        var previousResource = Environment.GetEnvironmentVariable("MCSL_LATE_HTTP_RESOURCE_PATH");
        Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_MODE", mode);
        Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_PORT", latePort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_OWNED_PORT", ownedPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_SIGNAL_PATH", signalPath);
        Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_RESULT_PATH", resultPath);
        Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_DISPOSE_ENTERED_PATH", disposeEnteredPath);
        Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_DISPOSE_RELEASE_PATH", disposeReleasePath);
        Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_RESOURCE_PATH", null);
        try
        {
            using var fixture = PluginHostFixture.CreateWithFeatures(
                (
                    "fixture.late-http-cleanup",
                    typeof(LateHttpRegistrationPlugin).Assembly,
                    typeof(LateHttpRegistrationPlugin).FullName!,
                    ["network.http.listen"]));
            var logger = new RecordingLogger<PluginHost>();
            var services = new ServiceCollection();
            services.AddMessagePipe(options =>
            {
                options.EnableAutoRegistration = false;
                options.DefaultAsyncPublishStrategy = AsyncPublishStrategy.Sequential;
                options.InstanceLifetime = InstanceLifetime.Singleton;
                options.EnableCaptureStackTrace = false;
            });
            using var provider = services.BuildServiceProvider();
            var config = new DaemonPluginsConfig { GrantLevel = "High" };
            var endpoints = new PluginHttpEndpointRegistry();
            var host = new PluginHost(
                new SnapshotSource(new InstanceCatalogSnapshot([])),
                new RecordingLoggerFactory(logger),
                logger,
                fixture.PluginsRoot,
                new PluginEventBus(provider.GetRequiredService<EventFactory>()),
                TimeSpan.FromMilliseconds(150),
                config,
                endpoints,
                preflight: PluginAdmissionPreflight.CreateNonInteractive(config),
                rollbackCleanupTimeout: TimeSpan.FromMilliseconds(50));

            Task cleanupTask;
            PluginRuntimeState expectedState;
            if (mode.Equals("shutdown", StringComparison.Ordinal))
            {
                await host.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
                AssertStates(host.States, ("fixture.late-http-cleanup", PluginRuntimeState.Started));
                cleanupTask = host.StopAsync(CancellationToken.None);
                expectedState = PluginRuntimeState.Stopped;
            }
            else
            {
                cleanupTask = host.StartAsync(CancellationToken.None);
                expectedState = PluginRuntimeState.Failed;
            }

            await WaitForFileAsync(disposeEnteredPath, TimeSpan.FromSeconds(5));

            var ownedEndpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, ownedPort);
            Assert.False(endpoints.TryRegister("fixture.during-dispose", ownedEndpoint, out var conflictOwner));
            Assert.Equal("fixture.late-http-cleanup", conflictOwner);

            File.WriteAllText(signalPath, string.Empty);
            await WaitForFileTextAsync(resultPath, "plugin_http_policy_closed", TimeSpan.FromSeconds(5));

            var lateEndpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, latePort);
            Assert.True(endpoints.TryRegister("fixture.late-http-competitor", lateEndpoint, out _));

            Task? stopTask = null;
            if (mode.Equals("start-timeout", StringComparison.Ordinal))
            {
                await cleanupTask.WaitAsync(TimeSpan.FromSeconds(5));
                await WaitForLogMessageAsync(
                    logger,
                    "timed-out start cleanup exceeded",
                    TimeSpan.FromSeconds(5));
                stopTask = host.StopAsync(CancellationToken.None);
                Assert.False(stopTask.IsCompleted);
            }
            File.WriteAllText(disposeReleasePath, string.Empty);
            if (mode.Equals("start-timeout", StringComparison.Ordinal))
                await stopTask!.WaitAsync(TimeSpan.FromSeconds(5));
            else
                await cleanupTask.WaitAsync(TimeSpan.FromSeconds(5));
            AssertStates(host.States, ("fixture.late-http-cleanup", expectedState));
            await WaitForEndpointRegistrationAsync(
                endpoints,
                "fixture.after-dispose",
                ownedEndpoint,
                TimeSpan.FromSeconds(5));
            if (!mode.Equals("start-timeout", StringComparison.Ordinal))
                await host.StopAsync(CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_MODE", previousMode);
            Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_PORT", previousPort);
            Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_OWNED_PORT", previousOwnedPort);
            Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_SIGNAL_PATH", previousSignal);
            Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_RESULT_PATH", previousResult);
            Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_DISPOSE_ENTERED_PATH", previousDisposeEntered);
            Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_DISPOSE_RELEASE_PATH", previousDisposeRelease);
            Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_RESOURCE_PATH", previousResource);
            File.WriteAllText(signalPath, string.Empty);
            File.WriteAllText(disposeReleasePath, string.Empty);
            if (File.Exists(signalPath))
                File.Delete(signalPath);
            if (File.Exists(resultPath))
                File.Delete(resultPath);
            if (File.Exists(disposeEnteredPath))
                File.Delete(disposeEnteredPath);
            if (File.Exists(disposeReleasePath))
                File.Delete(disposeReleasePath);
        }
    }

    [Fact]
    public async Task DisposeFailureKeepsClosedEndpointOwnershipFailClosed()
    {
        var signalPath = Path.Combine(Path.GetTempPath(), $"mcsl-plugin-dispose-fault-{Guid.NewGuid():N}.signal");
        var resultPath = Path.Combine(Path.GetTempPath(), $"mcsl-plugin-dispose-fault-{Guid.NewGuid():N}.result");
        const int ownedPort = 18126;
        const int latePort = 18127;
        var previousMode = Environment.GetEnvironmentVariable("MCSL_LATE_HTTP_MODE");
        var previousPort = Environment.GetEnvironmentVariable("MCSL_LATE_HTTP_PORT");
        var previousOwnedPort = Environment.GetEnvironmentVariable("MCSL_LATE_HTTP_OWNED_PORT");
        var previousSignal = Environment.GetEnvironmentVariable("MCSL_LATE_HTTP_SIGNAL_PATH");
        var previousResult = Environment.GetEnvironmentVariable("MCSL_LATE_HTTP_RESULT_PATH");
        var previousDisposeEntered = Environment.GetEnvironmentVariable("MCSL_LATE_HTTP_DISPOSE_ENTERED_PATH");
        var previousDisposeRelease = Environment.GetEnvironmentVariable("MCSL_LATE_HTTP_DISPOSE_RELEASE_PATH");
        var previousResource = Environment.GetEnvironmentVariable("MCSL_LATE_HTTP_RESOURCE_PATH");
        Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_MODE", "dispose-fault");
        Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_PORT", latePort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_OWNED_PORT", ownedPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_SIGNAL_PATH", signalPath);
        Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_RESULT_PATH", resultPath);
        Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_DISPOSE_ENTERED_PATH", null);
        Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_DISPOSE_RELEASE_PATH", null);
        Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_RESOURCE_PATH", null);
        try
        {
            using var fixture = PluginHostFixture.CreateWithFeatures(
                (
                    "fixture.late-http-cleanup",
                    typeof(LateHttpRegistrationPlugin).Assembly,
                    typeof(LateHttpRegistrationPlugin).FullName!,
                    ["network.http.listen"]));
            var logger = new RecordingLogger<PluginHost>();
            var services = new ServiceCollection();
            services.AddMessagePipe(options =>
            {
                options.EnableAutoRegistration = false;
                options.DefaultAsyncPublishStrategy = AsyncPublishStrategy.Sequential;
                options.InstanceLifetime = InstanceLifetime.Singleton;
                options.EnableCaptureStackTrace = false;
            });
            using var provider = services.BuildServiceProvider();
            var config = new DaemonPluginsConfig { GrantLevel = "High" };
            var endpoints = new PluginHttpEndpointRegistry();
            var host = new PluginHost(
                new SnapshotSource(new InstanceCatalogSnapshot([])),
                new RecordingLoggerFactory(logger),
                logger,
                fixture.PluginsRoot,
                new PluginEventBus(provider.GetRequiredService<EventFactory>()),
                TimeSpan.FromSeconds(1),
                config,
                endpoints,
                preflight: PluginAdmissionPreflight.CreateNonInteractive(config));

            await host.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            await host.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            AssertStates(host.States, ("fixture.late-http-cleanup", PluginRuntimeState.Stopped));
            File.WriteAllText(signalPath, string.Empty);
            await WaitForFileTextAsync(resultPath, "plugin_http_policy_closed", TimeSpan.FromSeconds(5));

            var ownedEndpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, ownedPort);
            Assert.False(endpoints.TryRegister("fixture.after-dispose-fault", ownedEndpoint, out var conflictOwner));
            Assert.Equal("fixture.late-http-cleanup", conflictOwner);
            var lateEndpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, latePort);
            Assert.True(endpoints.TryRegister("fixture.after-dispose-fault", lateEndpoint, out _));
            Assert.Contains(logger.Messages, message =>
                message.Contains("fixture.late-http-cleanup", StringComparison.Ordinal) &&
                message.Contains("dispose threw", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_MODE", previousMode);
            Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_PORT", previousPort);
            Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_OWNED_PORT", previousOwnedPort);
            Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_SIGNAL_PATH", previousSignal);
            Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_RESULT_PATH", previousResult);
            Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_DISPOSE_ENTERED_PATH", previousDisposeEntered);
            Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_DISPOSE_RELEASE_PATH", previousDisposeRelease);
            Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_RESOURCE_PATH", previousResource);
            File.WriteAllText(signalPath, string.Empty);
            if (File.Exists(signalPath))
                File.Delete(signalPath);
            if (File.Exists(resultPath))
                File.Delete(resultPath);
        }
    }

    [Fact]
    public async Task LoadFailureAfterAdapterConstructionDisposesOwnedResources()
    {
        var resourcePath = Path.Combine(Path.GetTempPath(), $"mcsl-plugin-load-cleanup-{Guid.NewGuid():N}.lock");
        var disposedPath = resourcePath + ".disposed";
        var previousMode = Environment.GetEnvironmentVariable("MCSL_LATE_HTTP_MODE");
        var previousResource = Environment.GetEnvironmentVariable("MCSL_LATE_HTTP_RESOURCE_PATH");
        var previousDisposeEntered = Environment.GetEnvironmentVariable("MCSL_LATE_HTTP_DISPOSE_ENTERED_PATH");
        var previousDisposeRelease = Environment.GetEnvironmentVariable("MCSL_LATE_HTTP_DISPOSE_RELEASE_PATH");
        Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_MODE", "construction-failure");
        Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_RESOURCE_PATH", resourcePath);
        Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_DISPOSE_ENTERED_PATH", null);
        Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_DISPOSE_RELEASE_PATH", null);
        try
        {
            using var fixture = PluginHostFixture.CreateWithFeatures(
                (
                    "fixture.late-http-cleanup",
                    typeof(LateHttpRegistrationPlugin).Assembly,
                    typeof(LateHttpRegistrationPlugin).FullName!,
                    ["network.http.listen"]));
            File.WriteAllText(
                Path.Combine(fixture.PluginsRoot, "fixture.late-http-cleanup", "config.json"),
                "{ invalid-json");
            var logger = new RecordingLogger<PluginHost>();
            var services = new ServiceCollection();
            services.AddMessagePipe(options =>
            {
                options.EnableAutoRegistration = false;
                options.DefaultAsyncPublishStrategy = AsyncPublishStrategy.Sequential;
                options.InstanceLifetime = InstanceLifetime.Singleton;
                options.EnableCaptureStackTrace = false;
            });
            using var provider = services.BuildServiceProvider();
            var config = new DaemonPluginsConfig { GrantLevel = "High" };
            var host = new PluginHost(
                new SnapshotSource(new InstanceCatalogSnapshot([])),
                new RecordingLoggerFactory(logger),
                logger,
                fixture.PluginsRoot,
                new PluginEventBus(provider.GetRequiredService<EventFactory>()),
                TimeSpan.FromSeconds(1),
                config,
                new PluginHttpEndpointRegistry(),
                preflight: PluginAdmissionPreflight.CreateNonInteractive(config));

            await host.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Empty(host.States);
            Assert.True(File.Exists(disposedPath));
            using (new FileStream(resourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
            }
            Assert.Contains(logger.Messages, message =>
                message.Contains("fixture.late-http-cleanup", StringComparison.Ordinal) &&
                message.Contains("Failed to load plugin", StringComparison.Ordinal));
            await host.StopAsync(CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_MODE", previousMode);
            Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_RESOURCE_PATH", previousResource);
            Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_DISPOSE_ENTERED_PATH", previousDisposeEntered);
            Environment.SetEnvironmentVariable("MCSL_LATE_HTTP_DISPOSE_RELEASE_PATH", previousDisposeRelease);
            if (File.Exists(resourcePath))
                File.Delete(resourcePath);
            if (File.Exists(disposedPath))
                File.Delete(disposedPath);
        }
    }

    private static async Task WaitForProbeLineAsync(string path, string expected, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            while (true)
            {
                if (File.Exists(path) && File.ReadAllLines(path).Contains(expected, StringComparer.Ordinal))
                    return;
                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }

        throw new TimeoutException($"Probe line '{expected}' was not observed before the deadline.");
    }

    private static async Task WaitForFileTextAsync(string path, string expected, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            while (true)
            {
                if (TryReadAllText(path, out var contents) &&
                    string.Equals(contents, expected, StringComparison.Ordinal))
                {
                    return;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }

        var actual = TryReadAllText(path, out var lastContents)
            ? lastContents
            : "<missing or temporarily unavailable>";
        throw new TimeoutException($"File '{path}' contained '{actual}' instead of '{expected}' before the deadline.");
    }

    private static bool TryReadAllText(string path, out string? contents)
    {
        try
        {
            contents = File.ReadAllText(path);
            return true;
        }
        catch (IOException)
        {
            contents = null;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            contents = null;
            return false;
        }
    }

    private static async Task WaitForFileAsync(string path, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            while (!File.Exists(path))
                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            throw new TimeoutException($"File '{path}' was not created before the deadline.");
        }
    }

    private static async Task WaitForLogMessageAsync(
        RecordingLogger<PluginHost> logger,
        string expected,
        TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            while (!logger.Messages.Any(message => message.Contains(expected, StringComparison.Ordinal)))
                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            throw new TimeoutException($"Log message containing '{expected}' was not observed before the deadline.");
        }
    }

    private static async Task WaitForEndpointRegistrationAsync(
        PluginHttpEndpointRegistry endpoints,
        string owner,
        System.Net.IPEndPoint endpoint,
        TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            while (true)
            {
                if (endpoints.TryRegister(owner, endpoint, out _))
                    return;

                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Endpoint '{endpoint}' was not released for owner '{owner}' before the deadline.");
        }
    }

    private static void AssertStates(
        ImmutableArray<(string Id, PluginRuntimeState State)> states,
        params (string Id, PluginRuntimeState State)[] expected)
    {
        var actual = states.ToDictionary(static state => state.Id, static state => state.State, StringComparer.Ordinal);
        Assert.Equal(expected.Length, actual.Count);
        foreach (var (id, state) in expected)
            Assert.Equal(state, actual[id]);
    }

    private static string GetHandwrittenAdapterProbeAssemblyPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "HandwrittenAdapterProbe.dll");
        Assert.True(File.Exists(path), $"Handwritten adapter fixture was not built at '{path}'.");
        return path;
    }

    private sealed class RecordingRawInstanceApplication : IInstanceApplication
    {
        internal int QueryCallCount { get; private set; }

        public Task<Result<InstanceReportList, DaemonError>> ListInstanceReportsAsync(
            CancellationToken cancellationToken)
        {
            QueryCallCount++;
            return Task.FromResult(Result.Err<InstanceReportList, DaemonError>(
                new ValidationDaemonError("fixture.raw-instance", "The raw application was invoked.")));
        }

        public Task<Result<MCServerLauncher.Common.Contracts.Instances.InstanceReport, DaemonError>> GetInstanceReportAsync(InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<InstanceLogResult, DaemonError>> GetInstanceLogAsync(InstanceLogQuery request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<InstanceSettingsResult, DaemonError>> GetInstanceSettingsAsync(InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<CreateInstanceResult, DaemonError>> CreateInstanceAsync(CreateInstanceRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> RemoveInstanceAsync(InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> StartInstanceAsync(InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> StopInstanceAsync(InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> HaltInstanceAsync(InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> SendCommandAsync(InstanceCommandRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<UpdateInstanceSettingsResult, DaemonError>> UpdateInstanceSettingsAsync(UpdateInstanceSettingsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<ConsoleSession, DaemonError>> OpenConsoleAsync(ConsoleOpenRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> ResizeConsoleAsync(ConsoleResizeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CloseConsoleAsync(ConsoleSessionReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> WriteConsoleAsync(Guid sessionId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingRawOperationApplication : IOperationApplication
    {
        internal int CancelCallCount { get; private set; }

        public Task<Result<OperationCancelResult, DaemonError>> CancelOperationAsync(
            OperationCancelRequest request,
            CancellationToken cancellationToken)
        {
            CancelCallCount++;
            return Task.FromResult(Result.Err<OperationCancelResult, DaemonError>(
                new ValidationDaemonError("fixture.raw-operation", "The raw operation application was invoked.")));
        }

        public Task<Result<OperationListResult, DaemonError>> ListOperationsAsync(OperationListQuery request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<OperationSnapshot, DaemonError>> GetOperationAsync(OperationReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class SnapshotSource(InstanceCatalogSnapshot snapshot) : IInstanceSnapshotSource
    {
        private readonly StatePublisher<InstanceCatalogSnapshot> _publisher = new(snapshot);

        public PublishedState<InstanceCatalogSnapshot> Current => _publisher.Current;

        public bool TryGet(Guid instanceId, [NotNullWhen(true)] out InstanceSnapshot? instanceSnapshot) =>
            Current.Value.TryGet(instanceId, out instanceSnapshot);
    }

    private sealed class PluginHostFixture : IDisposable
    {
        private readonly string _root;

        private PluginHostFixture(string root)
        {
            _root = root;
            PluginsRoot = Path.Combine(root, "plugins");
            Directory.CreateDirectory(PluginsRoot);
        }

        public string PluginsRoot { get; }

        public static PluginHostFixture Create(params (string Id, Assembly Assembly, string EntryType)[] plugins)
        {
            var fixture = new PluginHostFixture(Directory.CreateTempSubdirectory("mcsl-plugin-host-").FullName);
            foreach (var plugin in plugins)
                fixture.Add(plugin.Id, plugin.Assembly, plugin.EntryType);
            return fixture;
        }

        public static PluginHostFixture CreateWithFeatures(
            params (string Id, Assembly Assembly, string EntryType, string[] Features)[] plugins)
        {
            var fixture = new PluginHostFixture(Directory.CreateTempSubdirectory("mcsl-plugin-host-").FullName);
            foreach (var plugin in plugins)
                fixture.Add(plugin.Id, plugin.Assembly, plugin.EntryType, plugin.Features);
            return fixture;
        }

        public static PluginHostFixture CreateGenerated(
            string id,
            Assembly assembly,
            string entryAssembly,
            string entryType,
            string[] features)
        {
            var fixture = new PluginHostFixture(Directory.CreateTempSubdirectory("mcsl-plugin-host-").FullName);
            fixture.Add(id, assembly, entryType, features, entryAssembly, copyGeneratedDependencies: true);
            return fixture;
        }

        public static PluginHostFixture CreateFromPath(
            string id,
            string assemblyPath,
            string entryType,
            string[] features,
            bool copyPrivateDiDependency)
        {
            var fixture = new PluginHostFixture(Directory.CreateTempSubdirectory("mcsl-plugin-host-").FullName);
            fixture.AddFromPath(id, assemblyPath, entryType, features, copyPrivateDiDependency);
            return fixture;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private void Add(
            string id,
            Assembly assembly,
            string entryType,
            string[]? features = null,
            string entryAssembly = "PluginEntry.dll",
            bool copyGeneratedDependencies = false)
        {
            var bundle = Path.Combine(PluginsRoot, id);
            Directory.CreateDirectory(bundle);
            File.Copy(assembly.Location, Path.Combine(bundle, entryAssembly));
            if (copyGeneratedDependencies)
            {
                CopyDependency(assembly, bundle, "MCServerLauncher.Daemon.Plugin.Sdk.dll");
                CopyDependency(assembly, bundle, "Microsoft.Extensions.DependencyInjection.dll");
            }
            WriteManifest(bundle, id, entryAssembly, entryType, features);
        }

        private void AddFromPath(
            string id,
            string assemblyPath,
            string entryType,
            string[] features,
            bool copyPrivateDiDependency)
        {
            var bundle = Path.Combine(PluginsRoot, id);
            Directory.CreateDirectory(bundle);
            File.Copy(assemblyPath, Path.Combine(bundle, "PluginEntry.dll"));
            if (copyPrivateDiDependency)
            {
                var dependency = Path.Combine(
                    Path.GetDirectoryName(assemblyPath)!,
                    "Microsoft.Extensions.DependencyInjection.dll");
                File.Copy(dependency, Path.Combine(bundle, Path.GetFileName(dependency)));
            }

            WriteManifest(bundle, id, "PluginEntry.dll", entryType, features);
        }

        private static void WriteManifest(
            string bundle,
            string id,
            string entryAssembly,
            string entryType,
            string[]? features)
        {
            var serializedFeatures = JsonSerializer.Serialize(
                features ?? ["event.publish", "instance.query", "rpc.register"]);
            File.WriteAllText(
                Path.Combine(bundle, "mcsl-plugin.json"),
                $$"""
                {
                  "package": {
                    "id": "{{id}}",
                    "version": "1.0.0"
                  },
                  "entry": {
                    "assembly": "{{entryAssembly}}",
                    "type": "{{entryType}}"
                  },
                  "requires": {
                    "api": "[2.0.0,3.0.0)",
                    "features": {{serializedFeatures}}
                  }
                }
                """);
        }

        private static void CopyDependency(Assembly assembly, string bundle, string fileName)
        {
            var source = Path.Combine(Path.GetDirectoryName(assembly.Location)!, fileName);
            if (File.Exists(source))
                File.Copy(source, Path.Combine(bundle, fileName));
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        internal ConcurrentQueue<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (exception is not null)
                message += " | " + exception.GetBaseException().Message;
            Messages.Enqueue(message);
        }

        private sealed class NullScope : IDisposable
        {
            internal static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class RecordingLoggerFactory(ILogger logger) : ILoggerFactory
    {
        private readonly ILogger _logger = logger;

        public ILogger CreateLogger(string categoryName) => _logger;

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingPreflightConsole(PluginPreflightDecision decision) : IPluginPreflightConsole
    {
        internal int PromptCount { get; private set; }

        public bool IsInteractive => true;

        public PluginPreflightDecision Prompt(PluginPreflightRequest request)
        {
            _ = request;
            PromptCount++;
            return decision;
        }
    }

    private sealed class RecordingAdmissionStore : IPluginAdmissionStore
    {
        internal int CallCount { get; private set; }

        public bool TryPersistPermanent(
            PluginManifest manifest,
            ImmutableArray<MCServerLauncher.Daemon.API.Protocol.PluginFeature> expandedGrants,
            DateTimeOffset decidedAt)
        {
            _ = manifest;
            _ = expandedGrants;
            _ = decidedAt;
            CallCount++;
            return true;
        }
    }
}
