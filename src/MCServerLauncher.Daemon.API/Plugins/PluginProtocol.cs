using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.Daemon.API.Plugins;

/// <summary>
/// Creates typed protocol descriptors for plugin registration.
/// </summary>
public static class PluginProtocol
{
    public static RpcDescriptor<TRequest, TResult> CreateRpc<TRequest, TResult>(
        string method,
        string permission,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResult> resultTypeInfo,
        RpcDocumentation documentation,
        bool allowNotification = false)
    {
        ArgumentNullException.ThrowIfNull(documentation);
        EnsureSourceGenerated(requestTypeInfo);
        EnsureSourceGenerated(resultTypeInfo);
        requestTypeInfo.MakeReadOnly();
        resultTypeInfo.MakeReadOnly();

        return new RpcDescriptor<TRequest, TResult>(
            new RpcMethod(method),
            new PermissionName(permission),
            requestTypeInfo,
            resultTypeInfo,
            allowNotification,
            documentation);
    }

    public static EventDescriptor<TData, TMeta> CreateEvent<TData, TMeta>(
        string name,
        string permission,
        JsonTypeInfo<TData> dataTypeInfo,
        JsonTypeInfo<TMeta>? metaTypeInfo,
        EventDocumentation documentation,
        OpenRpcEventFieldPresence dataPresence = OpenRpcEventFieldPresence.Required,
        OpenRpcEventFieldPresence metaPresence = OpenRpcEventFieldPresence.Omitted)
    {
        ArgumentNullException.ThrowIfNull(documentation);
        EnsureSourceGenerated(dataTypeInfo);
        if (metaTypeInfo is not null)
            EnsureSourceGenerated(metaTypeInfo);
        dataTypeInfo.MakeReadOnly();
        metaTypeInfo?.MakeReadOnly();

        return new EventDescriptor<TData, TMeta>(
            new EventName(name),
            new PermissionName(permission),
            dataTypeInfo,
            metaTypeInfo,
            dataPresence,
            metaPresence,
            documentation);
    }

    private static void EnsureSourceGenerated<T>(JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        // Source-generated contracts originate from a JsonSerializerContext resolver.
        // Check the producing resolver rather than the options configuration: callers can
        // create reflection-backed or manually-created metadata with source-generated options.
        if (typeInfo.OriginatingResolver is not JsonSerializerContext)
        {
            throw new ArgumentException(
                "Plugin protocol metadata must use source-generated JsonTypeInfo from a JsonSerializerContext.",
                nameof(typeInfo));
        }
    }
}
