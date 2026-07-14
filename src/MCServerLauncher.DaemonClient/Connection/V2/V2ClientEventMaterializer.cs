using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Events;
using MCServerLauncher.Daemon.API.Protocol;
using RustyOptions;

namespace MCServerLauncher.DaemonClient.Connection.V2;

internal static class V2ClientEventMaterializer
{
    internal const string InvalidEventCode = "protocol.event_data_invalid";
    internal const string InvalidEventMessage =
        "A required V2 catalog event payload violates its descriptor metadata.";

    internal static Result<DaemonEvent<TData, TMeta>, DaemonError> Materialize<TData, TMeta>(
        EventDescriptor<TData, TMeta> descriptor,
        JsonRpcRemoteEventNotification notification)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            ArgumentNullException.ThrowIfNull(notification);

            if (!StringComparer.Ordinal.Equals(notification.Method, descriptor.Name.Value))
                throw new InvalidOperationException("The remote event method does not match its descriptor.");

            var meta = MaterializeField(
                descriptor.MetaPresence,
                notification.Params.Meta,
                descriptor.MetaTypeInfo);
            var data = MaterializeField(
                descriptor.DataPresence,
                notification.Params.Data,
                descriptor.DataTypeInfo);

            return Result.Ok<DaemonEvent<TData, TMeta>, DaemonError>(new DaemonEvent<TData, TMeta>(
                notification.Params.Sequence,
                notification.Params.Timestamp,
                meta,
                data));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or
                                           InvalidOperationException or ArgumentException or FormatException or
                                           OverflowException)
        {
            return Result.Err<DaemonEvent<TData, TMeta>, DaemonError>(InvalidEventError());
        }
    }

    private static DaemonEventField<T> MaterializeField<T>(
        OpenRpcEventFieldPresence presence,
        JsonRpcOptionalPayload payload,
        JsonTypeInfo<T>? typeInfo)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!Enum.IsDefined(presence))
            throw new InvalidOperationException("The event descriptor has an invalid field presence.");
        if ((presence == OpenRpcEventFieldPresence.Omitted) != (typeInfo is null))
            throw new InvalidOperationException("The event descriptor presence does not match its JSON metadata.");

        return presence switch
        {
            OpenRpcEventFieldPresence.Omitted when payload.Kind == JsonRpcOptionalPayloadKind.Missing =>
                DaemonEventField<T>.Missing,
            OpenRpcEventFieldPresence.Required when payload.Kind == JsonRpcOptionalPayloadKind.Value =>
                MaterializeValue(payload, typeInfo),
            OpenRpcEventFieldPresence.Optional when payload.Kind == JsonRpcOptionalPayloadKind.Missing =>
                DaemonEventField<T>.Missing,
            OpenRpcEventFieldPresence.Optional when payload.Kind == JsonRpcOptionalPayloadKind.ExplicitNull =>
                DaemonEventField<T>.ExplicitNull,
            OpenRpcEventFieldPresence.Optional when payload.Kind == JsonRpcOptionalPayloadKind.Value =>
                MaterializeValue(payload, typeInfo),
            _ => throw new InvalidOperationException("The remote event field does not match its descriptor presence.")
        };
    }

    private static DaemonEventField<T> MaterializeValue<T>(
        JsonRpcOptionalPayload payload,
        JsonTypeInfo<T>? typeInfo)
    {
        var metadata = typeInfo ?? throw new InvalidOperationException(
            "A present event field requires JSON metadata.");
        var value = payload.Deserialize(metadata);
        if (value is null)
            throw new JsonException("A present remote event field cannot materialize to null.");

        return DaemonEventField<T>.FromValue(value);
    }

    private static TransportDaemonError InvalidEventError() =>
        new(InvalidEventCode, InvalidEventMessage);
}
