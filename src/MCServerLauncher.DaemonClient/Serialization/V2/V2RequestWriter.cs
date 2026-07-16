using System.Buffers;
using System;
using System.Collections.Immutable;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.DaemonClient.Serialization.V2;

internal static class V2RequestWriter
{
    public static ImmutableArray<byte> Write<TRequest, TResult>(
        RpcDescriptor<TRequest, TResult> descriptor,
        JsonRpcRequestId id,
        TRequest request)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(id);

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteString("jsonrpc", JsonRpcWireConstants.Version);
        writer.WriteString("method", descriptor.Method.Value);
        writer.WritePropertyName("id");
        JsonSerializer.Serialize(writer, id, BuiltInProtocolJsonContext.Default.JsonRpcRequestId);
        if (typeof(TRequest) != typeof(EmptyRequest))
        {
            writer.WritePropertyName("params");
            JsonSerializer.Serialize(writer, request, descriptor.RequestTypeInfo);
        }

        writer.WriteEndObject();
        writer.Flush();
        // Own the written buffer once; avoid WrittenSpan.ToArray() + CreateRange double copy.
        return System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(
            buffer.WrittenSpan.ToArray());
    }
}
