using System.Collections.Immutable;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.API.State;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using RustyOptions;
using ContractDriveInfo = MCServerLauncher.Common.Contracts.System.DriveInfo;

namespace MCServerLauncher.ProtocolTests;

public sealed class BuiltInConnectionDiscoverySystemBindingTests
{
    private static readonly ImmutableArray<string> Methods =
    [
        "mcsl.auth.permissions.get",
        "mcsl.daemon.ping",
        "mcsl.event.subscribe",
        "mcsl.event.unsubscribe",
        "mcsl.instance.catalog.get",
        "mcsl.java.list",
        "mcsl.system.info.get",
        "rpc.discover"
    ];

    [Fact]
    public void Register_UsesExactBuiltInDescriptorsOwnersAndGenericTypesWithoutFreezing()
    {
        var fixture = CreateFixture();

        BuiltInConnectionDiscoverySystemRpcRegistrar.Register(
            fixture.Builder,
            fixture.SystemApplication,
            fixture.SnapshotSource,
            fixture.TimeProvider,
            fixture.CatalogAccessor);

        Assert.Null(fixture.Builder.Catalog);
        var pluginDescriptor = PluginPingDescriptor();
        var pluginOwner = ProtocolExecutionOwner.ForPlugin(new ProtocolOwnerIdentity("test.health", "1.0.0"));
        fixture.Builder.AddRpcDefinition(pluginOwner, pluginDescriptor);
        fixture.Builder.AddRpcBinding(
            pluginDescriptor.Method,
            new RpcBinding<EmptyRequest, PingResult>(
                pluginOwner,
                static (_, _, _) => Task.FromResult(
                    ProtocolRpcExecution<PingResult>.Ok(new PingResult(1)))));

        var catalog = fixture.Builder.Freeze();
        var expectedDescriptors = BuiltInProtocolDefinitions.Rpcs
            .Where(descriptor => Methods.Contains(descriptor.Method.Value, StringComparer.Ordinal))
            .ToArray();
        var actualEntries = catalog.Rpcs.Values
            .Where(entry => entry.Owner.Equals(ProtocolExecutionOwner.BuiltIn))
            .ToArray();

        Assert.Equal(
            expectedDescriptors.Select(descriptor => descriptor.Method.Value).Order(StringComparer.Ordinal),
            actualEntries.Select(entry => entry.Descriptor.Method.Value).Order(StringComparer.Ordinal));
        foreach (var descriptor in expectedDescriptors)
        {
            var entry = catalog.Rpcs[descriptor.Method];
            Assert.Same(descriptor, entry.Descriptor);
            Assert.Equal(ProtocolExecutionOwner.BuiltIn, entry.Owner);
            Assert.Equal(descriptor.RequestTypeInfo.Type, entry.Binding.RequestType);
            Assert.Equal(descriptor.ResultTypeInfo.Type, entry.Binding.ResultType);
        }
    }

    [Fact]
    public async Task PermissionsAndSubscriptions_UseOnlyPerInvocationCapabilities()
    {
        var fixture = CreateFrozenFixture();
        var firstPermissions = ImmutableArray.Create("first", "second");
        var firstSubscriptions = new TestSubscriptions();
        var firstContext = new ProtocolInvocationContext(
            ProtocolExecutionOwner.BuiltIn,
            new TestPermissionView(firstPermissions),
            firstSubscriptions);
        var secondSubscriptions = new TestSubscriptions();
        var secondContext = new ProtocolInvocationContext(
            ProtocolExecutionOwner.BuiltIn,
            new TestPermissionView(["other"]),
            secondSubscriptions);

        var permissions = await Invoke<EmptyRequest, PermissionsResult>(
            fixture.Catalog,
            "mcsl.auth.permissions.get",
            firstContext,
            new EmptyRequest(),
            CancellationToken.None);
        Assert.True(permissions.Result.IsOk(out var permissionResult));
        Assert.True(permissionResult.Permissions.SequenceEqual(firstPermissions));

        var request = new EventSubscriptionRequest("mcsl.event.daemon.report");
        var subscribed = await Invoke<EventSubscriptionRequest, UnitResult>(
            fixture.Catalog,
            "mcsl.event.subscribe",
            firstContext,
            request,
            CancellationToken.None);
        var unsubscribed = await Invoke<EventSubscriptionRequest, UnitResult>(
            fixture.Catalog,
            "mcsl.event.unsubscribe",
            secondContext,
            request,
            CancellationToken.None);

        Assert.True(subscribed.Result.IsOk(out _));
        Assert.True(unsubscribed.Result.IsOk(out _));
        Assert.Same(request, firstSubscriptions.LastSubscribeRequest);
        Assert.Null(firstSubscriptions.LastUnsubscribeRequest);
        Assert.Same(request, secondSubscriptions.LastUnsubscribeRequest);
        Assert.Null(secondSubscriptions.LastSubscribeRequest);
    }

    [Fact]
    public async Task ConnectionBindings_ReturnStableTypedErrorsForMissingCapabilitiesAndPreserveErrors()
    {
        var fixture = CreateFrozenFixture();
        var emptyContext = new ProtocolInvocationContext(ProtocolExecutionOwner.BuiltIn);

        var permissions = await Invoke<EmptyRequest, PermissionsResult>(
            fixture.Catalog,
            "mcsl.auth.permissions.get",
            emptyContext,
            new EmptyRequest(),
            CancellationToken.None);
        var subscribe = await Invoke<EventSubscriptionRequest, UnitResult>(
            fixture.Catalog,
            "mcsl.event.subscribe",
            emptyContext,
            new EventSubscriptionRequest("mcsl.event.daemon.report"),
            CancellationToken.None);

        AssertStableMissingCapability(permissions.Result);
        AssertStableMissingCapability(subscribe.Result);

        var expected = new ConflictDaemonError("subscription.conflict", "Expected conflict.");
        var subscriptions = new TestSubscriptions(expected);
        var context = new ProtocolInvocationContext(
            ProtocolExecutionOwner.BuiltIn,
            null,
            subscriptions);
        var failed = await Invoke<EventSubscriptionRequest, UnitResult>(
            fixture.Catalog,
            "mcsl.event.unsubscribe",
            context,
            new EventSubscriptionRequest("mcsl.event.daemon.report"),
            CancellationToken.None);

        Assert.True(failed.Result.IsErr(out var actual));
        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task SubscriptionSuccessSerializesUnitResultAsEmptyObject()
    {
        var fixture = CreateFrozenFixture();
        var context = new ProtocolInvocationContext(
            ProtocolExecutionOwner.BuiltIn,
            null,
            new TestSubscriptions());
        var execution = await Invoke<EventSubscriptionRequest, UnitResult>(
            fixture.Catalog,
            "mcsl.event.subscribe",
            context,
            new EventSubscriptionRequest("mcsl.event.daemon.report"),
            CancellationToken.None);
        Assert.True(execution.Result.IsOk(out var result));

        var json = JsonSerializer.Serialize(result, BuiltInProtocolJsonContext.Default.UnitResult);

        Assert.Equal("{}", json);
    }

    [Fact]
    public async Task Ping_UsesInjectedUtcClockAsUnixMilliseconds()
    {
        var expected = new DateTimeOffset(2026, 7, 12, 3, 4, 5, TimeSpan.Zero);
        var fixture = CreateFrozenFixture(timeProvider: new FixedTimeProvider(expected));

        var execution = await Invoke<EmptyRequest, PingResult>(
            fixture.Catalog,
            "mcsl.daemon.ping",
            new ProtocolInvocationContext(ProtocolExecutionOwner.BuiltIn),
            new EmptyRequest(),
            CancellationToken.None);

        Assert.True(execution.Result.IsOk(out var result));
        Assert.Equal(expected.ToUnixTimeMilliseconds(), result.Time);
    }

    [Fact]
    public async Task Discover_ReturnsTypedUnavailableBeforePublishAndFinalDocumentAfterPublish()
    {
        var fixture = CreateFixture();
        BuiltInConnectionDiscoverySystemRpcRegistrar.Register(
            fixture.Builder,
            fixture.SystemApplication,
            fixture.SnapshotSource,
            fixture.TimeProvider,
            fixture.CatalogAccessor);
        var pluginDescriptor = PluginPingDescriptor();
        var pluginOwner = ProtocolExecutionOwner.ForPlugin(new ProtocolOwnerIdentity("test.health", "1.0.0"));
        fixture.Builder.AddRpcDefinition(pluginOwner, pluginDescriptor);
        fixture.Builder.AddRpcBinding(
            pluginDescriptor.Method,
            new RpcBinding<EmptyRequest, PingResult>(
                pluginOwner,
                static (_, _, _) => Task.FromResult(
                    ProtocolRpcExecution<PingResult>.Ok(new PingResult(1)))));
        var catalog = fixture.Builder.Freeze();

        var unavailable = await Invoke<EmptyRequest, OpenRpcDocument>(
            catalog,
            "rpc.discover",
            new ProtocolInvocationContext(ProtocolExecutionOwner.BuiltIn),
            new EmptyRequest(),
            CancellationToken.None);
        Assert.True(unavailable.Result.IsErr(out var error));
        var unavailableError = Assert.IsAssignableFrom<DaemonError>(error);
        Assert.Equal("protocol.catalog.unavailable", unavailableError.Code);
        Assert.Equal(DaemonErrorKind.Internal, unavailableError.Kind);

        fixture.CatalogAccessor.Publish(catalog);
        var available = await Invoke<EmptyRequest, OpenRpcDocument>(
            catalog,
            "rpc.discover",
            new ProtocolInvocationContext(ProtocolExecutionOwner.BuiltIn),
            new EmptyRequest(),
            CancellationToken.None);

        Assert.True(available.Result.IsOk(out var document));
        Assert.Same(catalog.Document, document);
        Assert.Contains(document.Methods, method => method.Name == pluginDescriptor.Method.Value);
    }

    [Fact]
    public async Task CatalogGet_UsesOnePublishedSnapshotVersionAndDeterministicImmutableItems()
    {
        var high = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var low = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var publisher = new StatePublisher<InstanceCatalogSnapshot>(new InstanceCatalogSnapshot(
        [
            new(high, new InstanceSnapshot(high, "High", InstanceType.Universal, "2", InstanceStatus.Running)),
            new(low, new InstanceSnapshot(low, "Low", InstanceType.MCJava, "1", InstanceStatus.Stopped))
        ]));
        publisher.Publish(7, publisher.Current.Value);
        var source = new TestSnapshotSource(publisher);
        var fixture = CreateFrozenFixture(snapshotSource: source);

        var execution = await Invoke<EmptyRequest, InstanceCatalogResult>(
            fixture.Catalog,
            "mcsl.instance.catalog.get",
            new ProtocolInvocationContext(ProtocolExecutionOwner.BuiltIn),
            new EmptyRequest(),
            CancellationToken.None);
        publisher.Publish(8, InstanceCatalogSnapshot.Empty);

        Assert.True(execution.Result.IsOk(out var result));
        Assert.Equal(1, source.CurrentReadCount);
        Assert.Equal(7, result.Version);
        Assert.Equal([low, high], result.Items.Select(item => item.InstanceId));
        Assert.Equal(["Low", "High"], result.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task SystemBindings_DelegateCancellationAndPreserveSuccessIdentity()
    {
        var systemInfo = CreateSystemInfo();
        var javaList = new JavaRuntimeList([new JavaRuntime("java", "21", "x64")]);
        var application = new TestSystemApplication
        {
            SystemResult = Result.Ok<SystemInfo, DaemonError>(systemInfo),
            JavaResult = Result.Ok<JavaRuntimeList, DaemonError>(javaList)
        };
        var fixture = CreateFrozenFixture(systemApplication: application);
        using var cancellation = new CancellationTokenSource();

        var systemExecution = await Invoke<EmptyRequest, SystemInfo>(
            fixture.Catalog,
            "mcsl.system.info.get",
            new ProtocolInvocationContext(ProtocolExecutionOwner.BuiltIn),
            new EmptyRequest(),
            cancellation.Token);
        var javaExecution = await Invoke<EmptyRequest, JavaRuntimeList>(
            fixture.Catalog,
            "mcsl.java.list",
            new ProtocolInvocationContext(ProtocolExecutionOwner.BuiltIn),
            new EmptyRequest(),
            cancellation.Token);

        Assert.True(systemExecution.Result.IsOk(out var actualSystem));
        Assert.Same(systemInfo, actualSystem);
        Assert.True(javaExecution.Result.IsOk(out var actualJava));
        Assert.Same(javaList, actualJava);
        Assert.Equal(cancellation.Token, application.SystemCancellationToken);
        Assert.Equal(cancellation.Token, application.JavaCancellationToken);
        Assert.Equal(1, application.SystemCallCount);
        Assert.Equal(1, application.JavaCallCount);
    }

    [Fact]
    public async Task SystemBinding_PreservesApplicationErrorIdentity()
    {
        var expectedError = new InternalDaemonError("system.expected", "Expected failure.");
        var application = new TestSystemApplication
        {
            SystemResult = Result.Err<SystemInfo, DaemonError>(expectedError)
        };
        var fixture = CreateFrozenFixture(systemApplication: application);

        var execution = await Invoke<EmptyRequest, SystemInfo>(
            fixture.Catalog,
            "mcsl.system.info.get",
            new ProtocolInvocationContext(ProtocolExecutionOwner.BuiltIn),
            new EmptyRequest(),
            CancellationToken.None);

        Assert.True(execution.Result.IsErr(out var actualError));
        Assert.Same(expectedError, actualError);
    }

    [Fact]
    public async Task SystemBindings_PropagateCancellationWithoutConvertingWrappingOrSwallowing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var application = new TestSystemApplication
        {
            SystemHandler = token => Task.FromCanceled<Result<SystemInfo, DaemonError>>(token),
            JavaHandler = token => throw new OperationCanceledException(token)
        };
        var fixture = CreateFrozenFixture(systemApplication: application);
        var context = new ProtocolInvocationContext(ProtocolExecutionOwner.BuiltIn);

        var systemCancellation = await Assert.ThrowsAsync<TaskCanceledException>(() =>
            Invoke<EmptyRequest, SystemInfo>(
                fixture.Catalog,
                "mcsl.system.info.get",
                context,
                new EmptyRequest(),
                cancellation.Token));
        var javaCancellation = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Invoke<EmptyRequest, JavaRuntimeList>(
                fixture.Catalog,
                "mcsl.java.list",
                context,
                new EmptyRequest(),
                cancellation.Token));

        Assert.Equal(cancellation.Token, systemCancellation.CancellationToken);
        Assert.Equal(cancellation.Token, javaCancellation.CancellationToken);
        Assert.Equal(1, application.SystemCallCount);
        Assert.Equal(1, application.JavaCallCount);
        Assert.Equal(cancellation.Token, application.SystemCancellationToken);
        Assert.Equal(cancellation.Token, application.JavaCancellationToken);
    }

    [Fact]
    public void RegistrarSurface_ContainsOnlyApprovedDependenciesAndNoTransportTypes()
    {
        var register = typeof(BuiltInConnectionDiscoverySystemRpcRegistrar).GetMethod(
            nameof(BuiltInConnectionDiscoverySystemRpcRegistrar.Register),
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;

        Assert.Equal(
            [typeof(ProtocolCatalogBuilder), typeof(ISystemApplication), typeof(IInstanceSnapshotSource), typeof(TimeProvider), typeof(IFrozenProtocolCatalogAccessor)],
            register.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.DoesNotContain(
            typeof(BuiltInConnectionDiscoverySystemRpcRegistrar).Assembly.GetTypes()
                .Where(type => type.Namespace == typeof(BuiltInConnectionDiscoverySystemRpcRegistrar).Namespace)
                .Where(type => type.Name.Contains("ConnectionDiscoverySystem", StringComparison.Ordinal))
                .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                .SelectMany(MemberTypes),
            IsForbiddenType);
    }

    private static async Task<ProtocolRpcExecution<TResult>> Invoke<TRequest, TResult>(
        FrozenProtocolCatalog catalog,
        string method,
        ProtocolInvocationContext context,
        TRequest request,
        CancellationToken cancellationToken)
        where TResult : notnull
    {
        var entry = catalog.Rpcs[new RpcMethod(method)];
        var binding = Assert.IsType<RpcBinding<TRequest, TResult>>(entry.Binding);
        return await binding.Handler(context, request, cancellationToken);
    }

    private static void AssertStableMissingCapability<TResult>(Result<TResult, DaemonError> result)
        where TResult : notnull
    {
        Assert.True(result.IsErr(out var error));
        var capabilityError = Assert.IsAssignableFrom<DaemonError>(error);
        Assert.Equal("protocol.connection.capability.unavailable", capabilityError.Code);
        Assert.Equal(DaemonErrorKind.Internal, capabilityError.Kind);
    }

    private static TestFixture CreateFixture(
        TestSystemApplication? systemApplication = null,
        TestSnapshotSource? snapshotSource = null,
        TimeProvider? timeProvider = null)
    {
        var publisher = new StatePublisher<InstanceCatalogSnapshot>(InstanceCatalogSnapshot.Empty);
        return new TestFixture(
            new ProtocolCatalogBuilder(new OpenRpcInfo("Binding tests", "1.0.0")),
            systemApplication ?? new TestSystemApplication(),
            snapshotSource ?? new TestSnapshotSource(publisher),
            timeProvider ?? new FixedTimeProvider(DateTimeOffset.UnixEpoch),
            new FrozenProtocolCatalogAccessor());
    }

    private static FrozenFixture CreateFrozenFixture(
        TestSystemApplication? systemApplication = null,
        TestSnapshotSource? snapshotSource = null,
        TimeProvider? timeProvider = null)
    {
        var fixture = CreateFixture(systemApplication, snapshotSource, timeProvider);
        BuiltInConnectionDiscoverySystemRpcRegistrar.Register(
            fixture.Builder,
            fixture.SystemApplication,
            fixture.SnapshotSource,
            fixture.TimeProvider,
            fixture.CatalogAccessor);
        return new FrozenFixture(
            fixture.Builder.Freeze(),
            fixture.SystemApplication,
            fixture.SnapshotSource,
            fixture.CatalogAccessor);
    }

    private static RpcDescriptor<EmptyRequest, PingResult> PluginPingDescriptor()
    {
        var builtIn = (RpcDescriptor<EmptyRequest, PingResult>)BuiltInProtocolDefinitions.Rpcs.Single(
            descriptor => descriptor.Method.Value == "mcsl.daemon.ping");
        var constructor = typeof(RpcDescriptor<EmptyRequest, PingResult>).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(RpcMethod),
                typeof(PermissionName),
                typeof(JsonTypeInfo<EmptyRequest>),
                typeof(JsonTypeInfo<PingResult>),
                typeof(bool),
                typeof(RpcDocumentation)
            ],
            modifiers: null);
        Assert.NotNull(constructor);
        return (RpcDescriptor<EmptyRequest, PingResult>)constructor.Invoke(
        [
            new RpcMethod("plugin.test.health.rpc.ping"),
            new PermissionName("*"),
            builtIn.RequestTypeInfo,
            builtIn.ResultTypeInfo,
            false,
            new RpcDocumentation(
                "test",
                "Plugin ping",
                "Synthetic plugin method.",
                "test.plugin.ping.request",
                "test.plugin.ping.result")
        ]);
    }

    private static SystemInfo CreateSystemInfo()
    {
        var drive = new ContractDriveInfo("NTFS", 100, 50, "C");
        return new SystemInfo(
            new OperatingSystemInfo("Windows", "x64"),
            new ProcessorInfo("vendor", "cpu", 1, 0, 1, 1),
            new MemoryInfo(100, 50),
            drive,
            [drive],
            "test");
    }

    private static IEnumerable<Type> MemberTypes(MemberInfo member) => member switch
    {
        MethodBase method => method.GetParameters().Select(parameter => parameter.ParameterType)
            .Append(method is MethodInfo methodInfo ? methodInfo.ReturnType : typeof(void)),
        PropertyInfo property => [property.PropertyType],
        FieldInfo field => [field.FieldType],
        _ => []
    };

    private static bool IsForbiddenType(Type type)
    {
        if (type.IsByRef || type.IsArray || type.IsPointer)
        {
            return IsForbiddenType(type.GetElementType()!);
        }

        if (type.IsGenericType)
        {
            return type.GetGenericArguments().Any(IsForbiddenType);
        }

        return type.Assembly.GetName().Name?.StartsWith("TouchSocket", StringComparison.Ordinal) == true ||
               type.FullName?.Contains("WsContext", StringComparison.Ordinal) == true ||
               type.FullName?.Contains("IServiceProvider", StringComparison.Ordinal) == true;
    }

    private sealed record TestFixture(
        ProtocolCatalogBuilder Builder,
        TestSystemApplication SystemApplication,
        TestSnapshotSource SnapshotSource,
        TimeProvider TimeProvider,
        FrozenProtocolCatalogAccessor CatalogAccessor);

    private sealed record FrozenFixture(
        FrozenProtocolCatalog Catalog,
        TestSystemApplication SystemApplication,
        TestSnapshotSource SnapshotSource,
        FrozenProtocolCatalogAccessor CatalogAccessor);

    private sealed record TestPermissionView(ImmutableArray<string> Permissions) : IProtocolPermissionView;

    private sealed class TestSubscriptions(DaemonError? error = null) : IProtocolSubscriptionOperations
    {
        public EventSubscriptionRequest? LastSubscribeRequest { get; private set; }
        public EventSubscriptionRequest? LastUnsubscribeRequest { get; private set; }

        public Result<Unit, DaemonError> Subscribe(EventSubscriptionRequest request)
        {
            LastSubscribeRequest = request;
            return error is null
                ? Result.Ok<Unit, DaemonError>(Unit.Default)
                : Result.Err<Unit, DaemonError>(error);
        }

        public Result<Unit, DaemonError> Unsubscribe(EventSubscriptionRequest request)
        {
            LastUnsubscribeRequest = request;
            return error is null
                ? Result.Ok<Unit, DaemonError>(Unit.Default)
                : Result.Err<Unit, DaemonError>(error);
        }
    }

    private sealed class TestSystemApplication : ISystemApplication
    {
        public Result<SystemInfo, DaemonError> SystemResult { get; init; } =
            Result.Ok<SystemInfo, DaemonError>(CreateSystemInfo());
        public Result<JavaRuntimeList, DaemonError> JavaResult { get; init; } =
            Result.Ok<JavaRuntimeList, DaemonError>(new JavaRuntimeList([]));
        public Func<CancellationToken, Task<Result<SystemInfo, DaemonError>>>? SystemHandler { get; init; }
        public Func<CancellationToken, Task<Result<JavaRuntimeList, DaemonError>>>? JavaHandler { get; init; }
        public CancellationToken SystemCancellationToken { get; private set; }
        public CancellationToken JavaCancellationToken { get; private set; }
        public int SystemCallCount { get; private set; }
        public int JavaCallCount { get; private set; }

        public Task<Result<SystemInfo, DaemonError>> GetSystemInfoAsync(CancellationToken cancellationToken)
        {
            SystemCallCount++;
            SystemCancellationToken = cancellationToken;
            return SystemHandler?.Invoke(cancellationToken) ?? Task.FromResult(SystemResult);
        }

        public Task<Result<JavaRuntimeList, DaemonError>> ListJavaRuntimesAsync(CancellationToken cancellationToken)
        {
            JavaCallCount++;
            JavaCancellationToken = cancellationToken;
            return JavaHandler?.Invoke(cancellationToken) ?? Task.FromResult(JavaResult);
        }
    }

    private sealed class TestSnapshotSource(StatePublisher<InstanceCatalogSnapshot> publisher) : IInstanceSnapshotSource
    {
        public int CurrentReadCount { get; private set; }

        public PublishedState<InstanceCatalogSnapshot> Current
        {
            get
            {
                CurrentReadCount++;
                return publisher.Current;
            }
        }

        public bool TryGet(Guid instanceId, [NotNullWhen(true)] out InstanceSnapshot? snapshot) =>
            Current.Value.TryGet(instanceId, out snapshot);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
