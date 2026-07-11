using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Common.Contracts.Protocol;

namespace MCServerLauncher.Daemon.API.Protocol;

/// <summary>
/// Host-controlled immutable RPC descriptor used for frozen catalog enumeration.
/// </summary>
public abstract class RpcDescriptor
{
    private protected RpcDescriptor(
        RpcMethod method,
        PermissionName permission,
        JsonTypeInfo requestTypeInfo,
        JsonTypeInfo resultTypeInfo,
        bool allowNotification,
        RpcDocumentation? documentation)
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
        Documentation = documentation;
    }

    public RpcMethod Method { get; }

    public PermissionName Permission { get; }

    public JsonTypeInfo RequestTypeInfo { get; }

    public JsonTypeInfo ResultTypeInfo { get; }

    public bool AllowNotification { get; }

    public RpcDocumentation? Documentation { get; }
}

public sealed class RpcDescriptor<TRequest, TResult> : RpcDescriptor
{
    internal RpcDescriptor(
        RpcMethod method,
        PermissionName permission,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResult> resultTypeInfo,
        bool allowNotification = false,
        RpcDocumentation? documentation = null)
        : base(method, permission, requestTypeInfo, resultTypeInfo, allowNotification, documentation)
    {
        RequestTypeInfo = requestTypeInfo;
        ResultTypeInfo = resultTypeInfo;
    }

    public new JsonTypeInfo<TRequest> RequestTypeInfo { get; }

    public new JsonTypeInfo<TResult> ResultTypeInfo { get; }
}

/// <summary>
/// Host-controlled immutable event descriptor used for frozen catalog enumeration.
/// </summary>
public abstract class EventDescriptor
{
    private protected EventDescriptor(
        EventName name,
        PermissionName permission,
        JsonTypeInfo dataTypeInfo,
        JsonTypeInfo? metaTypeInfo,
        OpenRpcEventFieldPresence dataPresence,
        OpenRpcEventFieldPresence metaPresence,
        EventDocumentation? documentation)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(permission);
        ArgumentNullException.ThrowIfNull(dataTypeInfo);
        ValidatePresence(dataPresence, dataTypeInfo, nameof(dataTypeInfo));
        ValidatePresence(metaPresence, metaTypeInfo, nameof(metaTypeInfo));

        Name = name;
        Permission = permission;
        DataTypeInfo = dataTypeInfo;
        MetaTypeInfo = metaTypeInfo;
        DataPresence = dataPresence;
        MetaPresence = metaPresence;
        Documentation = documentation;
    }

    public EventName Name { get; }

    public PermissionName Permission { get; }

    public JsonTypeInfo DataTypeInfo { get; }

    public JsonTypeInfo? MetaTypeInfo { get; }

    public OpenRpcEventFieldPresence DataPresence { get; }

    public OpenRpcEventFieldPresence MetaPresence { get; }

    public EventDocumentation? Documentation { get; }

    private static void ValidatePresence(
        OpenRpcEventFieldPresence presence,
        JsonTypeInfo? typeInfo,
        string parameterName)
    {
        if (!Enum.IsDefined(presence))
        {
            throw new ArgumentOutOfRangeException(nameof(presence));
        }

        if (presence == OpenRpcEventFieldPresence.Omitted && typeInfo is not null)
        {
            throw new ArgumentException("An omitted event field cannot provide JSON metadata.", parameterName);
        }

        if (presence != OpenRpcEventFieldPresence.Omitted && typeInfo is null)
        {
            throw new ArgumentNullException(parameterName, "A present event field requires JSON metadata.");
        }
    }
}

public sealed class EventDescriptor<TData, TMeta> : EventDescriptor
{
    internal EventDescriptor(
        EventName name,
        PermissionName permission,
        JsonTypeInfo<TData> dataTypeInfo,
        JsonTypeInfo<TMeta>? metaTypeInfo,
        OpenRpcEventFieldPresence dataPresence,
        OpenRpcEventFieldPresence metaPresence,
        EventDocumentation? documentation = null)
        : base(name, permission, dataTypeInfo, metaTypeInfo, dataPresence, metaPresence, documentation)
    {
        DataTypeInfo = dataTypeInfo;
        MetaTypeInfo = metaTypeInfo;
    }

    public new JsonTypeInfo<TData> DataTypeInfo { get; }

    public new JsonTypeInfo<TMeta>? MetaTypeInfo { get; }
}
