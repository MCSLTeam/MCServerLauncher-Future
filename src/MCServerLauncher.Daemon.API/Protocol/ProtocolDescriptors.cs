using System.Text.Json.Serialization.Metadata;

namespace MCServerLauncher.Daemon.API.Protocol;

public sealed class RpcDescriptor<TRequest, TResult>
{
    public RpcDescriptor(
        RpcMethod method,
        PermissionName permission,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResult> resultTypeInfo,
        bool allowNotification = false)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(permission);
        ArgumentNullException.ThrowIfNull(requestTypeInfo);
        ArgumentNullException.ThrowIfNull(resultTypeInfo);

        Method = method;
        Permission = permission;
        RequestTypeInfo = requestTypeInfo;
        ResultTypeInfo = resultTypeInfo;
        AllowNotification = allowNotification;
    }

    public RpcMethod Method { get; }

    public PermissionName Permission { get; }

    public JsonTypeInfo<TRequest> RequestTypeInfo { get; }

    public JsonTypeInfo<TResult> ResultTypeInfo { get; }

    public bool AllowNotification { get; }
}

public sealed class EventDescriptor<TData, TMeta>
{
    public EventDescriptor(
        EventName name,
        PermissionName permission,
        JsonTypeInfo<TData> dataTypeInfo,
        JsonTypeInfo<TMeta> metaTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(permission);
        ArgumentNullException.ThrowIfNull(dataTypeInfo);
        ArgumentNullException.ThrowIfNull(metaTypeInfo);

        Name = name;
        Permission = permission;
        DataTypeInfo = dataTypeInfo;
        MetaTypeInfo = metaTypeInfo;
    }

    public EventName Name { get; }

    public PermissionName Permission { get; }

    public JsonTypeInfo<TData> DataTypeInfo { get; }

    public JsonTypeInfo<TMeta> MetaTypeInfo { get; }
}
