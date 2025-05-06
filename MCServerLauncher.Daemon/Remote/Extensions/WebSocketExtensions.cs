using System.Linq.Expressions;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using TouchSocket.Core;
using TouchSocket.Http.WebSockets;

namespace MCServerLauncher.Daemon.Remote.Extensions;

public static class WebSocketExtensions
{
    private static Action<IWebSocket, WebSocketCloseStatus>? _cachedSetter;

    public static async Task CloseAsync(this IWebSocket @this, ushort code, string? statusDescription = null)
    {
        if (!@this.Online) return;

        SetCloseStatus(@this, WebSocketCloseStatus.NormalClosure);

        using (var frame = new WSDataFrame { FIN = true, Opcode = WSDataType.Close })
        {
            using (var byteBlock = new ByteBlock(1024))
            {
                byteBlock.WriteUInt16(code, EndianType.Big);
                if (statusDescription.HasValue()) byteBlock.WriteNormalString(statusDescription, Encoding.UTF8);

                frame.PayloadData = byteBlock;
                await @this.SendAsync(frame).ConfigureAwait(EasyTask.ContinueOnCapturedContext);
            }
        }

        await @this.Client.ShutdownAsync(SocketShutdown.Both);
        await @this.Client.CloseAsync(statusDescription).ConfigureAwait(EasyTask.ContinueOnCapturedContext);
    }

    private static void SetCloseStatus(IWebSocket websocket, WebSocketCloseStatus status)
    {
        if (_cachedSetter is null)
        {
            var type = websocket.GetType();
            var prop = type.GetProperty("CloseStatus", BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanWrite)
                throw new InvalidOperationException("Setter not available.");

            // 构建表达式树： (instance, value) => ((InternalWebSocket)instance).CloseStatus = value
            var instanceParam = Expression.Parameter(typeof(IWebSocket));
            var valueParam = Expression.Parameter(typeof(WebSocketCloseStatus));
            var castInstance = Expression.Convert(instanceParam, type);
            var propertyAccess = Expression.Property(castInstance, prop);
            var assignExp = Expression.Assign(propertyAccess, valueParam);
            var lambda = Expression.Lambda<Action<IWebSocket, WebSocketCloseStatus>>(
                assignExp, instanceParam, valueParam
            );
            _cachedSetter = lambda.Compile();
        }

        _cachedSetter(websocket, status);
    }
}