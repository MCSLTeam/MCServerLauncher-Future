using System.Collections.Concurrent;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Daemon.Remote.Authentication;
using RustyOptions;
using TouchSocket.Http.WebSockets;
using TouchSocket.Sockets;

namespace MCServerLauncher.Daemon.Remote;

public sealed record EventContext(EventType Type, IEventFilter? Filter);

// TODO: 使用引用GracefulShutdown的CancelToken防止在GetWebsocket().SendAsync在程序关闭时的边界情况
/// <summary>
///     线程安全的ws服务上下文
/// </summary>
public class WsContext
{
    private readonly ConcurrentDictionary<Guid, EventContext> _subscribedEvents = new();

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

    public Guid SubscribeEvent(EventType type, IEventFilter? filter)
    {
        Guid id;
        var context = new EventContext(type, filter);
        var existed = _subscribedEvents.Where(kv => context == kv.Value).FirstOrNone();
        if (existed.IsSome(out var pair)) return pair.Key;

        do
        {
            id = Guid.NewGuid();
        } while (!_subscribedEvents.TryAdd(id, context));

        return id;
    }

    public void UnsubscribeEvent(Guid id)
    {
        _subscribedEvents.TryRemove(id, out var value);
    }

    public bool IsSubscribedEvent(EventType type, IEventFilter? filter)
    {
        return _subscribedEvents.Any(kv => kv.Value.Type == type && kv.Value.Filter == filter);
    }

    public Option<Guid> GetEventID(EventType type, IEventFilter? filter)
    {
        var context = new EventContext(type, filter);
        return _subscribedEvents.FirstOrNone(kv => kv.Value == context).Map(r => r.Key);
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