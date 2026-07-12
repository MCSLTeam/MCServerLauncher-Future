using System.Collections.Immutable;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;

namespace MCServerLauncher.Daemon.Remote.Rpc.Dispatch;

internal sealed class V2RpcConnectionContext(
    IProtocolPermissionView? permissionView,
    IProtocolSubscriptionOperations? subscriptionOperations,
    CancellationToken connectionCancellationToken)
{
    internal IProtocolPermissionView? PermissionView { get; } = permissionView;

    internal IProtocolSubscriptionOperations? SubscriptionOperations { get; } = subscriptionOperations;

    internal CancellationToken ConnectionCancellationToken { get; } = connectionCancellationToken;
}

internal sealed class V2RpcDispatchOutcome
{
    private V2RpcDispatchOutcome(
        ImmutableArray<byte> responseUtf8,
        ProtocolDownloadAttachment? downloadAttachment)
    {
        if (responseUtf8.IsDefault)
        {
            throw new ArgumentException("A dispatch response buffer cannot be default.", nameof(responseUtf8));
        }

        if (responseUtf8.IsEmpty && downloadAttachment is not null)
        {
            throw new ArgumentException("A response attachment requires a JSON-RPC response.", nameof(downloadAttachment));
        }

        ResponseUtf8 = responseUtf8;
        DownloadAttachment = downloadAttachment;
    }

    internal static V2RpcDispatchOutcome NoResponse { get; } = new([], null);

    internal ImmutableArray<byte> ResponseUtf8 { get; }

    internal ProtocolDownloadAttachment? DownloadAttachment { get; }

    internal bool HasResponse => !ResponseUtf8.IsEmpty;

    internal static V2RpcDispatchOutcome Response(
        ImmutableArray<byte> responseUtf8,
        ProtocolDownloadAttachment? downloadAttachment = null)
    {
        if (responseUtf8.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A JSON-RPC response cannot be empty.", nameof(responseUtf8));
        }

        return new V2RpcDispatchOutcome(responseUtf8, downloadAttachment);
    }
}

internal sealed class V2RpcDispatcher
{
    private static readonly byte[] EmptyObjectUtf8 = "{}"u8.ToArray();
    private readonly FrozenProtocolCatalog _catalog;
    private readonly IV2RpcDiagnosticSink _diagnosticSink;

    internal V2RpcDispatcher(FrozenProtocolCatalog catalog, IV2RpcDiagnosticSink diagnosticSink)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _diagnosticSink = diagnosticSink ?? throw new ArgumentNullException(nameof(diagnosticSink));
    }

    internal async Task<V2RpcDispatchOutcome> DispatchAsync(
        ReadOnlyMemory<byte> requestUtf8,
        V2RpcConnectionContext connection,
        CancellationToken requestCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        JsonRpcRequestEnvelope request;
        try
        {
            request = JsonRpcWireParser.ParseRequest(requestUtf8.Span);
        }
        catch (JsonRpcRequestParseException exception)
        {
            var code = exception.FailureKind == JsonRpcRequestFailureKind.ParseError ? -32700 : -32600;
            var message = exception.FailureKind == JsonRpcRequestFailureKind.ParseError
                ? "Parse error"
                : "Invalid Request";
            return Error(null, code, message, null, null);
        }

        RpcMethod method;
        try
        {
            method = new RpcMethod(request.Method);
        }
        catch (ArgumentException)
        {
            return ErrorOrSuppress(
                request,
                -32600,
                "Invalid Request",
                null,
                null,
                V2RpcNotificationSuppressionReason.InvalidRequest);
        }

        if (!_catalog.TryGetRpc(method, out var entry))
        {
            return ErrorOrSuppress(
                request,
                -32601,
                "Method not found",
                null,
                null,
                V2RpcNotificationSuppressionReason.UnknownMethod);
        }

        if (request.IsNotification && !entry.Descriptor.AllowNotification)
        {
            return SuppressNotification(
                request.Method,
                entry.Owner,
                V2RpcNotificationSuppressionReason.NotificationNotAllowed);
        }

        if (!HasPermission(connection.PermissionView, entry.Descriptor.Permission))
        {
            return ErrorOrSuppress(
                request,
                -32001,
                "Permission denied",
                new ErrorPayload("permission.denied", null),
                entry.Owner,
                V2RpcNotificationSuppressionReason.PermissionDenied);
        }

        object typedRequest;
        try
        {
            typedRequest = DeserializeRequest(request.Params, entry.Descriptor);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return ErrorOrSuppress(
                request,
                -32602,
                "Invalid params",
                null,
                entry.Owner,
                V2RpcNotificationSuppressionReason.InvalidParams);
        }
        catch (NotSupportedException exception)
        {
            return UnexpectedOrSuppress(request, entry.Owner, exception);
        }
        catch (Exception exception)
        {
            return UnexpectedOrSuppress(request, entry.Owner, exception);
        }
        using var linkedCancellation = CreateLinkedCancellation(
            requestCancellationToken,
            connection.ConnectionCancellationToken,
            out var invocationCancellationToken);

        ErasedProtocolRpcExecution execution;
        try
        {
            invocationCancellationToken.ThrowIfCancellationRequested();
            var invocationContext = new ProtocolInvocationContext(
                entry.Owner,
                connection.PermissionView,
                connection.SubscriptionOperations);
            execution = await entry.Binding
                .InvokeAsync(invocationContext, typedRequest, invocationCancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (
            invocationCancellationToken.IsCancellationRequested &&
            exception.CancellationToken == invocationCancellationToken)
        {
            throw;
        }
        catch (Exception exception)
        {
            return UnexpectedOrSuppress(request, entry.Owner, exception);
        }

        if (execution.Result.IsErr(out _))
        {
            var daemonError = execution.Result.UnwrapErr();
            // Phase 5 adds an explicit PluginError -> -32005 branch; owner or error-code strings are not a substitute.
            var code = daemonError is PermissionDaemonError ? -32001 : -32000;
            var message = daemonError is PermissionDaemonError ? "Permission denied" : "Daemon error";
            return ErrorOrSuppress(
                request,
                code,
                message,
                new ErrorPayload(daemonError.Code, daemonError.Details),
                entry.Owner,
                V2RpcNotificationSuppressionReason.ExpectedDaemonError);
        }

        if (request.IsNotification)
        {
            return V2RpcDispatchOutcome.NoResponse;
        }

        var result = execution.Result.Unwrap();
        if (result.GetType() != entry.Descriptor.ResultTypeInfo.Type)
        {
            return UnexpectedOrSuppress(
                request,
                entry.Owner,
                new InvalidOperationException(
                    $"RPC result type '{result.GetType()}' does not exactly match '{entry.Descriptor.ResultTypeInfo.Type}'."));
        }

        if (execution.DownloadAttachment is not null &&
            entry.Descriptor.ResultTypeInfo.Type != typeof(DownloadReadResult))
        {
            return UnexpectedOrSuppress(
                request,
                entry.Owner,
                new InvalidOperationException("A non-download RPC returned a download attachment."));
        }

        try
        {
            var payload = JsonRpcObjectPayload.From(result, entry.Descriptor.ResultTypeInfo);
            var envelope = new JsonRpcSuccessResponseEnvelope(request.Id!, payload);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                envelope,
                BuiltInProtocolJsonContext.Default.JsonRpcSuccessResponseEnvelope);
            return V2RpcDispatchOutcome.Response(bytes.ToImmutableArray(), execution.DownloadAttachment);
        }
        catch (Exception exception)
        {
            return UnexpectedOrSuppress(request, entry.Owner, exception);
        }
    }

    internal static object DeserializeRequest(JsonRpcObjectPayload? parameters, RpcDescriptor descriptor)
    {
        if (parameters is null)
        {
            if (descriptor.RequestTypeInfo.Type != typeof(EmptyRequest))
            {
                throw new JsonException("This RPC method requires params.");
            }

            return JsonSerializer.Deserialize(EmptyObjectUtf8, descriptor.RequestTypeInfo)
                ?? throw new JsonException("Empty request metadata produced null.");
        }

        if (parameters.IsEmptyObject && descriptor.RequestTypeInfo.Type != typeof(EmptyRequest))
        {
            throw new JsonException("Only EmptyRequest RPC methods accept an empty params object.");
        }

        return parameters.Deserialize(descriptor.RequestTypeInfo);
    }

    private static bool HasPermission(IProtocolPermissionView? permissionView, PermissionName requiredPermission)
    {
        if (permissionView is null || permissionView.Permissions.IsDefault)
        {
            return false;
        }

        try
        {
            return new Permissions(permissionView.Permissions.ToArray())
                .Matches(Permission.Of(requiredPermission.Value));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static CancellationTokenSource? CreateLinkedCancellation(
        CancellationToken requestCancellationToken,
        CancellationToken connectionCancellationToken,
        out CancellationToken invocationCancellationToken)
    {
        if (!requestCancellationToken.CanBeCanceled)
        {
            invocationCancellationToken = connectionCancellationToken;
            return null;
        }

        if (!connectionCancellationToken.CanBeCanceled || requestCancellationToken == connectionCancellationToken)
        {
            invocationCancellationToken = requestCancellationToken;
            return null;
        }

        var source = CancellationTokenSource.CreateLinkedTokenSource(
            requestCancellationToken,
            connectionCancellationToken);
        invocationCancellationToken = source.Token;
        return source;
    }

    private V2RpcDispatchOutcome ErrorOrSuppress(
        JsonRpcRequestEnvelope request,
        int code,
        string message,
        ErrorPayload? payload,
        ProtocolExecutionOwner? owner,
        V2RpcNotificationSuppressionReason suppressionReason) =>
        request.IsNotification
            ? SuppressNotification(request.Method, owner, suppressionReason)
            : Error(request.Id, code, message, payload, owner);

    private V2RpcDispatchOutcome UnexpectedOrSuppress(
        JsonRpcRequestEnvelope request,
        ProtocolExecutionOwner? owner,
        Exception exception)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        _diagnosticSink.RecordUnexpected(new V2RpcUnexpectedDiagnostic(
            correlationId,
            request.Method,
            owner,
            exception));
        return request.IsNotification
            ? V2RpcDispatchOutcome.NoResponse
            : Error(request.Id, -32603, "Internal error", null, owner, correlationId);
    }

    private V2RpcDispatchOutcome SuppressNotification(
        string method,
        ProtocolExecutionOwner? owner,
        V2RpcNotificationSuppressionReason reason)
    {
        _diagnosticSink.RecordNotificationSuppressed(
            new V2RpcNotificationSuppressionDiagnostic(method, owner, reason));
        return V2RpcDispatchOutcome.NoResponse;
    }

    private V2RpcDispatchOutcome Error(
        JsonRpcRequestId? id,
        int code,
        string message,
        ErrorPayload? payload,
        ProtocolExecutionOwner? owner,
        string? correlationId = null)
    {
        var data = new JsonRpcErrorData(
            payload?.DaemonErrorCode,
            correlationId ?? Guid.NewGuid().ToString("N"),
            payload?.Details,
            originPlugin: null,
            executionOwner: ResolveExecutionOwner(owner));
        var envelope = new JsonRpcErrorResponseEnvelope(id, new JsonRpcErrorObject(code, message, data));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            BuiltInProtocolJsonContext.Default.JsonRpcErrorResponseEnvelope);
        return V2RpcDispatchOutcome.Response(bytes.ToImmutableArray());
    }

    private ProtocolOwnerIdentity? ResolveExecutionOwner(ProtocolExecutionOwner? owner) => owner?.Kind switch
    {
        ProtocolExecutionOwnerKind.BuiltIn => new ProtocolOwnerIdentity(
            "mcsl.daemon",
            _catalog.Document.Info.Version),
        ProtocolExecutionOwnerKind.Plugin => owner.Plugin,
        null => null,
        _ => throw new InvalidOperationException("The frozen RPC entry has an unknown execution owner.")
    };

    private sealed record ErrorPayload(string DaemonErrorCode, JsonElement? Details);
}
