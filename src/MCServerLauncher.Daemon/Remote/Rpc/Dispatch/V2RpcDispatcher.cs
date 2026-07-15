using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;

namespace MCServerLauncher.Daemon.Remote.Rpc.Dispatch;

internal sealed class V2RpcConnectionContext(
    IProtocolPermissionView? permissionView,
    IProtocolSubscriptionOperations? subscriptionOperations,
    CancellationToken connectionCancellationToken,
    IProtocolFileSessionOperations? fileSessionOperations = null)
{
    private readonly ProtocolInvocationContext _builtInInvocationContext = new(
        ProtocolExecutionOwner.BuiltIn,
        permissionView,
        subscriptionOperations,
        fileSessionOperations);

    internal IProtocolPermissionView? PermissionView { get; } = permissionView;

    internal IProtocolSubscriptionOperations? SubscriptionOperations { get; } = subscriptionOperations;

    internal IProtocolFileSessionOperations? FileSessionOperations { get; } = fileSessionOperations;

    internal CancellationToken ConnectionCancellationToken { get; } = connectionCancellationToken;

    internal ProtocolInvocationContext GetInvocationContext(ProtocolExecutionOwner owner) =>
        owner.Kind == ProtocolExecutionOwnerKind.BuiltIn
            ? _builtInInvocationContext
            : new ProtocolInvocationContext(
                owner,
                PermissionView,
                SubscriptionOperations,
                FileSessionOperations);
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

internal readonly struct V2RpcPreparedDispatchOutcome
{
    private V2RpcPreparedDispatchOutcome(
        JsonRpcSuccessResponseEnvelope? successResponse,
        JsonRpcErrorResponseEnvelope? errorResponse,
        ProtocolExecutionOwner? owner,
        ProtocolDownloadAttachment? downloadAttachment)
    {
        if (successResponse is not null && errorResponse is not null)
        {
            throw new ArgumentException("A prepared dispatch outcome cannot contain both response kinds.");
        }

        if (successResponse is null && errorResponse is null && downloadAttachment is not null)
        {
            throw new ArgumentException("A response attachment requires a prepared success response.", nameof(downloadAttachment));
        }

        SuccessResponse = successResponse;
        ErrorResponse = errorResponse;
        Owner = owner;
        DownloadAttachment = downloadAttachment;
    }

    internal static V2RpcPreparedDispatchOutcome NoResponse => default;

    internal JsonRpcSuccessResponseEnvelope? SuccessResponse { get; }

    internal JsonRpcErrorResponseEnvelope? ErrorResponse { get; }

    internal ProtocolExecutionOwner? Owner { get; }

    internal ProtocolDownloadAttachment? DownloadAttachment { get; }

    internal bool HasResponse => SuccessResponse is not null || ErrorResponse is not null;

    internal static V2RpcPreparedDispatchOutcome Success(
        JsonRpcSuccessResponseEnvelope response,
        ProtocolExecutionOwner owner,
        ProtocolDownloadAttachment? downloadAttachment) =>
        new(
            response ?? throw new ArgumentNullException(nameof(response)),
            null,
            owner ?? throw new ArgumentNullException(nameof(owner)),
            downloadAttachment);

    internal static V2RpcPreparedDispatchOutcome Error(JsonRpcErrorResponseEnvelope response) =>
        new(null, response ?? throw new ArgumentNullException(nameof(response)), null, null);
}

internal sealed class V2RpcDispatcher
{
    private static readonly EmptyRequest SharedEmptyRequest = new();
    private static readonly ConcurrentDictionary<string, Permission> PermissionCache = new(StringComparer.Ordinal);
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
            return SerializePreparedOutcome(
                null,
                Error(null, code, message, DaemonErrorWireKind.Validation, null, null));
        }

        var prepared = await DispatchParsedAsync(request, connection, requestCancellationToken).ConfigureAwait(false);
        return SerializePreparedOutcome(request, prepared);
    }

    internal async Task<V2RpcPreparedDispatchOutcome> DispatchParsedAsync(
        JsonRpcRequestEnvelope request,
        V2RpcConnectionContext connection,
        CancellationToken requestCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(connection);

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
                DaemonErrorWireKind.Validation,
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
                DaemonErrorWireKind.NotFound,
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
                DaemonErrorWireKind.Permission,
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
                DaemonErrorWireKind.Validation,
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
            var invocationContext = connection.GetInvocationContext(entry.Owner);
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
            var pluginError = daemonError as PluginError;
            var code = pluginError is not null
                ? -32005
                : daemonError is PermissionDaemonError ? -32001 : -32000;
            var message = pluginError is not null
                ? "Plugin error"
                : daemonError is PermissionDaemonError ? "Permission denied" : "Daemon error";
            return ErrorOrSuppress(
                request,
                code,
                message,
                ToWireKind(daemonError.Kind),
                new ErrorPayload(daemonError.Code, daemonError.Details),
                entry.Owner,
                V2RpcNotificationSuppressionReason.ExpectedDaemonError,
                pluginError is null ? null : new ProtocolOwnerIdentity(pluginError.Identity.Id, pluginError.Identity.Version));
        }

        if (request.IsNotification)
        {
            return V2RpcPreparedDispatchOutcome.NoResponse;
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
            return V2RpcPreparedDispatchOutcome.Success(envelope, entry.Owner, execution.DownloadAttachment);
        }
        catch (Exception exception)
        {
            return UnexpectedOrSuppress(request, entry.Owner, exception);
        }
    }

    private V2RpcDispatchOutcome SerializePreparedOutcome(
        JsonRpcRequestEnvelope? request,
        V2RpcPreparedDispatchOutcome prepared)
    {
        if (!prepared.HasResponse)
        {
            return V2RpcDispatchOutcome.NoResponse;
        }

        if (prepared.ErrorResponse is not null)
        {
            var errorBytes = JsonSerializer.SerializeToUtf8Bytes(
                prepared.ErrorResponse,
                BuiltInProtocolJsonContext.Default.JsonRpcErrorResponseEnvelope);
            return V2RpcDispatchOutcome.Response(ImmutableCollectionsMarshal.AsImmutableArray(errorBytes));
        }

        try
        {
            var successBytes = JsonSerializer.SerializeToUtf8Bytes(
                prepared.SuccessResponse!,
                BuiltInProtocolJsonContext.Default.JsonRpcSuccessResponseEnvelope);
            return V2RpcDispatchOutcome.Response(
                ImmutableCollectionsMarshal.AsImmutableArray(successBytes),
                prepared.DownloadAttachment);
        }
        catch (Exception exception)
        {
            if (request is null)
            {
                throw;
            }

            return SerializePreparedOutcome(request, UnexpectedOrSuppress(request, prepared.Owner, exception));
        }
    }

    internal static object DeserializeRequest(JsonRpcObjectPayload? parameters, RpcDescriptor descriptor)
    {
        if (descriptor.RequestTypeInfo.Type == typeof(EmptyRequest))
        {
            if (parameters is null || parameters.IsEmptyObject)
            {
                return SharedEmptyRequest;
            }

            throw new JsonException("EmptyRequest RPC methods accept only an empty params object.");
        }

        if (parameters is null)
        {
            throw new JsonException("This RPC method requires params.");
        }

        if (parameters.IsEmptyObject)
        {
            throw new JsonException("Only EmptyRequest RPC methods accept an empty params object.");
        }

        return parameters.Deserialize(descriptor.RequestTypeInfo);
    }

    internal static bool HasPermission(IProtocolPermissionView? permissionView, PermissionName requiredPermission)
    {
        if (permissionView is null || permissionView.Permissions.IsDefault)
        {
            return false;
        }

        try
        {
            var required = PermissionCache.GetOrAdd(
                requiredPermission.Value,
                static value => Permission.Of(value));
            return permissionView is ICompiledProtocolPermissionView compiled
                ? compiled.CompiledPermissions.Matches(required)
                : new Permissions(permissionView.Permissions.ToArray()).Matches(required);
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

    private V2RpcPreparedDispatchOutcome ErrorOrSuppress(
        JsonRpcRequestEnvelope request,
        int code,
        string message,
        DaemonErrorWireKind errorKind,
        ErrorPayload? payload,
        ProtocolExecutionOwner? owner,
        V2RpcNotificationSuppressionReason suppressionReason,
        ProtocolOwnerIdentity? originPlugin = null) =>
        request.IsNotification
            ? SuppressNotification(request.Method, owner, suppressionReason)
            : Error(request.Id, code, message, errorKind, payload, owner, originPlugin: originPlugin);

    private V2RpcPreparedDispatchOutcome UnexpectedOrSuppress(
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
            ? V2RpcPreparedDispatchOutcome.NoResponse
            : Error(request.Id, -32603, "Internal error", DaemonErrorWireKind.Internal, null, owner, correlationId);
    }

    private V2RpcPreparedDispatchOutcome SuppressNotification(
        string method,
        ProtocolExecutionOwner? owner,
        V2RpcNotificationSuppressionReason reason)
    {
        _diagnosticSink.RecordNotificationSuppressed(
            new V2RpcNotificationSuppressionDiagnostic(method, owner, reason));
        return V2RpcPreparedDispatchOutcome.NoResponse;
    }

    private V2RpcPreparedDispatchOutcome Error(
        JsonRpcRequestId? id,
        int code,
        string message,
        DaemonErrorWireKind errorKind,
        ErrorPayload? payload,
        ProtocolExecutionOwner? owner,
        string? correlationId = null,
        ProtocolOwnerIdentity? originPlugin = null)
    {
        var data = new JsonRpcErrorData(
            payload?.DaemonErrorCode,
            errorKind,
            correlationId ?? Guid.NewGuid().ToString("N"),
            payload?.Details,
            originPlugin,
            executionOwner: ResolveExecutionOwner(owner));
        var envelope = new JsonRpcErrorResponseEnvelope(id, new JsonRpcErrorObject(code, message, data));
        return V2RpcPreparedDispatchOutcome.Error(envelope);
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

    private static DaemonErrorWireKind ToWireKind(DaemonErrorKind kind) => kind switch
    {
        DaemonErrorKind.Validation => DaemonErrorWireKind.Validation,
        DaemonErrorKind.NotFound => DaemonErrorWireKind.NotFound,
        DaemonErrorKind.Conflict => DaemonErrorWireKind.Conflict,
        DaemonErrorKind.Permission => DaemonErrorWireKind.Permission,
        DaemonErrorKind.Storage => DaemonErrorWireKind.Storage,
        DaemonErrorKind.Transport => DaemonErrorWireKind.Transport,
        DaemonErrorKind.Internal => DaemonErrorWireKind.Internal,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private sealed record ErrorPayload(string DaemonErrorCode, JsonElement? Details);
}
