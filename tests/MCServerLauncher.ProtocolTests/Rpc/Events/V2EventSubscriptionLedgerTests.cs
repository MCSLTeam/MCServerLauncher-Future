using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Dispatch;
using MCServerLauncher.Daemon.Remote.Rpc.Events;
using MCServerLauncher.Daemon.Remote.Rpc.Transport;

namespace MCServerLauncher.ProtocolTests.Rpc.Events;

public sealed class V2EventSubscriptionLedgerTests
{
    private static readonly BuiltInProtocolJsonContext Json = BuiltInProtocolJsonContext.Default;
    private static readonly Guid InstanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task UnknownEventAndPermissionDenialReturnTypedErrorsWithoutMutation()
    {
        var catalog = CreateCatalog();
        await using var deniedOwner = Owner();
        var denied = new V2EventSubscriptionLedger(catalog, deniedOwner);

        var unknown = denied.Subscribe(new EventSubscriptionRequest("mcsl.event.unknown"));
        var permission = denied.Subscribe(new EventSubscriptionRequest("mcsl.event.instance.log"));

        AssertError<NotFoundDaemonError>(unknown, "event.not_found");
        AssertError<PermissionDaemonError>(permission, "permission.denied");
        Assert.Equal(0, denied.Count);
    }

    [Fact]
    public async Task MetaPresenceMatrixEnforcesOmittedRequiredAndOptionalSemantics()
    {
        var catalog = CreateCatalog();
        await using var owner = Owner("*");
        var ledger = new V2EventSubscriptionLedger(catalog, owner);
        var objectFilter = Meta("{\"instance_id\":\"11111111-1111-1111-1111-111111111111\"}");

        AssertOk(ledger.Subscribe(new EventSubscriptionRequest("mcsl.event.daemon.report")));
        AssertError<ValidationDaemonError>(
            ledger.Subscribe(new EventSubscriptionRequest("mcsl.event.daemon.report", EventMetaFilter.ExplicitNull)),
            "event.meta.invalid");
        AssertError<ValidationDaemonError>(
            ledger.Subscribe(new EventSubscriptionRequest("mcsl.event.daemon.report", objectFilter)),
            "event.meta.invalid");

        AssertOk(ledger.Subscribe(new EventSubscriptionRequest("mcsl.event.instance.log")));
        AssertError<ValidationDaemonError>(
            ledger.Subscribe(new EventSubscriptionRequest("mcsl.event.instance.log", EventMetaFilter.ExplicitNull)),
            "event.meta.invalid");
        AssertOk(ledger.Subscribe(new EventSubscriptionRequest("mcsl.event.instance.log", objectFilter)));

        Assert.True(V2EventMetaCanonicalizer.PrepareValue(
            OpenRpcEventFieldPresence.Optional,
            Json.InstanceLogEventMeta,
            EventMetaFilter.Missing).IsOk(out _));
        Assert.True(V2EventMetaCanonicalizer.PrepareValue(
            OpenRpcEventFieldPresence.Optional,
            Json.InstanceLogEventMeta,
            EventMetaFilter.ExplicitNull).IsOk(out _));
        Assert.True(V2EventMetaCanonicalizer.PrepareValue(
            OpenRpcEventFieldPresence.Optional,
            Json.InstanceLogEventMeta,
            objectFilter).IsOk(out _));
    }

    [Fact]
    public void OptionalMetaMatchingDistinguishesMissingNullAndCanonicalObject()
    {
        var wildcard = PrepareOptional(EventMetaFilter.Missing);
        var nullOnly = PrepareOptional(EventMetaFilter.ExplicitNull);
        var objectOnly = PrepareOptional(Meta(
            "{\"instance_id\":\"11111111-1111-1111-1111-111111111111\"}"));
        var matchingObject = JsonSerializer.SerializeToUtf8Bytes(
            new InstanceLogEventMeta(InstanceId),
            Json.InstanceLogEventMeta).ToImmutableArray();
        var differentObject = JsonSerializer.SerializeToUtf8Bytes(
            new InstanceLogEventMeta(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            Json.InstanceLogEventMeta).ToImmutableArray();

        Assert.True(V2EventMetaMatcher.Matches(OpenRpcEventFieldPresence.Optional, wildcard, V2EventMetaValueKind.Omitted, []));
        Assert.True(V2EventMetaMatcher.Matches(OpenRpcEventFieldPresence.Optional, wildcard, V2EventMetaValueKind.ExplicitNull, []));
        Assert.True(V2EventMetaMatcher.Matches(OpenRpcEventFieldPresence.Optional, wildcard, V2EventMetaValueKind.Object, matchingObject));

        Assert.False(V2EventMetaMatcher.Matches(OpenRpcEventFieldPresence.Optional, nullOnly, V2EventMetaValueKind.Omitted, []));
        Assert.True(V2EventMetaMatcher.Matches(OpenRpcEventFieldPresence.Optional, nullOnly, V2EventMetaValueKind.ExplicitNull, []));
        Assert.False(V2EventMetaMatcher.Matches(OpenRpcEventFieldPresence.Optional, nullOnly, V2EventMetaValueKind.Object, matchingObject));

        Assert.False(V2EventMetaMatcher.Matches(OpenRpcEventFieldPresence.Optional, objectOnly, V2EventMetaValueKind.Omitted, []));
        Assert.False(V2EventMetaMatcher.Matches(OpenRpcEventFieldPresence.Optional, objectOnly, V2EventMetaValueKind.ExplicitNull, []));
        Assert.True(V2EventMetaMatcher.Matches(OpenRpcEventFieldPresence.Optional, objectOnly, V2EventMetaValueKind.Object, matchingObject));
        Assert.False(V2EventMetaMatcher.Matches(OpenRpcEventFieldPresence.Optional, objectOnly, V2EventMetaValueKind.Object, differentObject));
    }

    [Theory]
    [InlineData("{\"unknown\":true}")]
    [InlineData("{\"instance_id\":null}")]
    [InlineData("{\"instance_id\":{\"nested\":null}}")]
    public async Task ObjectFiltersRejectUnknownNullAndNestedInvalidShapes(string json)
    {
        var catalog = CreateCatalog();
        await using var owner = Owner("*");
        var ledger = new V2EventSubscriptionLedger(catalog, owner);

        var result = ledger.Subscribe(new EventSubscriptionRequest("mcsl.event.instance.log", Meta(json)));

        AssertError<ValidationDaemonError>(result, "event.meta.invalid");
        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public void ObjectFilterInputRejectsDuplicatePropertiesAtAnyDepth()
    {
        Assert.Throws<JsonException>(() => Meta(
            "{\"instance_id\":\"11111111-1111-1111-1111-111111111111\",\"instance_id\":\"22222222-2222-2222-2222-222222222222\"}"));
        Assert.Throws<JsonException>(() => Meta("{\"outer\":{\"value\":1,\"value\":2}}"));
    }

    [Fact]
    public async Task CanonicalReorderedObjectsAreIdempotentAndExactUnsubscribePreservesOtherKinds()
    {
        var catalog = CreateCatalog();
        await using var owner = Owner("*");
        var ledger = new V2EventSubscriptionLedger(catalog, owner);
        var first = Meta(
            "{\"source_instance_id\":\"11111111-1111-1111-1111-111111111111\",\"rule_id\":\"22222222-2222-2222-2222-222222222222\"}");
        var reordered = Meta(
            "{\"rule_id\":\"22222222-2222-2222-2222-222222222222\",\"source_instance_id\":\"11111111-1111-1111-1111-111111111111\"}");

        AssertOk(ledger.Subscribe(new EventSubscriptionRequest("mcsl.event.notification")));
        AssertOk(ledger.Subscribe(new EventSubscriptionRequest("mcsl.event.notification", first)));
        AssertOk(ledger.Subscribe(new EventSubscriptionRequest("mcsl.event.notification", reordered)));
        Assert.Equal(2, ledger.Count);

        AssertOk(ledger.Unsubscribe(new EventSubscriptionRequest("mcsl.event.notification")));
        Assert.Equal(1, ledger.Count);
        AssertOk(ledger.Unsubscribe(new EventSubscriptionRequest("mcsl.event.notification", reordered)));
        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public async Task MatchUsesWildcardOrExactCanonicalMetaAndRejectsForeignBindings()
    {
        var catalog = CreateCatalog();
        await using var owner = Owner("*");
        var ledger = new V2EventSubscriptionLedger(catalog, owner);
        var log = catalog.Events[new EventName("mcsl.event.instance.log")];
        var notification = catalog.Events[new EventName("mcsl.event.notification")];
        var actual = V2CanonicalEventMeta.FromTypedObject(log, new InstanceLogEventMeta(InstanceId));

        AssertOk(ledger.Subscribe(new EventSubscriptionRequest(
            log.Descriptor.Name.Value,
            Meta("{\"instance_id\":\"11111111-1111-1111-1111-111111111111\"}"))));
        Assert.True(ledger.Matches(log, actual));
        Assert.False(ledger.Matches(log, V2CanonicalEventMeta.FromTypedObject(log, new InstanceLogEventMeta(Guid.NewGuid()))));
        Assert.False(ledger.Matches(notification, actual));

        Assert.Throws<ArgumentException>(() => V2CanonicalEventMeta.Omitted(log));
        Assert.Throws<ArgumentException>(() => V2CanonicalEventMeta.ExplicitNull(log));
        Assert.Throws<ArgumentException>(() => V2CanonicalEventMeta.FromTypedObject(log, new object()));
    }

    [Fact]
    public async Task ForeignFrozenBindingAndCanonicalizerOutputAreRejectedByExactIdentity()
    {
        var firstCatalog = CreateCatalog();
        var secondCatalog = CreateCatalog();
        var name = new EventName("mcsl.event.instance.log");
        var firstBinding = firstCatalog.Events[name];
        var secondBinding = secondCatalog.Events[name];
        Assert.Same(firstBinding.Descriptor, secondBinding.Descriptor);
        Assert.NotSame(firstBinding, secondBinding);

        await using var owner = Owner("*");
        var ledger = new V2EventSubscriptionLedger(firstCatalog, owner);
        AssertOk(ledger.Subscribe(new EventSubscriptionRequest(name.Value)));
        var foreignActual = V2CanonicalEventMeta.FromTypedObject(
            secondBinding,
            new InstanceLogEventMeta(InstanceId));

        Assert.False(ledger.Matches(secondBinding, foreignActual));
        Assert.False(ledger.Matches(firstBinding, foreignActual));
        Assert.Equal(1, ledger.Count);

        var mismatched = new V2EventSubscriptionLedger(
            firstCatalog,
            owner,
            new ForeignBindingCanonicalizer(secondBinding));
        var result = mismatched.Subscribe(new EventSubscriptionRequest(name.Value));
        AssertError<InternalDaemonError>(result, "event.meta.binding_mismatch");
        Assert.Equal(0, mismatched.Count);
    }

    [Fact]
    public async Task ClosedLedgerRejectsEveryOperationAndClearsSnapshot()
    {
        var catalog = CreateCatalog();
        await using var owner = Owner("*");
        var ledger = new V2EventSubscriptionLedger(catalog, owner);
        AssertOk(ledger.Subscribe(new EventSubscriptionRequest("mcsl.event.instance.log")));

        ledger.Close();

        AssertError<TransportDaemonError>(
            ledger.Subscribe(new EventSubscriptionRequest("mcsl.event.instance.log")),
            "connection.closed");
        AssertError<TransportDaemonError>(
            ledger.Unsubscribe(new EventSubscriptionRequest("mcsl.event.instance.log")),
            "connection.closed");
        AssertError<TransportDaemonError>(
            ledger.Subscribe(new EventSubscriptionRequest("mcsl.event.unknown")),
            "connection.closed");
        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public async Task CloseDuringCanonicalizationWinsFinalMutationLinearization()
    {
        var catalog = CreateCatalog();
        await using var owner = Owner("*");
        var canonicalizer = new BlockingCanonicalizer();
        var ledger = new V2EventSubscriptionLedger(catalog, owner, canonicalizer);
        var request = new EventSubscriptionRequest(
            "mcsl.event.instance.log",
            Meta("{\"instance_id\":\"11111111-1111-1111-1111-111111111111\"}"));

        var subscribe = Task.Run(() => ledger.Subscribe(request));
        await canonicalizer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        ledger.Close();
        canonicalizer.Release.TrySetResult();
        var result = await subscribe.WaitAsync(TimeSpan.FromSeconds(10));

        AssertError<TransportDaemonError>(result, "connection.closed");
        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public async Task RootWildcardPermissionAndAllFrozenDefinitionsAreCatalogDriven()
    {
        var catalog = CreateCatalog();
        await using var owner = Owner("*");
        var ledger = new V2EventSubscriptionLedger(catalog, owner);

        Assert.Equal(
            BuiltInProtocolDefinitions.Events.Select(static item => item.Name.Value),
            catalog.EventDefinitions.Select(static item => item.Name.Value));

        foreach (var definition in catalog.EventDefinitions)
            AssertOk(ledger.Subscribe(new EventSubscriptionRequest(definition.Name.Value)));

        Assert.Equal(catalog.EventDefinitions.Length, ledger.Count);
    }

    [Fact]
    public async Task DispatcherInjectsRealLedgerAndMapsExpectedErrors()
    {
        var catalog = CreateCatalog(includeSubscriptionRpcs: true);
        await using var owner = Owner("*");
        var ledger = new V2EventSubscriptionLedger(catalog, owner);
        var dispatcher = new V2RpcDispatcher(catalog, new NoOpDiagnosticSink());
        var context = new V2RpcConnectionContext(owner, ledger, owner.ConnectionToken);
        var subscribeMethod = BuiltInProtocolDefinitions.Rpcs
            .Single(item => item.Method.Value == "mcsl.event.subscribe")
            .Method.Value;
        var unsubscribeMethod = BuiltInProtocolDefinitions.Rpcs
            .Single(item => item.Method.Value == "mcsl.event.unsubscribe")
            .Method.Value;

        var subscribed = await dispatcher.DispatchAsync(
            Utf8($"{{\"jsonrpc\":\"2.0\",\"method\":\"{subscribeMethod}\",\"id\":1,\"params\":{{\"event\":\"mcsl.event.instance.log\"}}}}"),
            context);
        var unknown = await dispatcher.DispatchAsync(
            Utf8($"{{\"jsonrpc\":\"2.0\",\"method\":\"{subscribeMethod}\",\"id\":2,\"params\":{{\"event\":\"mcsl.event.unknown\"}}}}"),
            context);

        Assert.True(subscribed.HasResponse);
        Assert.Equal(1, ledger.Count);
        Assert.Contains("\"result\":{}", Encoding.UTF8.GetString(subscribed.ResponseUtf8.AsSpan()));
        Assert.Contains("\"code\":-32000", Encoding.UTF8.GetString(unknown.ResponseUtf8.AsSpan()));

        var unsubscribed = await dispatcher.DispatchAsync(
            Utf8($"{{\"jsonrpc\":\"2.0\",\"method\":\"{unsubscribeMethod}\",\"id\":4,\"params\":{{\"event\":\"mcsl.event.instance.log\"}}}}"),
            context);
        Assert.Contains("\"result\":{}", Encoding.UTF8.GetString(unsubscribed.ResponseUtf8.AsSpan()));
        Assert.Equal(0, ledger.Count);

        await using var deniedOwner = Owner();
        var deniedLedger = new V2EventSubscriptionLedger(catalog, deniedOwner);
        await using var authorizedOwner = Owner("*");
        var denied = await dispatcher.DispatchAsync(
            Utf8($"{{\"jsonrpc\":\"2.0\",\"method\":\"{subscribeMethod}\",\"id\":3,\"params\":{{\"event\":\"mcsl.event.instance.log\"}}}}"),
            new V2RpcConnectionContext(authorizedOwner, deniedLedger, CancellationToken.None));
        Assert.Contains("\"code\":-32001", Encoding.UTF8.GetString(denied.ResponseUtf8.AsSpan()));

        var closedLedger = new V2EventSubscriptionLedger(catalog, authorizedOwner);
        closedLedger.Close();
        var closed = await dispatcher.DispatchAsync(
            Utf8($"{{\"jsonrpc\":\"2.0\",\"method\":\"{subscribeMethod}\",\"id\":5,\"params\":{{\"event\":\"mcsl.event.instance.log\"}}}}"),
            new V2RpcConnectionContext(authorizedOwner, closedLedger, CancellationToken.None));
        Assert.Contains("\"code\":-32000", Encoding.UTF8.GetString(closed.ResponseUtf8.AsSpan()));
        Assert.Contains("\"daemon_error_code\":\"connection.closed\"", Encoding.UTF8.GetString(closed.ResponseUtf8.AsSpan()));
        Assert.Equal(0, closedLedger.Count);
    }

    private static FrozenProtocolCatalog CreateCatalog(bool includeSubscriptionRpcs = false)
    {
        var builder = new ProtocolCatalogBuilder(new OpenRpcInfo("events", "1.0.0"));
        RegisterEvent<InstanceCatalogChangedEventData>(builder, "mcsl.event.instance.catalog.changed");
        RegisterEvent<DaemonReportEventData>(builder, "mcsl.event.daemon.report");
        RegisterEvent<InstanceLogEventData, InstanceLogEventMeta>(builder, "mcsl.event.instance.log");
        RegisterEvent<NotificationEventData, NotificationEventMeta>(builder, "mcsl.event.notification");

        if (includeSubscriptionRpcs)
        {
            RegisterSubscriptionRpc(builder, "mcsl.event.subscribe", static (operations, request) => operations.Subscribe(request));
            RegisterSubscriptionRpc(builder, "mcsl.event.unsubscribe", static (operations, request) => operations.Unsubscribe(request));
        }

        return builder.Freeze();
    }

    private static void RegisterEvent<TData>(ProtocolCatalogBuilder builder, string name)
    {
        var descriptor = BuiltInProtocolDefinitions.Events.Single(item => item.Name.Value == name);
        builder.RegisterBuiltInEvent(descriptor, new EventBinding<TData>(ProtocolExecutionOwner.BuiltIn));
    }

    private static void RegisterEvent<TData, TMeta>(ProtocolCatalogBuilder builder, string name)
    {
        var descriptor = BuiltInProtocolDefinitions.Events.Single(item => item.Name.Value == name);
        builder.RegisterBuiltInEvent(descriptor, new EventBinding<TData, TMeta>(ProtocolExecutionOwner.BuiltIn));
    }

    private static void RegisterSubscriptionRpc(
        ProtocolCatalogBuilder builder,
        string method,
        Func<IProtocolSubscriptionOperations, EventSubscriptionRequest, RustyOptions.Result<RustyOptions.Unit, DaemonError>> operation)
    {
        var descriptor = (RpcDescriptor<EventSubscriptionRequest, UnitResult>)BuiltInProtocolDefinitions.Rpcs
            .Single(item => item.Method.Value == method);
        builder.RegisterBuiltInRpc(
            descriptor,
            new RpcBinding<EventSubscriptionRequest, UnitResult>(
                ProtocolExecutionOwner.BuiltIn,
                (context, request, cancellationToken) =>
                {
                    var subscriptions = context.SubscriptionOperations ??
                        throw new InvalidOperationException("Missing subscription operations.");
                    var result = operation(subscriptions, request);
                    return Task.FromResult(result.IsOk(out var ignored)
                        ? ProtocolRpcExecution<UnitResult>.Ok(new UnitResult())
                        : ProtocolRpcExecution<UnitResult>.Err(result.UnwrapErr()));
                }));
    }

    private static EventMetaFilter Meta(string json) => EventMetaFilter.FromObject(Utf8(json));

    private static V2PreparedEventMetaValue PrepareOptional(EventMetaFilter filter)
    {
        var result = V2EventMetaCanonicalizer.PrepareValue(
            OpenRpcEventFieldPresence.Optional,
            Json.InstanceLogEventMeta,
            filter);
        Assert.True(result.IsOk(out _));
        return result.Unwrap();
    }

    private static V2ConnectionOwner Owner(params string[] permissions) =>
        new(new NoOpSender(), permissions);

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static void AssertOk(RustyOptions.Result<RustyOptions.Unit, DaemonError> result) =>
        Assert.True(result.IsOk(out _));

    private static void AssertError<TError>(
        RustyOptions.Result<RustyOptions.Unit, DaemonError> result,
        string code)
        where TError : DaemonError
    {
        Assert.True(result.IsErr(out _));
        var error = Assert.IsType<TError>(result.UnwrapErr());
        Assert.Equal(code, error.Code);
    }

    private sealed class NoOpSender : IV2OutboundSender
    {
        public ValueTask SendAsync(V2OutboundFrame frame, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask CloseAsync(V2ConnectionCloseReason reason, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class NoOpDiagnosticSink : IV2RpcDiagnosticSink
    {
        public void RecordUnexpected(V2RpcUnexpectedDiagnostic diagnostic)
        {
        }

        public void RecordNotificationSuppressed(V2RpcNotificationSuppressionDiagnostic diagnostic)
        {
        }
    }

    private sealed class BlockingCanonicalizer : IV2EventMetaCanonicalizer
    {
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RustyOptions.Result<V2PreparedEventMetaFilter, DaemonError> Prepare(
            FrozenEventBinding binding,
            EventMetaFilter filter)
        {
            Entered.TrySetResult();
            Release.Task.GetAwaiter().GetResult();
            return V2EventMetaCanonicalizer.Instance.Prepare(binding, filter);
        }
    }

    private sealed class ForeignBindingCanonicalizer(FrozenEventBinding foreignBinding)
        : IV2EventMetaCanonicalizer
    {
        public RustyOptions.Result<V2PreparedEventMetaFilter, DaemonError> Prepare(
            FrozenEventBinding binding,
            EventMetaFilter filter) =>
            RustyOptions.Result.Ok<V2PreparedEventMetaFilter, DaemonError>(
                new V2PreparedEventMetaFilter(
                    foreignBinding,
                    EventMetaFilterKind.Missing,
                    []));
    }
}
