using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.API.State;
using MCServerLauncher.Daemon.Remote.Rpc.Dispatch;
using RustyOptions;

namespace MCServerLauncher.Daemon.Remote.Rpc.Catalog;

internal static class BuiltInConnectionDiscoverySystemRpcRegistrar
{
    private const string PermissionsMethod = "mcsl.auth.permissions.get";
    private const string PingMethod = "mcsl.daemon.ping";
    private const string ExtensionDispatchMethod = "mcsl.extension.dispatch";
    private const string SubscribeMethod = "mcsl.event.subscribe";
    private const string UnsubscribeMethod = "mcsl.event.unsubscribe";
    private const string DiscoverMethod = "rpc.discover";
    private const string CatalogMethod = "mcsl.instance.catalog.get";
    private const string JavaListMethod = "mcsl.java.list";
    private const string SystemInfoMethod = "mcsl.system.info.get";

    public static void Register(
        ProtocolCatalogBuilder builder,
        ISystemApplication systemApplication,
        IInstanceSnapshotSource snapshotSource,
        TimeProvider timeProvider,
        IFrozenProtocolCatalogAccessor catalogAccessor)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(systemApplication);
        ArgumentNullException.ThrowIfNull(snapshotSource);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(catalogAccessor);

        builder.RegisterBuiltInRpc(
            Descriptor<EmptyRequest, PermissionsResult>(PermissionsMethod),
            new RpcBinding<EmptyRequest, PermissionsResult>(
                ProtocolExecutionOwner.BuiltIn,
                static (context, _, _) => Task.FromResult(GetPermissions(context))));
        builder.RegisterBuiltInRpc(
            Descriptor<EmptyRequest, PingResult>(PingMethod),
            new RpcBinding<EmptyRequest, PingResult>(
                ProtocolExecutionOwner.BuiltIn,
                (_, _, _) => Task.FromResult(ProtocolRpcExecution<PingResult>.Ok(
                    new PingResult(timeProvider.GetUtcNow().ToUnixTimeMilliseconds())))));
        builder.RegisterBuiltInRpc(
            Descriptor<ExtensionProtocolDispatchRequest, ExtensionProtocolDispatchResult>(ExtensionDispatchMethod),
            new RpcBinding<ExtensionProtocolDispatchRequest, ExtensionProtocolDispatchResult>(
                ProtocolExecutionOwner.BuiltIn,
                (context, request, cancellationToken) => DispatchExtensionProtocolAsync(context, catalogAccessor, request, cancellationToken)));
        builder.RegisterBuiltInRpc(
            Descriptor<EventSubscriptionRequest, UnitResult>(SubscribeMethod),
            new RpcBinding<EventSubscriptionRequest, UnitResult>(
                ProtocolExecutionOwner.BuiltIn,
                static (context, request, _) => Task.FromResult(UpdateSubscription(context, request, subscribe: true))));
        builder.RegisterBuiltInRpc(
            Descriptor<EventSubscriptionRequest, UnitResult>(UnsubscribeMethod),
            new RpcBinding<EventSubscriptionRequest, UnitResult>(
                ProtocolExecutionOwner.BuiltIn,
                static (context, request, _) => Task.FromResult(UpdateSubscription(context, request, subscribe: false))));
        builder.RegisterBuiltInRpc(
            Descriptor<EmptyRequest, OpenRpcDocument>(DiscoverMethod),
            new RpcBinding<EmptyRequest, OpenRpcDocument>(
                ProtocolExecutionOwner.BuiltIn,
                (_, _, _) => Task.FromResult(Discover(catalogAccessor))));
        builder.RegisterBuiltInRpc(
            Descriptor<EmptyRequest, InstanceCatalogResult>(CatalogMethod),
            new RpcBinding<EmptyRequest, InstanceCatalogResult>(
                ProtocolExecutionOwner.BuiltIn,
                (_, _, _) => Task.FromResult(GetInstanceCatalog(snapshotSource))));
        builder.RegisterBuiltInRpc(
            Descriptor<EmptyRequest, JavaRuntimeList>(JavaListMethod),
            new RpcBinding<EmptyRequest, JavaRuntimeList>(
                ProtocolExecutionOwner.BuiltIn,
                async (_, _, cancellationToken) => ToExecution(
                    await systemApplication.ListJavaRuntimesAsync(cancellationToken).ConfigureAwait(false))));
        builder.RegisterBuiltInRpc(
            Descriptor<EmptyRequest, SystemInfo>(SystemInfoMethod),
            new RpcBinding<EmptyRequest, SystemInfo>(
                ProtocolExecutionOwner.BuiltIn,
                async (_, _, cancellationToken) => ToExecution(
                    await systemApplication.GetSystemInfoAsync(cancellationToken).ConfigureAwait(false))));
    }

    private static async Task<ProtocolRpcExecution<ExtensionProtocolDispatchResult>> DispatchExtensionProtocolAsync(
        ProtocolInvocationContext context,
        IFrozenProtocolCatalogAccessor catalogAccessor,
        ExtensionProtocolDispatchRequest request,
        CancellationToken cancellationToken)
    {
        var envelope = request.Envelope;
        if (envelope.ValueKind != JsonValueKind.Object)
            return ExtensionError(null, null, "envelope_invalid", "Extension protocol envelope must be an object.");

        var protocol = ReadString(envelope, "protocol");
        var id = ReadString(envelope, "id");
        var plugin = ReadString(envelope, "plugin");
        if (!StringComparer.Ordinal.Equals(protocol, "mcsl.extension.v1"))
            return ExtensionError(id, plugin, "protocol_invalid", "protocol must be mcsl.extension.v1.");

        var type = ReadString(envelope, "type");
        if (!StringComparer.Ordinal.Equals(type, "request"))
            return ExtensionError(id, plugin, "type_unsupported", "Only request envelopes can be dispatched through mcsl.extension.dispatch.");

        if (string.IsNullOrWhiteSpace(id))
            return ExtensionError(null, plugin, "request_id_missing", "Extension protocol request id is required.");
        if (!IsCanonicalPluginId(plugin))
            return ExtensionError(id, plugin, "plugin_invalid", "Extension protocol plugin id is invalid.");

        var command = ReadString(envelope, "command");
        if (string.IsNullOrWhiteSpace(command))
            return ExtensionError(id, plugin, "command_missing", "Extension protocol command is required.");

        JsonElement? parameters = null;
        if (envelope.TryGetProperty("params", out var parameterElement))
        {
            if (parameterElement.ValueKind != JsonValueKind.Object)
                return ExtensionError(id, plugin, "params_invalid", "Extension protocol params must be an object when present.");
            if (TryFindUnsafeKey(parameterElement, "$.params", out var unsafePath))
                return ExtensionError(id, plugin, "json_key_unsafe", $"Unsafe extension protocol object key at '{unsafePath}'.");
            parameters = parameterElement.Clone();
        }

        if (!catalogAccessor.TryGet(out var catalog))
            return ExtensionError(id, plugin, "protocol_catalog_unavailable", "The frozen runtime protocol catalog is not available.");

        if (!catalog!.IsExtensionCommandDeclared(plugin!, command))
            return ExtensionError(id, plugin, "command_undeclared", $"Extension command '{command}' is not declared by plugin '{plugin}'.");

        var targetMethodValue = $"plugin.{plugin}.rpc.{command}";
        RpcMethod targetMethod;
        try
        {
            targetMethod = new RpcMethod(targetMethodValue);
        }
        catch (ArgumentException)
        {
            return ExtensionError(id, plugin, "command_invalid", "Extension protocol command is not a valid plugin RPC command path.");
        }

        if (!catalog!.TryGetRpc(targetMethod, out var target))
            return ExtensionError(id, plugin, "command_unavailable", $"No daemon extension command '{command}' is registered for plugin '{plugin}'.");
        if (target.Owner.Kind != ProtocolExecutionOwnerKind.Plugin ||
            !StringComparer.Ordinal.Equals(target.Owner.Plugin?.Id, plugin))
            return ExtensionError(id, plugin, "command_owner_mismatch", "The target command is not owned by the requested plugin.");
        if (!V2RpcDispatcher.HasPermission(context.PermissionView, target.Descriptor.Permission))
            return ExtensionError(id, plugin, "permission_denied", "The caller is not permitted to invoke the target extension command.");

        object typedRequest;
        try
        {
            var payload = parameters is null
                ? null
                : JsonRpcObjectPayload.FromValidatedUtf8Object(Encoding.UTF8.GetBytes(parameters.Value.GetRawText()));
            typedRequest = V2RpcDispatcher.DeserializeRequest(payload, target.Descriptor);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return ExtensionError(id, plugin, "params_invalid", "Extension protocol params do not match the target command contract.");
        }

        ErasedProtocolRpcExecution execution;
        try
        {
            var targetContext = new ProtocolInvocationContext(
                target.Owner,
                context.PermissionView,
                context.SubscriptionOperations,
                context.FileSessionOperations,
                context.ConsoleSessionOperations);
            execution = await target.Binding.InvokeAsync(targetContext, typedRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ExtensionError(id, plugin, "command_failed", "The target extension command failed unexpectedly.");
        }

        if (execution.Result.IsErr(out var error))
            return ExtensionError(id, plugin, error!.Code, error.Message, error.Details);

        var value = execution.Result.Unwrap();
        if (value.GetType() != target.Descriptor.ResultTypeInfo.Type)
            return ExtensionError(id, plugin, "command_result_invalid", "The target extension command returned an invalid result type.");

        var result = JsonSerializer.SerializeToElement(value, target.Descriptor.ResultTypeInfo);
        var response = new JsonObject
        {
            ["protocol"] = "mcsl.extension.v1",
            ["type"] = "response",
            ["id"] = id,
            ["plugin"] = plugin,
            ["result"] = JsonNode.Parse(result.GetRawText())
        };
        return ProtocolRpcExecution<ExtensionProtocolDispatchResult>.Ok(new ExtensionProtocolDispatchResult(ToElement(response)));
    }

    private static ProtocolRpcExecution<ExtensionProtocolDispatchResult> ExtensionError(
        string? id,
        string? plugin,
        string code,
        string message,
        JsonElement? details = null)
    {
        var error = new JsonObject
        {
            ["protocol"] = "mcsl.extension.v1",
            ["type"] = "error",
            ["code"] = code,
            ["message"] = message
        };
        if (!string.IsNullOrWhiteSpace(id))
            error["id"] = id;
        if (!string.IsNullOrWhiteSpace(plugin))
            error["plugin"] = plugin;
        if (details is not null)
            error["details"] = JsonNode.Parse(details.Value.GetRawText());

        return ProtocolRpcExecution<ExtensionProtocolDispatchResult>.Ok(new ExtensionProtocolDispatchResult(ToElement(error)));
    }

    private static JsonElement ToElement(JsonObject value)
    {
        using var document = JsonDocument.Parse(value.ToJsonString());
        return document.RootElement.Clone();
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool IsCanonicalPluginId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var segmentStart = true;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var isLetter = character is >= 'a' and <= 'z';
            var isDigit = character is >= '0' and <= '9';

            if (character == '.')
            {
                if (segmentStart || index == value.Length - 1 || value[index - 1] == '-')
                    return false;
                segmentStart = true;
                continue;
            }

            if ((segmentStart && !isLetter && !isDigit) || (!isLetter && !isDigit && character != '-'))
                return false;
            if (index == value.Length - 1 && character == '-')
                return false;
            segmentStart = false;
        }

        return true;
    }

    private static bool TryFindUnsafeKey(JsonElement element, string path, [NotNullWhen(true)] out string? unsafePath)
    {
        unsafePath = null;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var propertyPath = $"{path}.{property.Name}";
                if (property.Name is "__proto__" or "prototype" or "constructor")
                {
                    unsafePath = propertyPath;
                    return true;
                }

                if (TryFindUnsafeKey(property.Value, propertyPath, out unsafePath))
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindUnsafeKey(item, $"{path}[{index++}]", out unsafePath))
                    return true;
            }
        }

        return false;
    }

    private static ProtocolRpcExecution<PermissionsResult> GetPermissions(ProtocolInvocationContext context)
    {
        if (context.PermissionView is null)
        {
            return ProtocolRpcExecution<PermissionsResult>.Err(MissingConnectionCapability("permission view"));
        }

        return ProtocolRpcExecution<PermissionsResult>.Ok(new PermissionsResult(context.PermissionView.Permissions));
    }

    private static ProtocolRpcExecution<UnitResult> UpdateSubscription(
        ProtocolInvocationContext context,
        EventSubscriptionRequest request,
        bool subscribe)
    {
        if (context.SubscriptionOperations is null)
        {
            return ProtocolRpcExecution<UnitResult>.Err(MissingConnectionCapability("subscription operations"));
        }

        var result = subscribe
            ? context.SubscriptionOperations.Subscribe(request)
            : context.SubscriptionOperations.Unsubscribe(request);
        return result.IsOk(out _)
            ? ProtocolRpcExecution<UnitResult>.Ok(new UnitResult())
            : ProtocolRpcExecution<UnitResult>.Err(result.UnwrapErr());
    }

    private static ProtocolRpcExecution<OpenRpcDocument> Discover(IFrozenProtocolCatalogAccessor catalogAccessor)
    {
        if (!catalogAccessor.TryGet(out var catalog))
        {
            return ProtocolRpcExecution<OpenRpcDocument>.Err(new InternalDaemonError(
                "protocol.catalog.unavailable",
                "The frozen runtime protocol catalog is not available."));
        }

        return ProtocolRpcExecution<OpenRpcDocument>.Ok(catalog!.Document);
    }

    private static ProtocolRpcExecution<InstanceCatalogResult> GetInstanceCatalog(
        IInstanceSnapshotSource snapshotSource)
    {
        var published = snapshotSource.Current;
        var items = published.Value.Instances.Values
            .Select(static snapshot => new InstanceCatalogItem(
                snapshot.Id,
                snapshot.Name,
                snapshot.InstanceType,
                snapshot.Version,
                snapshot.Status,
                snapshot.ReadyTimedOut))
            .ToImmutableArray();
        return ProtocolRpcExecution<InstanceCatalogResult>.Ok(
            new InstanceCatalogResult(published.Version, items));
    }

    private static ProtocolRpcExecution<TResult> ToExecution<TResult>(Result<TResult, DaemonError> result)
        where TResult : notnull =>
        result.IsOk(out var value)
            ? ProtocolRpcExecution<TResult>.Ok(value)
            : ProtocolRpcExecution<TResult>.Err(result.UnwrapErr());

    private static InternalDaemonError MissingConnectionCapability(string capability) =>
        new(
            "protocol.connection.capability.unavailable",
            $"The RPC invocation does not provide the required connection {capability}.");

    private static RpcDescriptor<TRequest, TResult> Descriptor<TRequest, TResult>(string method) =>
        (RpcDescriptor<TRequest, TResult>)BuiltInProtocolDefinitions.Rpcs.Single(
            descriptor => StringComparer.Ordinal.Equals(descriptor.Method.Value, method));
}
