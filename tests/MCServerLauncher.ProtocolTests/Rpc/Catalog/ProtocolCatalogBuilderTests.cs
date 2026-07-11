using System.Reflection;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;

namespace MCServerLauncher.ProtocolTests;

public sealed class ProtocolCatalogBuilderTests
{
    [Fact]
    public void Freeze_CreatesOrdinalFrozenLookupsAndCachesTheFinalDocument()
    {
        var builder = CreateBuilder();
        AddPing(builder);
        AddPermissions(builder);
        AddDaemonReport(builder);

        var catalog = builder.Freeze();

        Assert.IsAssignableFrom<System.Collections.Frozen.FrozenDictionary<RpcMethod, FrozenRpcBinding>>(catalog.Rpcs);
        Assert.IsAssignableFrom<System.Collections.Frozen.FrozenDictionary<EventName, FrozenEventBinding>>(catalog.Events);
        Assert.Equal(
            ["mcsl.auth.permissions.get", "mcsl.daemon.ping"],
            catalog.RpcDefinitions.Select(descriptor => descriptor.Method.Value));
        Assert.Equal(["mcsl.event.daemon.report"], catalog.EventDefinitions.Select(descriptor => descriptor.Name.Value));
        Assert.True(catalog.TryGetRpc(new RpcMethod("mcsl.daemon.ping"), out var rpc));
        Assert.Equal("mcsl.daemon.ping", rpc.Descriptor.Method.Value);
        Assert.Same(Rpc("mcsl.daemon.ping"), rpc.Descriptor);
        Assert.True(catalog.TryGetEvent(new EventName("mcsl.event.daemon.report"), out var @event));
        Assert.Equal("mcsl.event.daemon.report", @event.Descriptor.Name.Value);
        Assert.False(catalog.TryGetRpc(new RpcMethod("mcsl.daemon.missing"), out _));
        Assert.False(catalog.TryGetEvent(new EventName("mcsl.event.missing"), out _));

        var expectedUtf8 = JsonSerializer.SerializeToUtf8Bytes(
            catalog.Document,
            BuiltInProtocolJsonContext.Default.OpenRpcDocument);
        Assert.Equal(expectedUtf8, catalog.DocumentUtf8);
        Assert.Same(catalog.Document, catalog.Document);
    }

    [Fact]
    public void Registration_RejectsDuplicateDefinitionsAndBindings()
    {
        var builder = CreateBuilder();
        var descriptor = Rpc("mcsl.daemon.ping");
        var binding = PingBinding();

        builder.AddRpcDefinition(ProtocolExecutionOwner.BuiltIn, descriptor);
        Assert.Throws<ArgumentException>(() => builder.AddRpcDefinition(ProtocolExecutionOwner.BuiltIn, descriptor));
        builder.AddRpcBinding(descriptor.Method, binding);
        Assert.Throws<ArgumentException>(() => builder.AddRpcBinding(descriptor.Method, binding));
    }

    [Fact]
    public void Registration_RejectsReservedOrForeignNamespaces()
    {
        var builder = CreateBuilder();
        var plugin = ProtocolExecutionOwner.ForPlugin(new ProtocolOwnerIdentity("community.health", "1.0.0"));
        var builtInDescriptor = Rpc("mcsl.daemon.ping");

        Assert.Throws<ArgumentException>(() => builder.AddRpcDefinition(plugin, builtInDescriptor));
        Assert.Throws<ArgumentException>(() => builder.AddRpcBinding(builtInDescriptor.Method, PingBinding(plugin)));
    }

    [Fact]
    public void PluginOwner_NormalizesAndEnforcesItsOwnRpcAndEventNamespaces()
    {
        var owner = ProtocolExecutionOwner.ForPlugin(new ProtocolOwnerIdentity("Community.Health", "1.0.0"));

        Assert.Equal("community.health", owner.Plugin!.Id);
        Assert.Equal("plugin.community.health.rpc.", owner.GetOwnedNamespace(ProtocolCatalogEntryKind.Rpc));
        Assert.Equal("plugin.community.health.event.", owner.GetOwnedNamespace(ProtocolCatalogEntryKind.Event));
    }

    [Fact]
    public void BuiltInNamePolicy_SeparatesRpcAndEventNamespaces()
    {
        ProtocolCatalogNamePolicy.ValidateOwnedName(
            ProtocolExecutionOwner.BuiltIn,
            "mcsl.daemon.ping",
            ProtocolCatalogEntryKind.Rpc);
        ProtocolCatalogNamePolicy.ValidateOwnedName(
            ProtocolExecutionOwner.BuiltIn,
            "rpc.discover",
            ProtocolCatalogEntryKind.Rpc);
        ProtocolCatalogNamePolicy.ValidateOwnedName(
            ProtocolExecutionOwner.BuiltIn,
            "mcsl.event.subscribe",
            ProtocolCatalogEntryKind.Rpc);
        ProtocolCatalogNamePolicy.ValidateOwnedName(
            ProtocolExecutionOwner.BuiltIn,
            "mcsl.event.unsubscribe",
            ProtocolCatalogEntryKind.Rpc);
        ProtocolCatalogNamePolicy.ValidateOwnedName(
            ProtocolExecutionOwner.BuiltIn,
            "mcsl.event.daemon.report",
            ProtocolCatalogEntryKind.Event);

        Assert.Throws<ArgumentException>(() => ProtocolCatalogNamePolicy.ValidateOwnedName(
            ProtocolExecutionOwner.BuiltIn,
            "mcsl.event.daemon.report",
            ProtocolCatalogEntryKind.Rpc));
        Assert.Throws<ArgumentException>(() => ProtocolCatalogNamePolicy.ValidateOwnedName(
            ProtocolExecutionOwner.BuiltIn,
            "mcsl.daemon.ping",
            ProtocolCatalogEntryKind.Event));
        Assert.Throws<ArgumentException>(() => ProtocolCatalogNamePolicy.ValidateOwnedName(
            ProtocolExecutionOwner.BuiltIn,
            "mcsl.event.report",
            ProtocolCatalogEntryKind.Event));
        Assert.Throws<ArgumentException>(() => ProtocolCatalogNamePolicy.ValidateOwnedName(
            ProtocolExecutionOwner.BuiltIn,
            "mcsl.daemon",
            ProtocolCatalogEntryKind.Rpc));
        Assert.Throws<ArgumentException>(() => ProtocolCatalogNamePolicy.ValidateOwnedName(
            ProtocolExecutionOwner.BuiltIn,
            "mcsl.event",
            ProtocolCatalogEntryKind.Rpc));
    }

    [Fact]
    public void NamePolicy_RejectsRpcAndEventWireNameCollision()
    {
        Assert.Throws<ArgumentException>(() => ProtocolCatalogNamePolicy.EnsureDistinctWireNames(
            [new RpcMethod("mcsl.event.daemon.report")],
            [new EventName("mcsl.event.daemon.report")]));
    }

    [Fact]
    public void Bindings_DoNotRetainDescriptorOrJsonMetadataState()
    {
        var bindingTypes = new[]
        {
            typeof(RpcBinding),
            typeof(RpcBinding<,>),
            typeof(EventBinding),
            typeof(EventBinding<>),
            typeof(EventBinding<,>)
        };

        var retainedTypes = bindingTypes
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(property => property.PropertyType)
                .Concat(type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Select(field => field.FieldType)));

        Assert.DoesNotContain(retainedTypes, type =>
            typeof(RpcDescriptor).IsAssignableFrom(type) ||
            typeof(EventDescriptor).IsAssignableFrom(type) ||
            typeof(System.Text.Json.Serialization.Metadata.JsonTypeInfo).IsAssignableFrom(type));
    }

    [Fact]
    public void RegisterBuiltInRpc_DuplicateBindingFailureLeavesNoDefinitionResidue()
    {
        var builder = CreateBuilder();
        var descriptor = Rpc("mcsl.daemon.ping");
        builder.AddRpcBinding(descriptor.Method, PingBinding());

        Assert.Throws<ArgumentException>(() => builder.RegisterBuiltInRpc(descriptor, PingBinding()));
        Assert.Throws<InvalidOperationException>(() => builder.Freeze());
        Assert.Null(builder.Catalog);
    }

    [Fact]
    public void RegisterBuiltInEvent_DuplicateBindingFailureLeavesNoDefinitionResidue()
    {
        var builder = CreateBuilder();
        var descriptor = Event("mcsl.event.daemon.report");
        builder.AddEventBinding(descriptor.Name, DaemonReportBinding());

        Assert.Throws<ArgumentException>(() => builder.RegisterBuiltInEvent(descriptor, DaemonReportBinding()));
        Assert.Throws<InvalidOperationException>(() => builder.Freeze());
        Assert.Null(builder.Catalog);
    }

    [Fact]
    public void Freeze_RejectsMissingDefinitionBinding()
    {
        var builder = CreateBuilder();
        builder.AddRpcDefinition(ProtocolExecutionOwner.BuiltIn, Rpc("mcsl.daemon.ping"));

        Assert.Throws<InvalidOperationException>(() => builder.Freeze());
    }

    [Fact]
    public void Freeze_RejectsBindingWithNoDefinition()
    {
        var builder = CreateBuilder();
        var descriptor = Rpc("mcsl.daemon.ping");
        builder.AddRpcBinding(descriptor.Method, PingBinding());

        Assert.Throws<InvalidOperationException>(() => builder.Freeze());
    }

    [Fact]
    public void Freeze_RejectsRpcRequestOrResultTypeMismatch()
    {
        var builder = CreateBuilder();
        var descriptor = Rpc("mcsl.daemon.ping");
        builder.AddRpcDefinition(ProtocolExecutionOwner.BuiltIn, descriptor);
        builder.AddRpcBinding(
            descriptor.Method,
            new RpcBinding<EmptyRequest, UnitResult>(
                ProtocolExecutionOwner.BuiltIn,
                static (_, _, _) => ValueTask.FromResult<UnitResult>(null!)));

        Assert.Throws<ArgumentException>(() => builder.Freeze());
    }

    [Fact]
    public void Freeze_RejectsEventDataOrMetaTypeMismatch()
    {
        var builder = CreateBuilder();
        var descriptor = Event("mcsl.event.instance.log");
        builder.AddEventDefinition(ProtocolExecutionOwner.BuiltIn, descriptor);
        builder.AddEventBinding(
            descriptor.Name,
            new EventBinding<InstanceLogEventData>(ProtocolExecutionOwner.BuiltIn));

        Assert.Throws<ArgumentException>(() => builder.Freeze());
    }

    [Fact]
    public void Freeze_IsOneShotAndRejectsPostFreezeMutation()
    {
        var builder = CreateBuilder();
        AddPing(builder);
        AddDaemonReport(builder);
        builder.Freeze();

        Assert.Throws<InvalidOperationException>(() => builder.Freeze());
        Assert.Throws<InvalidOperationException>(() => builder.AddRpcDefinition(ProtocolExecutionOwner.BuiltIn, Rpc("mcsl.daemon.ping")));
        var descriptor = Event("mcsl.event.daemon.report");
        Assert.Throws<InvalidOperationException>(() => builder.AddEventBinding(descriptor.Name, DaemonReportBinding()));
    }

    [Fact]
    public async Task FrozenCatalog_ConcurrentLookupsAreStableForHitsAndMisses()
    {
        var builder = CreateBuilder();
        AddPing(builder);
        AddDaemonReport(builder);
        var catalog = builder.Freeze();
        const int participantCount = 16;
        const int iterations = 2_048;
        using var start = new Barrier(participantCount + 1);
        var rpcHit = new RpcMethod("mcsl.daemon.ping");
        var rpcMiss = new RpcMethod("mcsl.daemon.unknown");
        var eventHit = new EventName("mcsl.event.daemon.report");
        var eventMiss = new EventName("mcsl.event.unknown");

        var lookups = Enumerable.Range(0, participantCount).Select(_ =>
            Task.Factory.StartNew(
                () =>
                {
                    try
                    {
                        if (!start.SignalAndWait(TimeSpan.FromSeconds(10)))
                        {
                            return LookupObservation.Failed(new TimeoutException("A lookup worker did not reach the shared start barrier."));
                        }

                        var rpcHits = 0;
                        var rpcMisses = 0;
                        var eventHits = 0;
                        var eventMisses = 0;
                        var invariantViolations = 0;
                        FrozenRpcBinding? firstRpc = null;
                        FrozenEventBinding? firstEvent = null;

                        for (var iteration = 0; iteration < iterations; iteration++)
                        {
                            if (catalog.TryGetRpc(rpcHit, out var rpc))
                            {
                                rpcHits++;
                                firstRpc ??= rpc;
                                if (!ReferenceEquals(firstRpc, rpc) || rpc.Descriptor.Method.Value != "mcsl.daemon.ping")
                                {
                                    invariantViolations++;
                                }
                            }

                            if (!catalog.TryGetRpc(rpcMiss, out var missingRpc) && missingRpc is null)
                            {
                                rpcMisses++;
                            }

                            if (catalog.TryGetEvent(eventHit, out var @event))
                            {
                                eventHits++;
                                firstEvent ??= @event;
                                if (!ReferenceEquals(firstEvent, @event) || @event.Descriptor.Name.Value != "mcsl.event.daemon.report")
                                {
                                    invariantViolations++;
                                }
                            }

                            if (!catalog.TryGetEvent(eventMiss, out var missingEvent) && missingEvent is null)
                            {
                                eventMisses++;
                            }
                        }

                        return new LookupObservation(
                            rpcHits,
                            rpcMisses,
                            eventHits,
                            eventMisses,
                            invariantViolations,
                            null);
                    }
                    catch (Exception exception)
                    {
                        return LookupObservation.Failed(exception);
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)).ToArray();

        var mainReachedBarrier = start.SignalAndWait(TimeSpan.FromSeconds(10));
        var observations = await Task.WhenAll(lookups).WaitAsync(TimeSpan.FromSeconds(20));

        Assert.True(mainReachedBarrier);
        Assert.Equal(participantCount, observations.Length);
        Assert.All(observations, observation =>
        {
            Assert.Null(observation.Error);
            Assert.Equal(iterations, observation.RpcHits);
            Assert.Equal(iterations, observation.RpcMisses);
            Assert.Equal(iterations, observation.EventHits);
            Assert.Equal(iterations, observation.EventMisses);
            Assert.Equal(0, observation.InvariantViolations);
        });
    }

    [Fact]
    public void CatalogBindingSurface_ContainsNoPhaseFourTransportConcepts()
    {
        var catalogTypes = typeof(ProtocolCatalogBuilder).Assembly
            .GetTypes()
            .Where(type => type.Namespace == typeof(ProtocolCatalogBuilder).Namespace)
            .ToArray();

        var members = catalogTypes
            .SelectMany(type => type.GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .ToArray();

        var exposedTypes = members
            .SelectMany(GetMemberTypes)
            .Append(typeof(ProtocolCatalogBuilder));

        Assert.DoesNotContain(exposedTypes, IsPhaseFourTransportType);
        Assert.DoesNotContain(catalogTypes.Select(type => type.Name).Concat(members.Select(member => member.Name)), name =>
            name.Contains("DownloadRead", StringComparison.Ordinal) ||
            name.Contains("UploadAcknowledgement", StringComparison.Ordinal) ||
            name.Contains("ConnectionWriter", StringComparison.Ordinal));
    }

    private static IEnumerable<Type> GetMemberTypes(MemberInfo member) => member switch
    {
        MethodBase method => method.GetParameters().Select(parameter => parameter.ParameterType)
            .Append(method is MethodInfo methodInfo ? methodInfo.ReturnType : typeof(void)),
        PropertyInfo property => [property.PropertyType],
        FieldInfo field => [field.FieldType],
        EventInfo @event => [@event.EventHandlerType!],
        _ => []
    };

    private static bool IsPhaseFourTransportType(Type type)
    {
        if (type.IsByRef || type.IsPointer || type.IsArray)
        {
            return IsPhaseFourTransportType(type.GetElementType()!);
        }

        if (type.IsGenericType)
        {
            return type.GetGenericArguments().Any(IsPhaseFourTransportType);
        }

        var assemblyName = type.Assembly.GetName().Name;
        return assemblyName?.StartsWith("TouchSocket", StringComparison.Ordinal) == true ||
               type.FullName?.Contains("WebSocket", StringComparison.Ordinal) == true;
    }

    private static ProtocolCatalogBuilder CreateBuilder() =>
        new(new OpenRpcInfo("Catalog tests", "1.0.0"));

    private static RpcDescriptor Rpc(string method) =>
        BuiltInProtocolDefinitions.Rpcs.Single(descriptor => descriptor.Method.Value == method);

    private static EventDescriptor Event(string name) =>
        BuiltInProtocolDefinitions.Events.Single(descriptor => descriptor.Name.Value == name);

    private static void AddPing(ProtocolCatalogBuilder builder)
    {
        var descriptor = Rpc("mcsl.daemon.ping");
        builder.RegisterBuiltInRpc(descriptor, PingBinding());
    }

    private static void AddPermissions(ProtocolCatalogBuilder builder)
    {
        var descriptor = Rpc("mcsl.auth.permissions.get");
        builder.RegisterBuiltInRpc(
            descriptor,
            new RpcBinding<EmptyRequest, PermissionsResult>(
                ProtocolExecutionOwner.BuiltIn,
                static (_, _, _) => ValueTask.FromResult<PermissionsResult>(null!)));
    }

    private static void AddDaemonReport(ProtocolCatalogBuilder builder)
    {
        var descriptor = Event("mcsl.event.daemon.report");
        builder.RegisterBuiltInEvent(descriptor, DaemonReportBinding());
    }

    private static RpcBinding<EmptyRequest, PingResult> PingBinding(ProtocolExecutionOwner? owner = null) =>
        new(
            owner ?? ProtocolExecutionOwner.BuiltIn,
            static (_, _, _) => ValueTask.FromResult<PingResult>(null!));

    private static EventBinding<DaemonReportEventData> DaemonReportBinding() =>
        new(ProtocolExecutionOwner.BuiltIn);

    private sealed record LookupObservation(
        int RpcHits,
        int RpcMisses,
        int EventHits,
        int EventMisses,
        int InvariantViolations,
        Exception? Error)
    {
        public static LookupObservation Failed(Exception error) => new(0, 0, 0, 0, 0, error);
    }
}
