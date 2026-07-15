using System.Collections.Immutable;
using System.Reflection;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests;

public sealed class ProtocolRpcExecutionTests
{
    [Fact]
    public void NormalOk_HasNoAttachment()
    {
        var value = new PingResult(123);

        var execution = ProtocolRpcExecution<PingResult>.Ok(value);

        Assert.True(execution.Result.IsOk(out var actual));
        Assert.Same(value, actual);
        Assert.Null(execution.DownloadAttachment);
    }

    [Fact]
    public void DefaultExecutionsRejectPropertyAccess()
    {
        var typed = default(ProtocolRpcExecution<PingResult>);
        var erased = default(ErasedProtocolRpcExecution);

        Assert.Throws<InvalidOperationException>(() => _ = typed.Result);
        Assert.Throws<InvalidOperationException>(() => _ = typed.DownloadAttachment);
        Assert.Throws<InvalidOperationException>(() => _ = erased.Result);
        Assert.Throws<InvalidOperationException>(() => _ = erased.DownloadAttachment);
    }

    [Fact]
    public void NormalOk_RejectsNullAndDownloadMetadata()
    {
        Assert.Throws<ArgumentNullException>(() => ProtocolRpcExecution<string>.Ok(null!));
        Assert.Throws<InvalidOperationException>(() => ProtocolRpcExecution<DownloadReadResult>.Ok(
            new DownloadReadResult(Guid.NewGuid(), 0, 0, true)));
    }

    [Fact]
    public void Err_PreservesErrorIdentityAndHasNoAttachment()
    {
        var error = new InternalDaemonError("test.failure", "Expected failure.");

        var execution = ProtocolRpcExecution<PingResult>.Err(error);

        Assert.True(execution.Result.IsErr(out var actual));
        Assert.Same(error, actual);
        Assert.Null(execution.DownloadAttachment);
    }

    [Fact]
    public void DownloadOk_PreservesMatchingMetadataAndData()
    {
        var sessionId = Guid.NewGuid();
        var data = ImmutableArray.Create<byte>(1, 2, 3);
        var metadata = new DownloadReadResult(sessionId, 7, data.Length, true);
        var attachment = new ProtocolDownloadAttachment(sessionId, 7, data, true);

        var execution = ProtocolRpcExecution<DownloadReadResult>.DownloadOk(metadata, attachment);

        Assert.True(execution.Result.IsOk(out var actual));
        Assert.Same(metadata, actual);
        Assert.Same(attachment, execution.DownloadAttachment);
        Assert.Equal(data, attachment.Data);
    }

    [Fact]
    public void DownloadOk_RejectsEveryMetadataMismatch()
    {
        var sessionId = Guid.NewGuid();
        var data = ImmutableArray.Create<byte>(1, 2, 3);
        var metadata = new DownloadReadResult(sessionId, 7, data.Length, true);

        Assert.Throws<ArgumentException>(() => ProtocolRpcExecution<DownloadReadResult>.DownloadOk(
            metadata,
            new ProtocolDownloadAttachment(Guid.NewGuid(), 7, data, true)));
        Assert.Throws<ArgumentException>(() => ProtocolRpcExecution<DownloadReadResult>.DownloadOk(
            metadata,
            new ProtocolDownloadAttachment(sessionId, 8, data, true)));
        Assert.Throws<ArgumentException>(() => ProtocolRpcExecution<DownloadReadResult>.DownloadOk(
            metadata,
            new ProtocolDownloadAttachment(sessionId, 7, [1, 2], true)));
        Assert.Throws<ArgumentException>(() => ProtocolRpcExecution<DownloadReadResult>.DownloadOk(
            metadata,
            new ProtocolDownloadAttachment(sessionId, 7, data, false)));
    }

    [Fact]
    public void DownloadAttachment_RejectsInvalidIdentityOffsetAndData()
    {
        Assert.Throws<ArgumentException>(() => new ProtocolDownloadAttachment(
            Guid.Empty,
            0,
            [],
            false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProtocolDownloadAttachment(
            Guid.NewGuid(),
            -1,
            [],
            false));
        Assert.Throws<ArgumentException>(() => new ProtocolDownloadAttachment(
            Guid.NewGuid(),
            0,
            default,
            false));
    }

    [Fact]
    public void InvocationContext_CapabilitiesAreOptionalAndIsolated()
    {
        var empty = new ProtocolInvocationContext(ProtocolExecutionOwner.BuiltIn);
        var permissionView = new TestPermissionView(["instance.read"]);
        var subscriptions = new TestSubscriptionOperations();
        var populated = new ProtocolInvocationContext(
            ProtocolExecutionOwner.BuiltIn,
            permissionView,
            subscriptions);

        Assert.Null(empty.PermissionView);
        Assert.Null(empty.SubscriptionOperations);
        Assert.Same(permissionView, populated.PermissionView);
        Assert.Same(subscriptions, populated.SubscriptionOperations);
        Assert.True(permissionView.Permissions.SequenceEqual(["instance.read"]));

        var request = new EventSubscriptionRequest("mcsl.event.daemon.report");
        Assert.True(subscriptions.Subscribe(request).IsOk(out _));
        Assert.True(subscriptions.Unsubscribe(request).IsOk(out _));
        Assert.Equal(1, subscriptions.SubscribeCount);
        Assert.Equal(1, subscriptions.UnsubscribeCount);
    }

    [Fact]
    public void FrozenCatalogAccessor_IsUnavailableBeforePublishAndRejectsSecondPublish()
    {
        var accessor = new FrozenProtocolCatalogAccessor();
        var first = CreateFrozenCatalog();
        var second = CreateFrozenCatalog();

        Assert.False(accessor.TryGet(out var unavailable));
        Assert.Null(unavailable);
        Assert.Throws<InvalidOperationException>(accessor.GetRequired);

        accessor.Publish(first);

        Assert.True(accessor.TryGet(out var published));
        Assert.Same(first, published);
        Assert.Same(first, accessor.GetRequired());
        Assert.Throws<InvalidOperationException>(() => accessor.Publish(second));
        Assert.Same(first, accessor.GetRequired());
    }

    [Fact]
    public async Task FrozenCatalogAccessor_ConcurrentReadersObserveOnlyPublishedCatalog()
    {
        var accessor = new FrozenProtocolCatalogAccessor();
        var catalog = CreateFrozenCatalog();
        const int readerCount = 16;
        using var start = new Barrier(readerCount + 1);
        var readers = Enumerable.Range(0, readerCount).Select(_ => Task.Run(() =>
        {
            Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10)));
            FrozenProtocolCatalog? observed;
            while (!accessor.TryGet(out observed))
            {
                Thread.Yield();
            }

            return observed;
        })).ToArray();

        Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10)));
        accessor.Publish(catalog);
        var observations = await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.All(observations, observation => Assert.Same(catalog, observation));
    }

    [Fact]
    public async Task FrozenCatalogAccessor_ConcurrentPublishersAllowExactlyOneWinner()
    {
        var accessor = new FrozenProtocolCatalogAccessor();
        var candidates = Enumerable.Range(0, 16)
            .Select(_ => CreateFrozenCatalog())
            .ToArray();
        using var start = new Barrier(candidates.Length + 1);
        var publishers = candidates.Select(candidate => Task.Run(() =>
        {
            Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10)));
            try
            {
                accessor.Publish(candidate);
                return (Catalog: candidate, Published: true);
            }
            catch (InvalidOperationException)
            {
                return (Catalog: candidate, Published: false);
            }
        })).ToArray();

        Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10)));
        var observations = await Task.WhenAll(publishers).WaitAsync(TimeSpan.FromSeconds(10));
        var winner = Assert.Single(observations, observation => observation.Published);

        Assert.Same(winner.Catalog, accessor.GetRequired());
        Assert.Equal(candidates.Length - 1, observations.Count(observation => !observation.Published));
    }

    [Fact]
    public void BindingTypesAndDescriptorIdentityRemainUnchanged()
    {
        var descriptor = BuiltInProtocolDefinitions.Rpcs.Single(
            candidate => candidate.Method.Value == "mcsl.daemon.ping");
        var binding = new RpcBinding<EmptyRequest, PingResult>(
            ProtocolExecutionOwner.BuiltIn,
            static (_, _, _) => Task.FromResult(
                ProtocolRpcExecution<PingResult>.Ok(new PingResult(0))));
        var builder = new ProtocolCatalogBuilder(new OpenRpcInfo("Execution tests", "1.0.0"));
        builder.RegisterBuiltInRpc(descriptor, binding);

        var frozen = builder.Freeze();

        Assert.Equal(descriptor.RequestTypeInfo.Type, binding.RequestType);
        Assert.Equal(descriptor.ResultTypeInfo.Type, binding.ResultType);
        Assert.Same(descriptor, frozen.Rpcs[descriptor.Method].Descriptor);
        Assert.Same(binding, frozen.Rpcs[descriptor.Method].Binding);
        Assert.Equal(
            typeof(Task<ProtocolRpcExecution<PingResult>>),
            typeof(ProtocolRpcHandler<EmptyRequest, PingResult>).GetMethod("Invoke")!.ReturnType);
    }

    [Fact]
    public void DownloadFactory_CannotConstructAnObjectExecutionThroughGenericOrReflectionShape()
    {
        var factory = typeof(ProtocolRpcExecution<object>).GetMethod(
            nameof(ProtocolRpcExecution<DownloadReadResult>.DownloadOk),
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;

        Assert.Equal(typeof(ProtocolRpcExecution<DownloadReadResult>), factory.ReturnType);
        Assert.Equal(
            [typeof(DownloadReadResult), typeof(ProtocolDownloadAttachment)],
            factory.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.DoesNotContain(
            typeof(ProtocolRpcExecution<object>).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic),
            method => method.ReturnType == typeof(ProtocolRpcExecution<object>) &&
                      method.GetParameters().Any(parameter => parameter.ParameterType == typeof(ProtocolDownloadAttachment)));
    }

    [Fact]
    public void InvocationContextAndCapabilities_HaveExactSeparatedMemberSets()
    {
        Assert.Equal(
            [nameof(ProtocolInvocationContext.ExecutionOwner), nameof(ProtocolInvocationContext.FileSessionOperations), nameof(ProtocolInvocationContext.PermissionView), nameof(ProtocolInvocationContext.SubscriptionOperations)],
            typeof(ProtocolInvocationContext)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));

        var permissionMembers = InterfaceMemberNames(typeof(IProtocolPermissionView));
        var subscriptionMembers = InterfaceMemberNames(typeof(IProtocolSubscriptionOperations));
        var fileSessionMembers = InterfaceMemberNames(typeof(IProtocolFileSessionOperations));

        Assert.Equal([nameof(IProtocolPermissionView.Permissions)], permissionMembers);
        Assert.Equal(
            [nameof(IProtocolSubscriptionOperations.Subscribe), nameof(IProtocolSubscriptionOperations.Unsubscribe)],
            subscriptionMembers);
        Assert.Equal(
            [nameof(IProtocolFileSessionOperations.CancelUploadAsync), nameof(IProtocolFileSessionOperations.CloseDownloadAsync), nameof(IProtocolFileSessionOperations.CloseUploadAsync), nameof(IProtocolFileSessionOperations.OpenDownloadAsync), nameof(IProtocolFileSessionOperations.OpenUploadAsync), nameof(IProtocolFileSessionOperations.ReadDownloadChunkAsync)],
            fileSessionMembers);
        Assert.Empty(permissionMembers.Intersect(subscriptionMembers, StringComparer.Ordinal));
        Assert.Empty(permissionMembers.Intersect(fileSessionMembers, StringComparer.Ordinal));
        Assert.Empty(subscriptionMembers.Intersect(fileSessionMembers, StringComparer.Ordinal));
    }

    [Fact]
    public void ExecutionSurface_IsInternalAndContainsNoTransportTypes()
    {
        var surfaceTypes = new[]
        {
            typeof(ProtocolRpcExecution<>),
            typeof(ProtocolDownloadAttachment),
            typeof(ProtocolInvocationContext),
            typeof(IProtocolPermissionView),
            typeof(IProtocolSubscriptionOperations),
            typeof(IProtocolFileSessionOperations),
            typeof(IFrozenProtocolCatalogAccessor),
            typeof(FrozenProtocolCatalogAccessor)
        };

        Assert.All(surfaceTypes, type => Assert.False(type.IsPublic));

        var exposedTypes = surfaceTypes
            .SelectMany(type => type.GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .SelectMany(GetMemberTypes)
            .ToArray();
        Assert.DoesNotContain(exposedTypes, IsForbiddenTransportType);
        Assert.DoesNotContain(
            surfaceTypes.Select(type => type.Name),
            name => name.Contains("Writer", StringComparison.Ordinal) ||
                    name.Contains("Frame", StringComparison.Ordinal) ||
                    name.Contains("Socket", StringComparison.Ordinal) ||
                    name.Contains("Queue", StringComparison.Ordinal));
    }

    private static FrozenProtocolCatalog CreateFrozenCatalog()
    {
        var descriptor = BuiltInProtocolDefinitions.Rpcs.Single(
            candidate => candidate.Method.Value == "mcsl.daemon.ping");
        var builder = new ProtocolCatalogBuilder(new OpenRpcInfo("Accessor tests", "1.0.0"));
        builder.RegisterBuiltInRpc(
            descriptor,
            new RpcBinding<EmptyRequest, PingResult>(
                ProtocolExecutionOwner.BuiltIn,
                static (_, _, _) => Task.FromResult(
                    ProtocolRpcExecution<PingResult>.Ok(new PingResult(0)))));
        return builder.Freeze();
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

    private static string[] InterfaceMemberNames(Type type) =>
        type.GetMembers(BindingFlags.Instance | BindingFlags.Public)
            .Where(member => member.MemberType is MemberTypes.Property or MemberTypes.Method)
            .Where(member => !member.Name.StartsWith("get_", StringComparison.Ordinal))
            .Select(member => member.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool IsForbiddenTransportType(Type type)
    {
        if (type.IsByRef || type.IsPointer || type.IsArray)
        {
            return IsForbiddenTransportType(type.GetElementType()!);
        }

        if (type.IsGenericType)
        {
            return type.GetGenericArguments().Any(IsForbiddenTransportType);
        }

        var assemblyName = type.Assembly.GetName().Name;
        return assemblyName?.StartsWith("TouchSocket", StringComparison.Ordinal) == true ||
               type.FullName?.Contains("WebSocket", StringComparison.Ordinal) == true ||
               type.FullName?.Contains("IServiceProvider", StringComparison.Ordinal) == true;
    }

    private sealed record TestPermissionView(ImmutableArray<string> Permissions) : IProtocolPermissionView;

    private sealed class TestSubscriptionOperations : IProtocolSubscriptionOperations
    {
        public int SubscribeCount { get; private set; }
        public int UnsubscribeCount { get; private set; }

        public Result<Unit, DaemonError> Subscribe(EventSubscriptionRequest request)
        {
            SubscribeCount++;
            return Result.Ok<Unit, DaemonError>(Unit.Default);
        }

        public Result<Unit, DaemonError> Unsubscribe(EventSubscriptionRequest request)
        {
            UnsubscribeCount++;
            return Result.Ok<Unit, DaemonError>(Unit.Default);
        }
    }
}
