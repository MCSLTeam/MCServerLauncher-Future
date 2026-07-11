using System.Reflection;
using System.Security.Cryptography;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.DaemonClient.WebSocketPlugin;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Http.WebSockets;

namespace MCServerLauncher.ProtocolTests;

public class LegacyBinaryUploadMigrationCharacterizationTests
{
    private const int LegacyV1FileIdLength = 16;
    private const int LegacyV1OffsetLength = 8;
    private const int LegacyV1ChecksumLength = 20;
    private const int LegacyV1HeaderLength =
        LegacyV1FileIdLength + LegacyV1OffsetLength + LegacyV1ChecksumLength;

    [Fact]
    [Trait("Category", "LegacyBinaryUploadMigration")]
    public async Task LegacyV1RawBinaryUploadMigration_44ByteHeader_ProducesPartialTextAcknowledgement()
    {
        const int uploadSize = 32;
        const long offset = 0;
        var chunk = new byte[] { 0x4d, 0x43, 0x53, 0x4c, 0x2d, 0x56, 0x31, 0x21 };
        var relativePath = $"caches/uploads/legacy-v1-migration-{Guid.NewGuid():N}.bin";
        var resolvedPath = FileManager.ResolveAndValidatePath(relativePath);
        var fileId = Guid.Empty;

        try
        {
            var opened = await FileSessionCoordinator.Shared.OpenLegacyUploadAsync(
                relativePath,
                uploadSize,
                sha1: null,
                CancellationToken.None);
            Assert.True(opened.IsOk(out var uploadSession));
            fileId = uploadSession.SessionId;

            var payload = BuildLegacyV1MigrationFrame(fileId, offset, chunk);

            Assert.Equal(LegacyV1HeaderLength + chunk.Length, payload.Length);
            Assert.Equal(fileId, new Guid(payload.AsSpan(0, LegacyV1FileIdLength)));
            Assert.Equal(offset, BitConverter.ToInt64(payload, LegacyV1FileIdLength));
            Assert.Equal(
                SHA1.HashData(chunk),
                payload.AsSpan(LegacyV1FileIdLength + LegacyV1OffsetLength, LegacyV1ChecksumLength).ToArray());
            Assert.Equal(chunk, payload.AsSpan(LegacyV1HeaderLength).ToArray());

            var acknowledgement = await InvokeLegacyV1DaemonBinaryUploadSeamAsync(payload);

            Assert.Equal(WSDataType.Text, acknowledgement.Opcode);
            Assert.True(acknowledgement.Fin);

            var parsed = WsReceivedPlugin.ParseInboundEnvelopeFromBytes(acknowledgement.Payload);
            Assert.Equal(WsReceivedPlugin.InboundEnvelopeType.BinaryUpload, parsed.EnvelopeType);
            Assert.True(parsed.BinaryUploadResponse.HasValue);

            var response = parsed.BinaryUploadResponse.Value;
            Assert.Equal(fileId, response.FileId);
            Assert.False(response.Done);
            Assert.Equal(chunk.Length, response.Received);
            Assert.Null(response.Error);
        }
        finally
        {
            if (fileId != Guid.Empty)
                await FileSessionCoordinator.Shared.CancelUploadAsync(fileId, CancellationToken.None);

            DeleteIfExists(resolvedPath);
            DeleteIfExists(resolvedPath + ".tmp");
        }
    }

    private static byte[] BuildLegacyV1MigrationFrame(Guid fileId, long offset, byte[] chunk)
    {
        // Migration evidence only: this deliberately mirrors the retiring V1 wire layout.
        var checksum = SHA1.HashData(chunk);
        var payload = new byte[LegacyV1HeaderLength + chunk.Length];

        Assert.True(fileId.TryWriteBytes(payload.AsSpan(0, LegacyV1FileIdLength)));
        Assert.True(BitConverter.TryWriteBytes(payload.AsSpan(LegacyV1FileIdLength, LegacyV1OffsetLength), offset));
        Array.Copy(checksum, 0, payload, LegacyV1FileIdLength + LegacyV1OffsetLength, LegacyV1ChecksumLength);
        Array.Copy(chunk, 0, payload, LegacyV1HeaderLength, chunk.Length);

        return payload;
    }

    private static async Task<CapturedLegacyAcknowledgement> InvokeLegacyV1DaemonBinaryUploadSeamAsync(byte[] payload)
    {
        CapturedLegacyAcknowledgement? captured = null;
        var webSocket = CreateProxy<IWebSocket>((method, args) =>
        {
            if (method.Name == "SendAsync" && args is { Length: > 0 } && args[0] is WSDataFrame frame)
            {
                captured = new CapturedLegacyAcknowledgement(
                    frame.PayloadData.ToArray(),
                    frame.Opcode,
                    frame.FIN);
                return Task.CompletedTask;
            }

            return GetDefaultReturnValue(method.ReturnType);
        });

        var plugin = new WsActionPlugin(
            CreateProxy<IActionExecutor>((method, _) => GetDefaultReturnValue(method.ReturnType)),
            CreateProxy<IHttpService>((method, _) => GetDefaultReturnValue(method.ReturnType)),
            new WsContextContainer());

        var method = typeof(WsActionPlugin).GetMethod(
                         "HandleBinaryFileUploadChunk",
                         BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(typeof(WsActionPlugin).FullName, "HandleBinaryFileUploadChunk");

        var task = method.Invoke(plugin, [webSocket, payload]) as Task
                   ?? throw new InvalidOperationException("Legacy V1 binary upload seam did not return a task.");
        await task;

        return captured
               ?? throw new InvalidOperationException("Legacy V1 binary upload seam did not send an acknowledgement.");
    }

    private static T CreateProxy<T>(Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, InterfaceDispatchProxy>();
        ((InterfaceDispatchProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private static object? GetDefaultReturnValue(Type returnType)
    {
        if (returnType == typeof(void))
            return null;
        if (returnType == typeof(Task))
            return Task.CompletedTask;
        if (returnType == typeof(ValueTask))
            return ValueTask.CompletedTask;

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var resultValue = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
            return typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, [resultValue]);
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var resultValue = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
            return Activator.CreateInstance(returnType, resultValue);
        }

        return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private readonly record struct CapturedLegacyAcknowledgement(byte[] Payload, WSDataType Opcode, bool Fin);

    private class InterfaceDispatchProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = (_, _) => null;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return Handler(targetMethod ?? throw new MissingMethodException("DispatchProxy target method was null."), args);
        }
    }
}
