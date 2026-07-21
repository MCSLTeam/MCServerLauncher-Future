using System.Reflection;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.API.State;
using MCServerLauncher.Daemon.Plugins;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.PluginFixtures.InstanceHealth;
using MCServerLauncher.PluginFixtures.ReturnedError;
using MCServerLauncher.PluginFixtures.StartReturnedError;
using MCServerLauncher.PluginFixtures.StartHanging;
using MCServerLauncher.PluginFixtures.StartThrowing;
using MCServerLauncher.PluginFixtures.Throwing;
using MCServerLauncher.ExternalCompileFixture;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
                    InstanceStatus.Running)),
            new KeyValuePair<Guid, InstanceSnapshot>(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                new InstanceSnapshot(
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    "stopped",
                    InstanceType.MCJava,
                    "1.21.8",
                    InstanceStatus.Stopped))
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

    private static void AssertStates(
        ImmutableArray<(string Id, PluginRuntimeState State)> states,
        params (string Id, PluginRuntimeState State)[] expected)
    {
        var actual = states.ToDictionary(static state => state.Id, static state => state.State, StringComparer.Ordinal);
        Assert.Equal(expected.Length, actual.Count);
        foreach (var (id, state) in expected)
            Assert.Equal(state, actual[id]);
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

        private void Add(string id, Assembly assembly, string entryType)
        {
            var bundle = Path.Combine(PluginsRoot, id);
            Directory.CreateDirectory(bundle);
            File.Copy(assembly.Location, Path.Combine(bundle, "PluginEntry.dll"));
            File.WriteAllText(
                Path.Combine(bundle, "mcsl-plugin.json"),
                $$"""
                {
                  "package": {
                    "id": "{{id}}",
                    "version": "1.0.0"
                  },
                  "entry": {
                    "assembly": "PluginEntry.dll",
                    "type": "{{entryType}}"
                  },
                  "requires": {
                    "api": "[2.0.0,3.0.0)",
                    "features": ["event.publish", "instance.query", "rpc.register"]
                  }
                }
                """);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        internal List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
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
}
