using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Dispatch;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests.Rpc.Dispatch;

public sealed class V2RpcDispatcherTests
{
    private static readonly BuiltInProtocolJsonContext ProtocolJson = BuiltInProtocolJsonContext.Default;

    [Fact]
    public async Task Dispatch_MapsFileSessionOperationsAndPreservesDownloadAttachment()
    {
        var sessionId = Guid.NewGuid();
        var data = ImmutableArray.Create<byte>(4, 5, 6);
        var operations = new RecordingFileSessionOperations(
            new DownloadChunk(7, data, true));
        var dispatcher = CreateBuiltInDispatcher<DownloadChunkRequest, DownloadReadResult>(
            "mcsl.file.download.read",
            async (context, request, token) =>
            {
                Assert.Same(operations, context.FileSessionOperations);
                var chunk = (await context.FileSessionOperations!.ReadDownloadChunkAsync(request, token)).Unwrap();
                return ProtocolRpcExecution<DownloadReadResult>.DownloadOk(
                    new DownloadReadResult(request.SessionId, chunk.Offset, chunk.Data.Length, chunk.IsFinal),
                    new ProtocolDownloadAttachment(request.SessionId, chunk.Offset, chunk.Data, chunk.IsFinal));
            });
        using var requestCancellation = new CancellationTokenSource();
        var connection = new V2RpcConnectionContext(
            new TestPermissionView(ImmutableArray.Create("mcsl.daemon.file.download")),
            null,
            CancellationToken.None,
            operations);

        var outcome = await dispatcher.DispatchAsync(
            Utf8($"{{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.file.download.read\",\"id\":1,\"params\":{{\"session_id\":\"{sessionId}\",\"offset\":0,\"maximum_length\":8}}}}"),
            connection,
            requestCancellation.Token);

        var attachment = Assert.IsType<ProtocolDownloadAttachment>(outcome.DownloadAttachment);
        Assert.Equal(sessionId, attachment.SessionId);
        Assert.Equal(7, attachment.Offset);
        Assert.Equal(data, attachment.Data);
        Assert.True(attachment.IsFinal);
        Assert.Equal(requestCancellation.Token, operations.ObservedToken);
        var response = JsonRpcWireParser.ParseSuccessResponse(outcome.ResponseUtf8.AsSpan());
        var metadata = Assert.IsType<DownloadReadResult>(response.Result.Deserialize(ProtocolJson.DownloadReadResult));
        Assert.Equal(sessionId, metadata.SessionId);
        Assert.Equal(7, metadata.Offset);
        Assert.Equal(data.Length, metadata.Length);
        Assert.True(metadata.IsFinal);
    }

    [Theory]
    [InlineData("", -32700)]
    [InlineData("{", -32700)]
    [InlineData("{} {}", -32700)]
    [InlineData("[]", -32600)]
    [InlineData("null", -32600)]
    [InlineData("{}", -32600)]
    [InlineData("{\"jsonrpc\":\"1.0\",\"method\":\"mcsl.daemon.ping\",\"id\":1}", -32600)]
    [InlineData("{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.daemon.ping\",\"id\":null}", -32600)]
    public async Task SyntaxAndProfileFailuresMapToExactJsonRpcErrors(string json, int expectedCode)
    {
        var dispatcher = CreatePingDispatcher(static (_, _, _) =>
            Task.FromResult(ProtocolRpcExecution<PingResult>.Ok(new PingResult(1))));

        var outcome = await dispatcher.DispatchAsync(Utf8(json), Connection("*"));

        var error = ParseError(outcome);
        Assert.Equal(expectedCode, error.Error.Code);
        Assert.Null(error.Id);
        Assert.False(string.IsNullOrWhiteSpace(error.Error.Data.CorrelationId));
    }

    [Fact]
    public async Task BatchProducesOneInvalidRequestAndExecutesNothing()
    {
        var executionCount = 0;
        var dispatcher = CreatePingDispatcher((_, _, _) =>
        {
            executionCount++;
            return Task.FromResult(ProtocolRpcExecution<PingResult>.Ok(new PingResult(1)));
        });

        var outcome = await dispatcher.DispatchAsync(
            Utf8("[{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.daemon.ping\",\"id\":1}]"),
            Connection("*"));

        Assert.Equal(-32600, ParseError(outcome).Error.Code);
        Assert.Equal(0, executionCount);
    }

    [Fact]
    public async Task UnknownMethodAndPermissionDenialDoNotInvokeHandler()
    {
        var executionCount = 0;
        var dispatcher = CreatePingDispatcher((_, _, _) =>
        {
            executionCount++;
            return Task.FromResult(ProtocolRpcExecution<PingResult>.Ok(new PingResult(1)));
        });

        var unknown = await dispatcher.DispatchAsync(
            Utf8("{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.daemon.unknown\",\"id\":1}"),
            Connection("*"));
        var denied = await dispatcher.DispatchAsync(PingRequest("1"), Connection());

        Assert.Equal(-32601, ParseError(unknown).Error.Code);
        Assert.Equal(-32001, ParseError(denied).Error.Code);
        Assert.Equal("permission.denied", ParseError(denied).Error.Data.DaemonErrorCode);
        Assert.Equal(0, executionCount);
    }

    [Fact]
    public async Task MissingPermissionViewDeniesAndWildcardPermissionsUseExistingMatcherSemantics()
    {
        var executions = 0;
        var dispatcher = CreateBuiltInDispatcher<PathRequest, UnitResult>(
            "mcsl.directory.create",
            (_, _, _) =>
            {
                executions++;
                return Task.FromResult(ProtocolRpcExecution<UnitResult>.Ok(new UnitResult()));
            });
        var request = Utf8(
            "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.directory.create\",\"id\":1,\"params\":{\"path\":\"folder\"}}");

        var missing = await dispatcher.DispatchAsync(
            request,
            new V2RpcConnectionContext(null, null, CancellationToken.None));
        var wildcard = await dispatcher.DispatchAsync(request, Connection("mcsl.daemon.file.**"));

        Assert.Equal(-32001, ParseError(missing).Error.Code);
        Assert.True(wildcard.HasResponse);
        Assert.Equal(1, executions);
    }

    [Fact]
    public async Task ParamsFollowEnvelopeAndDescriptorStrictness()
    {
        var directoryExecutions = 0;
        var ping = CreatePingDispatcher(static (_, _, _) =>
            Task.FromResult(ProtocolRpcExecution<PingResult>.Ok(new PingResult(1))));
        var directory = CreateBuiltInDispatcher<PathRequest, UnitResult>(
            "mcsl.directory.create",
            (_, _, _) =>
            {
                directoryExecutions++;
                return Task.FromResult(ProtocolRpcExecution<UnitResult>.Ok(new UnitResult()));
            });

        var unknownMember = await ping.DispatchAsync(PingRequest("1", "{\"unknown\":true}"), Connection("*"));
        var wrongEnvelopeShape = await ping.DispatchAsync(
            Utf8("{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.daemon.ping\",\"id\":2,\"params\":[]}"),
            Connection("*"));
        var missingRequiredParams = await directory.DispatchAsync(
            Utf8("{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.directory.create\",\"id\":3}"),
            Connection("mcsl.daemon.file.create.directory"));
        var emptyRequiredParams = await directory.DispatchAsync(
            Utf8("{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.directory.create\",\"id\":4,\"params\":{}}"),
            Connection("mcsl.daemon.file.create.directory"));
        Assert.Equal(-32602, ParseError(unknownMember).Error.Code);
        Assert.Equal(-32600, ParseError(wrongEnvelopeShape).Error.Code);
        Assert.Equal(-32602, ParseError(missingRequiredParams).Error.Code);
        Assert.Equal(-32602, ParseError(emptyRequiredParams).Error.Code);
        Assert.Equal(0, directoryExecutions);
    }

    [Fact]
    public async Task DtoConstructorArgumentFailureMapsToInvalidParamsWithoutInvokingHandler()
    {
        var executions = 0;
        var dispatcher = CreateBuiltInDispatcher<FileSessionReference, UnitResult>(
            "mcsl.file.download.close",
            (_, _, _) =>
            {
                executions++;
                return Task.FromResult(ProtocolRpcExecution<UnitResult>.Ok(new UnitResult()));
            });

        var outcome = await dispatcher.DispatchAsync(
            Utf8("{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.file.download.close\",\"id\":1,\"params\":{\"session_id\":\"00000000-0000-0000-0000-000000000000\"}}"),
            Connection("mcsl.daemon.file.download"));

        Assert.Equal(-32602, ParseError(outcome).Error.Code);
        Assert.Equal(0, executions);
        Assert.Null(outcome.DownloadAttachment);
    }

    [Fact]
    public async Task DtoConstructorArgumentFailureInNotificationSuppressesInvalidParams()
    {
        var executions = 0;
        var diagnostics = new RecordingDiagnosticSink();
        var owner = ProtocolExecutionOwner.ForPlugin(new ProtocolOwnerIdentity("test.session", "1.0.0"));
        var dispatcher = CreatePluginDispatcher<FileSessionReference, UnitResult>(
            owner,
            "plugin.test.session.rpc.close",
            allowNotification: true,
            (_, _, _) =>
            {
                executions++;
                return Task.FromResult(ProtocolRpcExecution<UnitResult>.Ok(new UnitResult()));
            },
            diagnostics);

        var outcome = await dispatcher.DispatchAsync(
            Utf8("{\"jsonrpc\":\"2.0\",\"method\":\"plugin.test.session.rpc.close\",\"params\":{\"session_id\":\"00000000-0000-0000-0000-000000000000\"}}"),
            Connection("*"));

        Assert.False(outcome.HasResponse);
        Assert.Null(outcome.DownloadAttachment);
        Assert.Equal(0, executions);
        Assert.Empty(diagnostics.Unexpected);
        Assert.Equal(
            V2RpcNotificationSuppressionReason.InvalidParams,
            Assert.Single(diagnostics.Suppressions).Reason);
    }

    [Fact]
    public async Task UnexpectedDeserializationFailureMapsToDiagnosticInternalError()
    {
        var executions = 0;
        var diagnostics = new RecordingDiagnosticSink();
        var owner = ProtocolExecutionOwner.ForPlugin(new ProtocolOwnerIdentity("test.deserialize", "1.0.0"));
        var dispatcher = CreatePluginDispatcher<UnexpectedDeserializationRequest, PingResult>(
            owner,
            "plugin.test.deserialize.rpc.run",
            allowNotification: false,
            (_, _, _) =>
            {
                executions++;
                return Task.FromResult(ProtocolRpcExecution<PingResult>.Ok(new PingResult(1)));
            },
            diagnostics);

        var outcome = await dispatcher.DispatchAsync(
            Utf8("{\"jsonrpc\":\"2.0\",\"method\":\"plugin.test.deserialize.rpc.run\",\"id\":1,\"params\":{\"Value\":{}}}"),
            Connection("*"));

        var error = ParseError(outcome).Error;
        var diagnostic = Assert.Single(diagnostics.Unexpected);
        Assert.Equal(-32603, error.Code);
        Assert.Equal(error.Data.CorrelationId, diagnostic.CorrelationId);
        Assert.Same(UnexpectedDeserializationRequestJsonConverter.Exception, diagnostic.Exception);
        Assert.Same(owner, diagnostic.Owner);
        Assert.Equal("plugin.test.deserialize.rpc.run", diagnostic.Method);
        Assert.Equal(0, executions);
        Assert.Null(outcome.DownloadAttachment);
        Assert.Empty(diagnostics.Suppressions);
    }

    [Fact]
    public async Task UnexpectedDeserializationFailureInNotificationRecordsDiagnosticWithoutResponse()
    {
        var executions = 0;
        var diagnostics = new RecordingDiagnosticSink();
        var owner = ProtocolExecutionOwner.ForPlugin(new ProtocolOwnerIdentity("test.deserialize", "1.0.0"));
        var dispatcher = CreatePluginDispatcher<UnexpectedDeserializationRequest, PingResult>(
            owner,
            "plugin.test.deserialize.rpc.notify",
            allowNotification: true,
            (_, _, _) =>
            {
                executions++;
                return Task.FromResult(ProtocolRpcExecution<PingResult>.Ok(new PingResult(1)));
            },
            diagnostics);

        var outcome = await dispatcher.DispatchAsync(
            Utf8("{\"jsonrpc\":\"2.0\",\"method\":\"plugin.test.deserialize.rpc.notify\",\"params\":{\"Value\":{}}}"),
            Connection("*"));

        var diagnostic = Assert.Single(diagnostics.Unexpected);
        Assert.False(outcome.HasResponse);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.CorrelationId));
        Assert.Same(UnexpectedDeserializationRequestJsonConverter.Exception, diagnostic.Exception);
        Assert.Same(owner, diagnostic.Owner);
        Assert.Equal("plugin.test.deserialize.rpc.notify", diagnostic.Method);
        Assert.Equal(0, executions);
        Assert.Null(outcome.DownloadAttachment);
        Assert.Empty(diagnostics.Suppressions);
    }

    [Theory]
    [InlineData("\"request-id\"", "\"request-id\"")]
    [InlineData("-0", "-0")]
    [InlineData("9223372036854775807", "9223372036854775807")]
    public async Task SuccessEchoesTheExactRequestIdAndSerializesTypedResultOnce(
        string requestId,
        string expectedResponseId)
    {
        var dispatcher = CreatePingDispatcher(static (_, _, _) =>
            Task.FromResult(ProtocolRpcExecution<PingResult>.Ok(new PingResult(42))));

        var outcome = await dispatcher.DispatchAsync(PingRequest(requestId), Connection("*"));

        var json = Encoding.UTF8.GetString(outcome.ResponseUtf8.AsSpan());
        Assert.Contains($"\"id\":{expectedResponseId}", json, StringComparison.Ordinal);
        var response = JsonRpcWireParser.ParseSuccessResponse(outcome.ResponseUtf8.AsSpan());
        var result = Assert.IsType<PingResult>(response.Result.Deserialize(ProtocolJson.PingResult));
        Assert.Equal(42, result.Time);
        Assert.Null(outcome.DownloadAttachment);
    }

    [Fact]
    public async Task UnitResultIsStrictlyAnObject()
    {
        var dispatcher = CreateBuiltInDispatcher<PathRequest, UnitResult>(
            "mcsl.directory.create",
            static (_, _, _) => Task.FromResult(ProtocolRpcExecution<UnitResult>.Ok(new UnitResult())));

        var outcome = await dispatcher.DispatchAsync(
            Utf8("{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.directory.create\",\"id\":1,\"params\":{\"path\":\"folder\"}}"),
            Connection("mcsl.daemon.file.**"));

        Assert.Equal(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}",
            Encoding.UTF8.GetString(outcome.ResponseUtf8.AsSpan()));
    }

    [Theory]
    [InlineData(false, -32000)]
    [InlineData(true, -32001)]
    public async Task ExpectedDaemonErrorsPreserveIdentityDetailsAndMapping(bool permissionError, int expectedCode)
    {
        using var detailsDocument = JsonDocument.Parse("{\"reason\":\"expected\"}");
        DaemonError expected = permissionError
            ? new PermissionDaemonError("permission.scope", "Denied.", detailsDocument.RootElement)
            : new NotFoundDaemonError("instance.not_found", "Missing.", detailsDocument.RootElement);
        DaemonError? observed = null;
        var dispatcher = CreatePingDispatcher((_, _, _) =>
        {
            observed = expected;
            return Task.FromResult(ProtocolRpcExecution<PingResult>.Err(expected));
        });

        var outcome = await dispatcher.DispatchAsync(PingRequest("1"), Connection("*"));

        var error = ParseError(outcome).Error;
        Assert.Same(expected, observed);
        Assert.Equal(expectedCode, error.Code);
        Assert.Equal(expected.Code, error.Data.DaemonErrorCode);
        Assert.Equal("expected", error.Data.Details!.Value.GetProperty("reason").GetString());
        Assert.Null(error.Data.OriginPlugin);
        Assert.Equal("mcsl.daemon", error.Data.ExecutionOwner!.Id);
        Assert.Equal("1.0.0", error.Data.ExecutionOwner.Version);
    }

    [Fact]
    public async Task MatchingCancellationPropagatesAndForeignCancellationMapsToInternalError()
    {
        using var requested = new CancellationTokenSource();
        requested.Cancel();
        var matching = CreatePingDispatcher(static (_, _, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(ProtocolRpcExecution<PingResult>.Ok(new PingResult(1)));
        });
        var foreign = CreatePingDispatcher(static (_, _, _) =>
            throw new OperationCanceledException(new CancellationToken(canceled: true)));

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            matching.DispatchAsync(PingRequest("1"), Connection("*"), requested.Token));
        var foreignOutcome = await foreign.DispatchAsync(PingRequest("2"), Connection("*"));

        Assert.Equal(requested.Token, exception.CancellationToken);
        Assert.Equal(-32603, ParseError(foreignOutcome).Error.Code);
    }

    [Fact]
    public async Task EffectiveCancellationTokenCoversRequestConnectionSameAndLinkedShapes()
    {
        using var requestOnly = new CancellationTokenSource();
        using var connectionOnly = new CancellationTokenSource();
        using var same = new CancellationTokenSource();
        using var linkedRequest = new CancellationTokenSource();
        using var linkedConnection = new CancellationTokenSource();

        Assert.Equal(
            requestOnly.Token,
            await ObserveEffectiveTokenAsync(requestOnly.Token, CancellationToken.None));
        Assert.Equal(
            connectionOnly.Token,
            await ObserveEffectiveTokenAsync(CancellationToken.None, connectionOnly.Token));
        Assert.Equal(same.Token, await ObserveEffectiveTokenAsync(same.Token, same.Token));
        var linked = await ObserveEffectiveTokenAsync(linkedRequest.Token, linkedConnection.Token);
        Assert.NotEqual(linkedRequest.Token, linked);
        Assert.NotEqual(linkedConnection.Token, linked);
        Assert.True(linked.CanBeCanceled);
    }

    [Fact]
    public async Task MatchingCancellationPropagatesForEveryEffectiveTokenSource()
    {
        using var requestOnly = new CancellationTokenSource();
        requestOnly.Cancel();
        await AssertMatchingCancellationAsync(requestOnly.Token, CancellationToken.None, requestOnly.Token);

        using var connectionOnly = new CancellationTokenSource();
        connectionOnly.Cancel();
        await AssertMatchingCancellationAsync(CancellationToken.None, connectionOnly.Token, connectionOnly.Token);

        using var same = new CancellationTokenSource();
        same.Cancel();
        await AssertMatchingCancellationAsync(same.Token, same.Token, same.Token);

        using var requestLinked = new CancellationTokenSource();
        using var connectionLinked = new CancellationTokenSource();
        requestLinked.Cancel();
        var requestLinkedException = await AssertMatchingCancellationAsync(
            requestLinked.Token,
            connectionLinked.Token,
            expectedToken: null);
        Assert.NotEqual(requestLinked.Token, requestLinkedException.CancellationToken);
        Assert.NotEqual(connectionLinked.Token, requestLinkedException.CancellationToken);

        using var requestLinkedSecond = new CancellationTokenSource();
        using var connectionLinkedSecond = new CancellationTokenSource();
        connectionLinkedSecond.Cancel();
        var connectionLinkedException = await AssertMatchingCancellationAsync(
            requestLinkedSecond.Token,
            connectionLinkedSecond.Token,
            expectedToken: null);
        Assert.NotEqual(requestLinkedSecond.Token, connectionLinkedException.CancellationToken);
        Assert.NotEqual(connectionLinkedSecond.Token, connectionLinkedException.CancellationToken);
    }

    [Fact]
    public async Task LinkedCancellationRegistrationsAreDisposedAfterDispatch()
    {
        using var request = new CancellationTokenSource();
        using var connection = new CancellationTokenSource();
        var callbacks = 0;
        var dispatcher = CreatePingDispatcher((_, _, token) =>
        {
            token.Register(() => callbacks++);
            return Task.FromResult(ProtocolRpcExecution<PingResult>.Ok(new PingResult(1)));
        });

        var outcome = await dispatcher.DispatchAsync(
            PingRequest("1"),
            Connection(connection.Token, "*"),
            request.Token);
        request.Cancel();
        connection.Cancel();

        Assert.True(outcome.HasResponse);
        Assert.Equal(0, callbacks);
    }

    [Fact]
    public async Task ForeignCancellationIsLoggedAndMappedForEveryEffectiveTokenShape()
    {
        using var request = new CancellationTokenSource();
        using var connection = new CancellationTokenSource();
        using var same = new CancellationTokenSource();

        await AssertForeignCancellationAsync(CancellationToken.None, CancellationToken.None);
        await AssertForeignCancellationAsync(request.Token, CancellationToken.None);
        await AssertForeignCancellationAsync(CancellationToken.None, connection.Token);
        await AssertForeignCancellationAsync(same.Token, same.Token);
        await AssertForeignCancellationAsync(request.Token, connection.Token);
    }

    [Fact]
    public async Task MatchingNotificationCancellationPropagatesWithoutResponseOrDiagnostic()
    {
        using var requested = new CancellationTokenSource();
        requested.Cancel();
        var diagnostics = new RecordingDiagnosticSink();
        var owner = ProtocolExecutionOwner.ForPlugin(new ProtocolOwnerIdentity("test.cancel", "1.0.0"));
        var dispatcher = CreatePluginDispatcher(
            owner,
            "plugin.test.cancel.rpc.run",
            allowNotification: true,
            static (_, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(ProtocolRpcExecution<PingResult>.Ok(new PingResult(1)));
            },
            diagnostics);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatcher.DispatchAsync(
            Notification("plugin.test.cancel.rpc.run"),
            Connection("*"),
            requested.Token));

        Assert.Equal(requested.Token, exception.CancellationToken);
        Assert.Empty(diagnostics.Unexpected);
        Assert.Empty(diagnostics.Suppressions);
    }

    [Fact]
    public async Task UnexpectedExceptionMapsWithoutLeakingImplementationData()
    {
        var expected = new InvalidOperationException("secret C:\\daemon\\config.json InternalType");
        var diagnostics = new RecordingDiagnosticSink();
        var dispatcher = CreatePingDispatcher((_, _, _) => throw expected, diagnostics);

        var outcome = await dispatcher.DispatchAsync(PingRequest("1"), Connection("*"));

        var json = Encoding.UTF8.GetString(outcome.ResponseUtf8.AsSpan());
        var error = ParseError(outcome).Error;
        var diagnostic = Assert.Single(diagnostics.Unexpected);
        Assert.Equal(-32603, error.Code);
        Assert.Equal(error.Data.CorrelationId, diagnostic.CorrelationId);
        Assert.Same(expected, diagnostic.Exception);
        Assert.Equal("mcsl.daemon.ping", diagnostic.Method);
        Assert.Same(ProtocolExecutionOwner.BuiltIn, diagnostic.Owner);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("config.json", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InternalType", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResultSerializationFailureUsesTheSameWireAndDiagnosticCorrelation()
    {
        var diagnostics = new RecordingDiagnosticSink();
        var dispatcher = CreateBuiltInDispatcher<EmptyRequest, PermissionsResult>(
            "mcsl.auth.permissions.get",
            static (_, _, _) => Task.FromResult(
                ProtocolRpcExecution<PermissionsResult>.Ok(new PermissionsResult(default))),
            diagnostics);

        var outcome = await dispatcher.DispatchAsync(
            Utf8("{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.auth.permissions.get\",\"id\":1,\"params\":{}}"),
            Connection("*"));

        var error = ParseError(outcome).Error;
        var diagnostic = Assert.Single(diagnostics.Unexpected);
        Assert.Equal(-32603, error.Code);
        Assert.Equal(error.Data.CorrelationId, diagnostic.CorrelationId);
        Assert.Equal("mcsl.auth.permissions.get", diagnostic.Method);
        Assert.Same(ProtocolExecutionOwner.BuiltIn, diagnostic.Owner);
    }

    [Fact]
    public async Task NotificationsExecuteOnlyWhenAllowedAndNeverRespond()
    {
        var allowedExecutions = 0;
        var allowedOwner = ProtocolExecutionOwner.ForPlugin(new ProtocolOwnerIdentity("test.notify", "1.0.0"));
        ProtocolExecutionOwner? observedOwner = null;
        var allowed = CreatePluginDispatcher(
            allowedOwner,
            "plugin.test.notify.rpc.run",
            allowNotification: true,
            (context, _, _) =>
            {
                allowedExecutions++;
                observedOwner = context.ExecutionOwner;
                return Task.FromResult(ProtocolRpcExecution<PingResult>.Ok(new PingResult(1)));
            });
        var disallowedExecutions = 0;
        var disallowed = CreatePingDispatcher((_, _, _) =>
        {
            disallowedExecutions++;
            return Task.FromResult(ProtocolRpcExecution<PingResult>.Ok(new PingResult(1)));
        });

        var allowedOutcome = await allowed.DispatchAsync(
            Utf8("{\"jsonrpc\":\"2.0\",\"method\":\"plugin.test.notify.rpc.run\",\"params\":{}}"),
            Connection("*"));
        var disallowedOutcome = await disallowed.DispatchAsync(
            Utf8("{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.daemon.ping\",\"params\":{}}"),
            Connection("*"));
        var unknownOutcome = await disallowed.DispatchAsync(
            Utf8("{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.daemon.unknown\",\"params\":{}}"),
            Connection("*"));

        Assert.False(allowedOutcome.HasResponse);
        Assert.False(disallowedOutcome.HasResponse);
        Assert.False(unknownOutcome.HasResponse);
        Assert.Equal(1, allowedExecutions);
        Assert.Equal(0, disallowedExecutions);
        Assert.Same(allowedOwner, observedOwner);
    }

    [Fact]
    public async Task NotificationHandlerFailuresAndPermissionDenialNeverRespond()
    {
        var owner = ProtocolExecutionOwner.ForPlugin(new ProtocolOwnerIdentity("test.notify", "1.0.0"));
        var expectedDiagnostics = new RecordingDiagnosticSink();
        var unexpectedDiagnostics = new RecordingDiagnosticSink();
        var unexpectedException = new InvalidOperationException("secret");
        var expectedError = CreatePluginDispatcher(
            owner,
            "plugin.test.notify.rpc.expected",
            allowNotification: true,
            static (_, _, _) => Task.FromResult(ProtocolRpcExecution<PingResult>.Err(
                new ConflictDaemonError("expected", "Expected."))),
            expectedDiagnostics);
        var unexpectedError = CreatePluginDispatcher(
            owner,
            "plugin.test.notify.rpc.unexpected",
            allowNotification: true,
            (_, _, _) => throw unexpectedException,
            unexpectedDiagnostics);

        var expected = await expectedError.DispatchAsync(Notification("plugin.test.notify.rpc.expected"), Connection("*"));
        var unexpected = await unexpectedError.DispatchAsync(Notification("plugin.test.notify.rpc.unexpected"), Connection("*"));
        var denied = await expectedError.DispatchAsync(Notification("plugin.test.notify.rpc.expected"), Connection());

        Assert.False(expected.HasResponse);
        Assert.False(unexpected.HasResponse);
        Assert.False(denied.HasResponse);
        Assert.Empty(expectedDiagnostics.Unexpected);
        Assert.Equal(
            [
                V2RpcNotificationSuppressionReason.ExpectedDaemonError,
                V2RpcNotificationSuppressionReason.PermissionDenied
            ],
            expectedDiagnostics.Suppressions.Select(item => item.Reason));
        var diagnostic = Assert.Single(unexpectedDiagnostics.Unexpected);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.CorrelationId));
        Assert.Same(unexpectedException, diagnostic.Exception);
        Assert.Same(owner, diagnostic.Owner);
        Assert.Equal("plugin.test.notify.rpc.unexpected", diagnostic.Method);
    }

    [Fact]
    public async Task UnknownAndDisallowedNotificationsRecordTypedSuppressionWithoutExecuting()
    {
        var executions = 0;
        var diagnostics = new RecordingDiagnosticSink();
        var dispatcher = CreatePingDispatcher((_, _, _) =>
        {
            executions++;
            return Task.FromResult(ProtocolRpcExecution<PingResult>.Ok(new PingResult(1)));
        }, diagnostics);

        var disallowed = await dispatcher.DispatchAsync(Notification("mcsl.daemon.ping"), Connection("*"));
        var unknown = await dispatcher.DispatchAsync(Notification("mcsl.daemon.unknown"), Connection("*"));

        Assert.False(disallowed.HasResponse);
        Assert.False(unknown.HasResponse);
        Assert.Equal(0, executions);
        Assert.Equal(
            [
                V2RpcNotificationSuppressionReason.NotificationNotAllowed,
                V2RpcNotificationSuppressionReason.UnknownMethod
            ],
            diagnostics.Suppressions.Select(item => item.Reason));
        Assert.All(diagnostics.Suppressions, diagnostic => Assert.DoesNotContain("params", diagnostic.Method));
    }

    [Fact]
    public async Task PluginErrorDataUsesFrozenEntryOwnerAndNeverClaimsAnOriginPlugin()
    {
        var owner = ProtocolExecutionOwner.ForPlugin(new ProtocolOwnerIdentity("test.owner", "2.3.4"));
        var dispatcher = CreatePluginDispatcher(
            owner,
            "plugin.test.owner.rpc.fail",
            allowNotification: false,
            static (_, _, _) => Task.FromResult(ProtocolRpcExecution<PingResult>.Err(
                new NotFoundDaemonError("test.missing", "Missing."))));

        var outcome = await dispatcher.DispatchAsync(
            Utf8("{\"jsonrpc\":\"2.0\",\"method\":\"plugin.test.owner.rpc.fail\",\"id\":1,\"params\":{}}"),
            Connection("*"));

        var data = ParseError(outcome).Error.Data;
        Assert.Null(data.OriginPlugin);
        Assert.Equal("test.owner", data.ExecutionOwner!.Id);
        Assert.Equal("2.3.4", data.ExecutionOwner.Version);
    }

    [Fact]
    public async Task DownloadSuccessPreservesTheExactAttachmentIdentity()
    {
        var sessionId = Guid.NewGuid();
        var data = ImmutableArray.Create<byte>(1, 2, 3);
        var attachment = new ProtocolDownloadAttachment(sessionId, 7, data, true);
        var dispatcher = CreateBuiltInDispatcher<DownloadChunkRequest, DownloadReadResult>(
            "mcsl.file.download.read",
            (_, _, _) => Task.FromResult(ProtocolRpcExecution<DownloadReadResult>.DownloadOk(
                new DownloadReadResult(sessionId, 7, data.Length, true),
                attachment)));

        var outcome = await dispatcher.DispatchAsync(
            Utf8($"{{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.file.download.read\",\"id\":1,\"params\":{{\"session_id\":\"{sessionId}\",\"offset\":7,\"maximum_length\":3}}}}"),
            Connection("mcsl.daemon.file.download"));

        Assert.True(outcome.HasResponse);
        Assert.Same(attachment, outcome.DownloadAttachment);
        var response = JsonRpcWireParser.ParseSuccessResponse(outcome.ResponseUtf8.AsSpan());
        var metadata = Assert.IsType<DownloadReadResult>(response.Result.Deserialize(ProtocolJson.DownloadReadResult));
        Assert.Equal(sessionId, metadata.SessionId);
    }

    [Fact]
    public void CatalogRejectsAttachmentProducingNotificationsDuringRegistration()
    {
        var owner = ProtocolExecutionOwner.ForPlugin(new ProtocolOwnerIdentity("test.download", "1.0.0"));
        var descriptor = CreatePluginDescriptor<EmptyRequest, DownloadReadResult>(
            "plugin.test.download.rpc.read",
            allowNotification: true);
        var builder = new ProtocolCatalogBuilder(new OpenRpcInfo("Dispatcher tests", "1.0.0"));

        Assert.Throws<ArgumentException>(() => builder.AddRpcDefinition(owner, descriptor));
    }

    [Fact]
    public void EveryBuiltInRequestDescriptorRejectsUnknownMembersAtTheTypedPayloadBoundary()
    {
        Assert.NotEmpty(BuiltInProtocolDefinitions.Rpcs);
        var payload = JsonRpcWireParser.ParseRequest(Utf8(
            "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.daemon.ping\",\"id\":1,\"params\":{\"__unknown_request_member\":true}}")
            .Span)
            .Params!;
        Assert.All(BuiltInProtocolDefinitions.Rpcs, descriptor =>
        {
            Assert.False(descriptor.RequestTypeInfo.Options.AllowDuplicateProperties);
            Assert.Equal(
                JsonUnmappedMemberHandling.Disallow,
                descriptor.RequestTypeInfo.Options.UnmappedMemberHandling);
            Assert.True(descriptor.RequestTypeInfo.Options.RespectNullableAnnotations);
            Assert.True(descriptor.RequestTypeInfo.Options.RespectRequiredConstructorParameters);
            var exception = Record.Exception(() => payload.Deserialize(descriptor.RequestTypeInfo));
            Assert.IsType<JsonException>(exception);
        });
    }

    [Fact]
    public void EmptyObjectParamsAreAcceptedOnlyByEmptyRequestDefinitions()
    {
        var payload = JsonRpcWireParser.ParseRequest(Utf8(
            "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.daemon.ping\",\"id\":1,\"params\":{}}")
            .Span)
            .Params!;

        Assert.All(BuiltInProtocolDefinitions.Rpcs, descriptor =>
        {
            if (descriptor.RequestTypeInfo.Type == typeof(EmptyRequest))
            {
                Assert.IsType<EmptyRequest>(V2RpcDispatcher.DeserializeRequest(payload, descriptor));
                return;
            }

            Assert.Throws<JsonException>(() => V2RpcDispatcher.DeserializeRequest(payload, descriptor));
        });
    }

    [Fact]
    public void NestedTypedRequestsRejectUnknownNullMissingAndDuplicateMembers()
    {
        var request = new UpdateInstanceSettingsRequest(
            Guid.NewGuid(),
            "test",
            InstanceType.Universal,
            null,
            ["--headless"],
            null,
            new InstanceCoreReplacementRequest("uploads/core.jar", "server.jar"),
            false);
        var validJson = JsonSerializer.Serialize(
            request,
            ApplicationContractJsonContext.Default.UpdateInstanceSettingsRequest);
        const string nestedProperty = "\"uploaded_source_path\":\"uploads/core.jar\"";

        var valid = ParsePayload(validJson).Deserialize(
            ApplicationContractJsonContext.Default.UpdateInstanceSettingsRequest);
        var unknown = validJson.Replace(
            nestedProperty,
            $"\"unknown_nested\":true,{nestedProperty}",
            StringComparison.Ordinal);
        var explicitNull = validJson.Replace(
            nestedProperty,
            "\"uploaded_source_path\":null",
            StringComparison.Ordinal);
        var missing = validJson.Replace($"{nestedProperty},", string.Empty, StringComparison.Ordinal);
        var duplicate = validJson.Replace(
            nestedProperty,
            $"{nestedProperty},{nestedProperty}",
            StringComparison.Ordinal);

        Assert.IsType<UpdateInstanceSettingsRequest>(valid);
        Assert.Throws<JsonException>(() => ParsePayload(unknown).Deserialize(
            ApplicationContractJsonContext.Default.UpdateInstanceSettingsRequest));
        Assert.Throws<JsonException>(() => ParsePayload(explicitNull).Deserialize(
            ApplicationContractJsonContext.Default.UpdateInstanceSettingsRequest));
        Assert.Throws<JsonException>(() => ParsePayload(missing).Deserialize(
            ApplicationContractJsonContext.Default.UpdateInstanceSettingsRequest));
        Assert.Throws<JsonException>(() => ParsePayload(duplicate).Deserialize(
            ApplicationContractJsonContext.Default.UpdateInstanceSettingsRequest));
    }

    [Fact]
    public void DynamicJsonElementAllowsUnknownShapeButRejectsDuplicatesInsideNestedArrays()
    {
        var instanceId = Guid.NewGuid();
        var valid = $"{{\"instance_id\":\"{instanceId}\",\"rules\":[{{\"extension_key\":true}}]}}";
        var duplicate = $"{{\"instance_id\":\"{instanceId}\",\"rules\":[{{\"kind\":\"a\",\"kind\":\"b\"}}]}}";

        var value = ParsePayload(valid).Deserialize(ApplicationContractJsonContext.Default.EventRuleUpdateRequest);

        Assert.IsType<EventRuleUpdateRequest>(value);
        Assert.Throws<JsonException>(() => ParsePayload(duplicate).Deserialize(
            ApplicationContractJsonContext.Default.EventRuleUpdateRequest));
    }

    [Fact]
    public void CustomSubscriptionConverterRejectsDuplicatesInsideDynamicMeta()
    {
        var payload = ParsePayload(
            "{\"event\":\"mcsl.event.daemon.report\",\"meta\":{\"key\":1,\"key\":2}}");

        Assert.Throws<JsonException>(() => payload.Deserialize(ProtocolJson.EventSubscriptionRequest));
    }

    [Fact]
    public async Task TypeErasedBindingRejectsWrongRequestTypeBeforeHandler()
    {
        var handlerCalled = false;
        var binding = new RpcBinding<EmptyRequest, PingResult>(
            ProtocolExecutionOwner.BuiltIn,
            (_, _, _) =>
            {
                handlerCalled = true;
                return Task.FromResult(ProtocolRpcExecution<PingResult>.Ok(new PingResult(1)));
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() => binding.InvokeAsync(
            new ProtocolInvocationContext(ProtocolExecutionOwner.BuiltIn),
            new UnitResult(),
            CancellationToken.None));
        Assert.False(handlerCalled);
    }

    [Fact]
    public async Task TypeErasedBindingPreservesExpectedErrorIdentity()
    {
        var expected = new ConflictDaemonError("test.conflict", "Expected.");
        var binding = new RpcBinding<EmptyRequest, PingResult>(
            ProtocolExecutionOwner.BuiltIn,
            (_, _, _) => Task.FromResult(ProtocolRpcExecution<PingResult>.Err(expected)));

        var execution = await binding.InvokeAsync(
            new ProtocolInvocationContext(ProtocolExecutionOwner.BuiltIn),
            new EmptyRequest(),
            CancellationToken.None);

        Assert.True(execution.Result.IsErr(out _));
        Assert.Same(expected, execution.Result.UnwrapErr());
        Assert.Null(execution.DownloadAttachment);
    }

    private static async Task<CancellationToken> ObserveEffectiveTokenAsync(
        CancellationToken requestCancellationToken,
        CancellationToken connectionCancellationToken)
    {
        var observed = CancellationToken.None;
        var dispatcher = CreatePingDispatcher((_, _, token) =>
        {
            observed = token;
            return Task.FromResult(ProtocolRpcExecution<PingResult>.Ok(new PingResult(1)));
        });

        await dispatcher.DispatchAsync(
            PingRequest("1"),
            Connection(connectionCancellationToken, "*"),
            requestCancellationToken);
        return observed;
    }

    private static async Task<OperationCanceledException> AssertMatchingCancellationAsync(
        CancellationToken requestCancellationToken,
        CancellationToken connectionCancellationToken,
        CancellationToken? expectedToken)
    {
        var dispatcher = CreatePingDispatcher(static (_, _, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(ProtocolRpcExecution<PingResult>.Ok(new PingResult(1)));
        });

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatcher.DispatchAsync(
            PingRequest("1"),
            Connection(connectionCancellationToken, "*"),
            requestCancellationToken));
        Assert.True(exception.CancellationToken.IsCancellationRequested);
        if (expectedToken is { } token)
        {
            Assert.Equal(token, exception.CancellationToken);
        }

        return exception;
    }

    private static async Task AssertForeignCancellationAsync(
        CancellationToken requestCancellationToken,
        CancellationToken connectionCancellationToken)
    {
        using var foreignSource = new CancellationTokenSource();
        foreignSource.Cancel();
        var expected = new OperationCanceledException(foreignSource.Token);
        var diagnostics = new RecordingDiagnosticSink();
        var dispatcher = CreatePingDispatcher((_, _, _) => throw expected, diagnostics);

        var outcome = await dispatcher.DispatchAsync(
            PingRequest("1"),
            Connection(connectionCancellationToken, "*"),
            requestCancellationToken);

        var error = ParseError(outcome).Error;
        var diagnostic = Assert.Single(diagnostics.Unexpected);
        Assert.Equal(-32603, error.Code);
        Assert.Equal(error.Data.CorrelationId, diagnostic.CorrelationId);
        Assert.Same(expected, diagnostic.Exception);
    }

    private static V2RpcDispatcher CreatePingDispatcher(
        ProtocolRpcHandler<EmptyRequest, PingResult> handler,
        IV2RpcDiagnosticSink? diagnosticSink = null) =>
        CreateBuiltInDispatcher("mcsl.daemon.ping", handler, diagnosticSink);

    private static V2RpcDispatcher CreateBuiltInDispatcher<TRequest, TResult>(
        string method,
        ProtocolRpcHandler<TRequest, TResult> handler,
        IV2RpcDiagnosticSink? diagnosticSink = null)
        where TResult : notnull
    {
        var descriptor = (RpcDescriptor<TRequest, TResult>)BuiltInProtocolDefinitions.Rpcs.Single(
            candidate => candidate.Method.Value == method);
        var builder = new ProtocolCatalogBuilder(new OpenRpcInfo("Dispatcher tests", "1.0.0"));
        builder.RegisterBuiltInRpc(
            descriptor,
            new RpcBinding<TRequest, TResult>(ProtocolExecutionOwner.BuiltIn, handler));
        return new V2RpcDispatcher(builder.Freeze(), diagnosticSink ?? new RecordingDiagnosticSink());
    }

    private static V2RpcDispatcher CreatePluginDispatcher(
        ProtocolExecutionOwner owner,
        string method,
        bool allowNotification,
        ProtocolRpcHandler<EmptyRequest, PingResult> handler,
        IV2RpcDiagnosticSink? diagnosticSink = null)
    {
        var descriptor = CreatePluginDescriptor<EmptyRequest, PingResult>(method, allowNotification);
        var builder = new ProtocolCatalogBuilder(new OpenRpcInfo("Dispatcher tests", "1.0.0"));
        builder.AddRpcDefinition(owner, descriptor);
        builder.AddRpcBinding(descriptor.Method, new RpcBinding<EmptyRequest, PingResult>(owner, handler));
        return new V2RpcDispatcher(builder.Freeze(), diagnosticSink ?? new RecordingDiagnosticSink());
    }

    private static V2RpcDispatcher CreatePluginDispatcher<TRequest, TResult>(
        ProtocolExecutionOwner owner,
        string method,
        bool allowNotification,
        ProtocolRpcHandler<TRequest, TResult> handler,
        IV2RpcDiagnosticSink? diagnosticSink = null)
        where TResult : notnull
    {
        var descriptor = CreatePluginDescriptor<TRequest, TResult>(method, allowNotification);
        var builder = new ProtocolCatalogBuilder(new OpenRpcInfo("Dispatcher tests", "1.0.0"));
        builder.AddRpcDefinition(owner, descriptor);
        builder.AddRpcBinding(descriptor.Method, new RpcBinding<TRequest, TResult>(owner, handler));
        return new V2RpcDispatcher(builder.Freeze(), diagnosticSink ?? new RecordingDiagnosticSink());
    }

    private static RpcDescriptor<TRequest, TResult> CreatePluginDescriptor<TRequest, TResult>(
        string method,
        bool allowNotification)
    {
        var requestTypeInfo = (JsonTypeInfo<TRequest>)(object)(typeof(TRequest) == typeof(EmptyRequest)
            ? ProtocolJson.EmptyRequest
            : typeof(TRequest) == typeof(FileSessionReference)
                ? ProtocolJson.FileSessionReference
                : typeof(TRequest) == typeof(UnexpectedDeserializationRequest)
                    ? DispatcherTestJsonContext.Default.UnexpectedDeserializationRequest
            : throw new InvalidOperationException("The dispatcher test helper needs request metadata."));
        var resultTypeInfo = (JsonTypeInfo<TResult>)(object)(typeof(TResult) == typeof(PingResult)
            ? ProtocolJson.PingResult
            : typeof(TResult) == typeof(DownloadReadResult)
                ? ProtocolJson.DownloadReadResult
                : typeof(TResult) == typeof(UnitResult)
                    ? ProtocolJson.UnitResult
                : throw new InvalidOperationException("The dispatcher test helper needs result metadata."));
        var constructor = typeof(RpcDescriptor<TRequest, TResult>)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single();
        return (RpcDescriptor<TRequest, TResult>)constructor.Invoke(
        [
            new RpcMethod(method),
            new PermissionName("*"),
            requestTypeInfo,
            resultTypeInfo,
            allowNotification,
            new RpcDocumentation("test", "Test", "Test descriptor.", "test.request", "test.result")
        ]);
    }

    private static V2RpcConnectionContext Connection(params string[] permissions) =>
        new(new TestPermissionView(permissions.ToImmutableArray()), null, CancellationToken.None);

    private static V2RpcConnectionContext Connection(
        CancellationToken connectionCancellationToken,
        params string[] permissions) =>
        new(new TestPermissionView(permissions.ToImmutableArray()), null, connectionCancellationToken);

    private static ReadOnlyMemory<byte> PingRequest(string id, string? parameters = null) =>
        Utf8($"{{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.daemon.ping\",\"id\":{id}{(parameters is null ? string.Empty : $",\"params\":{parameters}")}}}");

    private static ReadOnlyMemory<byte> Notification(string method) =>
        Utf8($"{{\"jsonrpc\":\"2.0\",\"method\":\"{method}\",\"params\":{{}}}}");

    private static JsonRpcObjectPayload ParsePayload(string jsonObject) =>
        JsonRpcWireParser.ParseRequest(Utf8(
            $"{{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.test.payload\",\"id\":1,\"params\":{jsonObject}}}")
            .Span)
            .Params!;

    private static ReadOnlyMemory<byte> Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static JsonRpcErrorResponseEnvelope ParseError(V2RpcDispatchOutcome outcome)
    {
        Assert.True(outcome.HasResponse);
        Assert.Null(outcome.DownloadAttachment);
        return JsonRpcWireParser.ParseErrorResponse(outcome.ResponseUtf8.AsSpan());
    }

    private sealed record TestPermissionView(ImmutableArray<string> Permissions) : IProtocolPermissionView;

    private sealed class RecordingFileSessionOperations(DownloadChunk chunk) : IProtocolFileSessionOperations
    {
        internal CancellationToken ObservedToken { get; private set; }

        public Task<Result<UploadSession, DaemonError>> OpenUploadAsync(UploadOpenRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CloseUploadAsync(Guid sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CancelUploadAsync(Guid sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<DownloadSession, DaemonError>> OpenDownloadAsync(DownloadOpenRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CloseDownloadAsync(Guid sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<DownloadChunk, DaemonError>> ReadDownloadChunkAsync(DownloadChunkRequest request, CancellationToken cancellationToken)
        {
            ObservedToken = cancellationToken;
            return Task.FromResult(Result.Ok<DownloadChunk, DaemonError>(chunk));
        }
    }

    private sealed class RecordingDiagnosticSink : IV2RpcDiagnosticSink
    {
        internal List<V2RpcUnexpectedDiagnostic> Unexpected { get; } = [];

        internal List<V2RpcNotificationSuppressionDiagnostic> Suppressions { get; } = [];

        public void RecordUnexpected(V2RpcUnexpectedDiagnostic diagnostic)
        {
            Unexpected.Add(diagnostic);
        }

        public void RecordNotificationSuppressed(V2RpcNotificationSuppressionDiagnostic diagnostic)
        {
            Suppressions.Add(diagnostic);
        }
    }
}

internal sealed class UnexpectedDeserializationRequest
{
    public required UnexpectedDeserializationValue Value { get; init; }
}

[JsonConverter(typeof(UnexpectedDeserializationRequestJsonConverter))]
internal sealed class UnexpectedDeserializationValue;

internal sealed class UnexpectedDeserializationRequestJsonConverter : JsonConverter<UnexpectedDeserializationValue>
{
    internal static InvalidOperationException Exception { get; } =
        new("Unexpected converter failure with implementation details.");

    public override UnexpectedDeserializationValue Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => throw Exception;

    public override void Write(
        Utf8JsonWriter writer,
        UnexpectedDeserializationValue value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteEndObject();
    }
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(UnexpectedDeserializationRequest))]
internal partial class DispatcherTestJsonContext : JsonSerializerContext;
