using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using RustyOptions;

namespace MCServerLauncher.DaemonClient.Connection.V2;

internal enum V2ClientEventFieldKind
{
    Missing,
    ExplicitNull,
    Value
}

internal sealed class V2ClientEventField<T>
{
    private readonly T? _value;

    private V2ClientEventField(V2ClientEventFieldKind kind, T? value = default)
    {
        Kind = kind;
        _value = value;
    }

    internal static V2ClientEventField<T> Missing { get; } =
        new(V2ClientEventFieldKind.Missing);

    internal static V2ClientEventField<T> ExplicitNull { get; } =
        new(V2ClientEventFieldKind.ExplicitNull);

    internal V2ClientEventFieldKind Kind { get; }

    internal T Value => Kind == V2ClientEventFieldKind.Value
        ? _value!
        : throw new InvalidOperationException("Only a materialized value event field exposes a value.");

    internal static V2ClientEventField<T> FromValue(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new V2ClientEventField<T>(V2ClientEventFieldKind.Value, value);
    }
}

internal sealed record V2ClientEvent<TData, TMeta>(
    long Sequence,
    long Timestamp,
    V2ClientEventField<TMeta> Meta,
    V2ClientEventField<TData> Data);

internal static class V2ClientEventMaterializer
{
    internal const string InvalidEventCode = "protocol.event_data_invalid";
    internal const string InvalidEventMessage =
        "A required V2 catalog event payload violates its descriptor metadata.";

    internal static Result<V2ClientEvent<TData, TMeta>, DaemonError> Materialize<TData, TMeta>(
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

            return Result.Ok<V2ClientEvent<TData, TMeta>, DaemonError>(new V2ClientEvent<TData, TMeta>(
                notification.Params.Sequence,
                notification.Params.Timestamp,
                meta,
                data));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or
                                           InvalidOperationException or ArgumentException or FormatException or
                                           OverflowException)
        {
            return Result.Err<V2ClientEvent<TData, TMeta>, DaemonError>(InvalidEventError());
        }
    }

    private static V2ClientEventField<T> MaterializeField<T>(
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
                V2ClientEventField<T>.Missing,
            OpenRpcEventFieldPresence.Required when payload.Kind == JsonRpcOptionalPayloadKind.Value =>
                MaterializeValue(payload, typeInfo),
            OpenRpcEventFieldPresence.Optional when payload.Kind == JsonRpcOptionalPayloadKind.Missing =>
                V2ClientEventField<T>.Missing,
            OpenRpcEventFieldPresence.Optional when payload.Kind == JsonRpcOptionalPayloadKind.ExplicitNull =>
                V2ClientEventField<T>.ExplicitNull,
            OpenRpcEventFieldPresence.Optional when payload.Kind == JsonRpcOptionalPayloadKind.Value =>
                MaterializeValue(payload, typeInfo),
            _ => throw new InvalidOperationException("The remote event field does not match its descriptor presence.")
        };
    }

    private static V2ClientEventField<T> MaterializeValue<T>(
        JsonRpcOptionalPayload payload,
        JsonTypeInfo<T>? typeInfo)
    {
        var metadata = typeInfo ?? throw new InvalidOperationException(
            "A present event field requires JSON metadata.");
        var value = payload.Deserialize(metadata);
        if (value is null)
            throw new JsonException("A present remote event field cannot materialize to null.");

        return V2ClientEventField<T>.FromValue(value);
    }

    private static TransportDaemonError InvalidEventError() =>
        new(InvalidEventCode, InvalidEventMessage);
}
