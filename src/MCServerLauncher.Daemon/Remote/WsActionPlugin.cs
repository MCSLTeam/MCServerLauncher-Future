using System.Linq;
using System.Text.Json;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Serialization;
using StjJsonSerializer = System.Text.Json.JsonSerializer;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Http.WebSockets;

namespace MCServerLauncher.Daemon.Remote;

internal record struct BinaryUploadResponse(Guid FileId, bool Done, long Received);
internal record struct BinaryUploadErrorResponse(Guid FileId, string Error);

public class WsActionPlugin(IActionExecutor executor,
    IHttpService httpService, WsContextContainer container)
    : PluginBase, IWsPlugin, IWebSocketReceivedPlugin

{
    public async Task OnWebSocketReceived(IWebSocket webSocket, WSDataFrameEventArgs e)
    {
        if (e.DataFrame.IsText)
        {
            var context = Container.GetContext((webSocket.Client as IHttpSessionClient)!.Id);
            var response = executor.ProcessAction(e.DataFrame.PayloadData, context);

            if (response is not null)
            {
                var utf8Payload = StjJsonSerializer.SerializeToUtf8Bytes(response, DaemonRpcTypeInfoCache<ActionResponse>.TypeInfo);
                var frame = new WSDataFrame(utf8Payload)
                {
                    Opcode = WSDataType.Text,
                    FIN = true
                };
                await webSocket.SendAsync(frame);
            }
        }
        else if (e.DataFrame.Opcode == WSDataType.Binary)
        {
            await HandleBinaryFileUploadChunk(webSocket, e.DataFrame.PayloadData.ToArray());
        }

        await e.InvokeNext();
    }

    private async Task HandleBinaryFileUploadChunk(IWebSocket webSocket, byte[] payload)
    {
        if (payload.Length < 44) return; // 16 + 8 + 20 = 44 minimum

        var fileIdBytes = payload.AsSpan(0, 16);
        var fileId = new Guid(fileIdBytes);

        var offsetBytes = payload.AsSpan(16, 8);
        var offset = BitConverter.ToInt64(offsetBytes);

        var expectedChecksum = payload.AsSpan(24, 20).ToArray();
        var data = payload.AsSpan(44).ToArray();

        // Verify checksum
        var actualChecksum = System.Security.Cryptography.SHA1.HashData(data);
        if (!expectedChecksum.SequenceEqual(actualChecksum))
        {
            var expectedHex = BitConverter.ToString(expectedChecksum).Replace("-", "");
            var actualHex = BitConverter.ToString(actualChecksum).Replace("-", "");
            Serilog.Log.Error("[WsActionPlugin] Checksum mismatch: fileId={FileId}, offset={Offset}, expected={Expected}, actual={Actual}",
                fileId, offset, expectedHex, actualHex);

            var errorResponse = new BinaryUploadErrorResponse(fileId, $"Checksum mismatch at offset {offset}");
            var utf8Payload = StjJsonSerializer.SerializeToUtf8Bytes(errorResponse,
                DaemonRpcTypeInfoCache<BinaryUploadErrorResponse>.TypeInfo);
            var frame = new WSDataFrame(utf8Payload)
            {
                Opcode = WSDataType.Text,
                FIN = true
            };
            await webSocket.SendAsync(frame);
            return;
        }

        var checksumHex = BitConverter.ToString(actualChecksum).Replace("-", "");
        Serilog.Log.Debug("[WsActionPlugin] Server received chunk: fileId={FileId}, offset={Offset}, length={Length}, checksum={Checksum}, first4bytes={First4}",
            fileId, offset, data.Length, checksumHex, BitConverter.ToString(data.Take(4).ToArray()));

        try
        {
            var (done, received) = await Storage.FileManager.FileUploadChunk(fileId, offset, data);

            Serilog.Log.Debug("[WsActionPlugin] Server wrote chunk: done={Done}, received={Received}", done, received);

            var response = new BinaryUploadResponse(fileId, done, received);
            var utf8Payload = StjJsonSerializer.SerializeToUtf8Bytes(response,
                DaemonRpcTypeInfoCache<BinaryUploadResponse>.TypeInfo);
            var frame = new WSDataFrame(utf8Payload)
            {
                Opcode = WSDataType.Text,
                FIN = true
            };
            await webSocket.SendAsync(frame);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error("[WsActionPlugin] Binary file upload chunk failed: {0}", ex);
            var errorResponse = new BinaryUploadErrorResponse(fileId, ex.Message);
            var utf8Payload = StjJsonSerializer.SerializeToUtf8Bytes(errorResponse,
                DaemonRpcTypeInfoCache<BinaryUploadErrorResponse>.TypeInfo);
            var frame = new WSDataFrame(utf8Payload)
            {
                Opcode = WSDataType.Text,
                FIN = true
            };
            await webSocket.SendAsync(frame);
        }
    }


    public IHttpService HttpService { get; init; } = httpService;
    public WsContextContainer Container { get; init; } = container;
}
