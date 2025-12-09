using System.Collections.Concurrent;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Daemon.Remote.Authentication;
using TouchSocket.Http.WebSockets;
using TouchSocket.Sockets;

namespace MCServerLauncher.Daemon.Remote;

// TODO: 使用引用GracefulShutdown的CancelToken防止在GetWebsocket().SendAsync在程序关闭时的边界情况
/// <summary>
///     线程安全的ws服务上下文
/// </summary>
public class WsContext
{
    private readonly ConcurrentDictionary<EventType, HashSet<IEventMeta>> _subscribedEvents = new();

    public WsContext(string clientId, Guid jti, string? permissions, DateTime expiredTo)
    {
        ClientId = clientId;
        Permissions = permissions is null ? Permissions.Never : new Permissions(permissions);
        ExpiredTo = expiredTo;
        JTI = jti;
    }

    public Permissions Permissions { get; }
    public DateTime ExpiredTo { get; }
    public Guid JTI { get; }
    public string ClientId { get; }

    public IWebSocket GetWebsocket()
    {
        return Application.HttpService.GetClient(ClientId).WebSocket;
    }

    public void SubscribeEvent(EventType type, IEventMeta? meta)
    {
        if (!_subscribedEvents.TryGetValue(type, out var set))
        {
            set = new HashSet<IEventMeta>();
            _subscribedEvents.TryAdd(type, set);
        }

        if (meta != null) set.Add(meta);
    }

    public void UnsubscribeEvent(EventType type, IEventMeta? meta)
    {
        if (meta != null)
        {
            if (_subscribedEvents.TryGetValue(type, out var set))
            {
                if (set.Count > 1) set.Remove(meta);
                else _subscribedEvents.TryRemove(type, out _);
            }
        }
        else
        {
            _subscribedEvents.TryRemove(type, out _);
        }
    }

    public bool IsSubscribedEvent(EventType type, IEventMeta? meta)
    {
        return _subscribedEvents.TryGetValue(type, out var set) && (meta == null || set.Contains(meta));
    }

    public IEnumerable<IEventMeta> GetEventMetas(EventType type)
    {
        return _subscribedEvents.TryGetValue(type, out var set)
            ? new HashSet<IEventMeta>(set)
            : Enumerable.Empty<IEventMeta>();
    }

    public void UnsubscribeAllEvents()
    {
        _subscribedEvents.Clear();
    }

    // /// <summary>
    // /// 异步发送文本消息。
    // /// </summary>
    // /// <param name="text">要发送的文本内容。</param>
    // /// <param name="endOfMessage">指示是否是消息的结束。默认为<see langword="true"/>。</param>
    // /// <param name="cancellationToken">可取消令箭</param>
    // /// <returns>返回一个任务对象，表示异步操作的结果。</returns>
    // async Task<bool> SendAsync(string text, bool endOfMessage = true)
    // {
    //     try
    //     {
    //         GetWebsocket().SendAsync(text, endOfMessage, )
    //     }
    //     catch (OperationCanceledException e)
    //     {
    //         System.Console.WriteLine(e);
    //         return false;
    //     }
    // }
    //
    // /// <summary>
    // /// 异步发送指定的字节内存数据。
    // /// </summary>
    // /// <param name="memory">要发送的字节数据，作为只读内存块。</param>
    // /// <param name="endOfMessage">指示当前数据是否为消息的结束。默认为<see langword="true"/>。</param>
    // /// <param name="cancellationToken">可取消令箭</param>
    // /// <remarks>
    // /// 此方法允许异步发送数据，通过指定是否为消息的结束来控制数据流。
    // /// </remarks>
    // async Task<bool> SendAsync(ReadOnlyMemory<byte> memory, bool endOfMessage = true)
    // {
    //     try
    //     {
    //
    //     }
    //     catch (OperationCanceledException e)
    //     {
    //         System.Console.WriteLine(e);
    //         return false;
    //     }
    // }
}