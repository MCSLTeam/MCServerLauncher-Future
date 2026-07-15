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
}
