using MCServerLauncher.Common.Concurrent;
using MCServerLauncher.Common.Helpers;
using Serilog;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Http.WebSockets;
using TouchSocket.Sockets;

namespace MCServerLauncher.Daemon.Remote;

public class WsExpirationPlugin : PluginBase, IWsPlugin, IWebSocketHandshakedPlugin, IWebSocketClosingPlugin
{
    private const int CF_MAX_DELAY_SECOND = int.MaxValue / 1000;

    private readonly DaryHeap<HashSet<string>, long> _expiredContexts = new();

    private readonly CancellationTokenSource _loopCts = new();
    private readonly SemaphoreSlim _sync = new(0);
    private Task? _loopTask;

    public WsExpirationPlugin(IHttpService httpService, WsContextContainer container)
    {
        HttpService = httpService;
        Container = container;
    }

    public async Task OnWebSocketClosing(IWebSocket webSocket, ClosingEventArgs e)
    {
        RemoveWatchingWebsocket(webSocket);
        await e.InvokeNext();
    }

    public async Task OnWebSocketHandshaked(IWebSocket webSocket, HttpContextEventArgs e)
    {
        AddWatchingWebsocket(webSocket);
        await e.InvokeNext();
    }

    public IHttpService HttpService { get; init; }
    public WsContextContainer Container { get; init; }

    private async Task CheckExpireLoop()
    {
        var delaySeconds = long.MaxValue;
        while (true)
            try
            {
                await _sync.WaitAsync(
                    TimeSpan.FromSeconds(Math.Min(delaySeconds, CF_MAX_DELAY_SECOND)),
                    _loopCts.Token
                ); // 等待最近的expire到期 或者 接收信号导致提前结束

                var (ids, expiredTo) = await _expiredContexts.DequeueAsync(_loopCts.Token);
                delaySeconds = expiredTo - DateTime.UtcNow.ToUnixTimeSeconds();
                if (delaySeconds <= 0 && ids.Count > 0)
                {
                    Log.Debug(
                        "[WsExpirePlugin / CheckExpireLoop] Expire and start closing websocket connections: {Connections}",
                        ids
                    );
                    var tasks = new List<Task>();
                    foreach (var id in ids)
                    {
                        var ws = this.GetWebSocket(id);
                        if (ws != null) tasks.Add(ws.SafeCloseAsync("connection expired"));
                    }

                    await Task.WhenAll(tasks);
                }
                else
                {
                    _expiredContexts.Enqueue(ids, expiredTo);
                }
                // 插回优先队列
            }
            catch (OperationCanceledException)
            {
                break; // 如果_loopCts被取消，则跳出循环, 代表插件将要被卸载
            }
    }

    private void RemoveWatchingWebsocket(IWebSocket webSocket)
    {
        var context = this.GetWsContext(webSocket);
        var expiredTimeStamp = context.ExpiredTo.ToUnixTimeSeconds();

        // 临界区
        // 如果过期时间戳不存在，则取消当前任务，并创建一个新的任务，并将当前任务添加到过期时间戳对应的集合中
        var set = _expiredContexts.GetElementOrDefault(expiredTimeStamp);
        if (set is null) return; // 可能是过期直接踢了后触发的Closed事件，我们不需要处理

        set.Remove(context.ClientId);
        if (set.Count == 0) _expiredContexts.TryRemove(expiredTimeStamp, out _);

        Log.Debug("[WsExpirePlugin] Remove {ClientId} from expired context", context.ClientId);

        _sync.Release(1); // 通知Loop线程重新获取堆顶值
    }

    private void AddWatchingWebsocket(IWebSocket webSocket)
    {
        var context = this.GetWsContext(webSocket);
        var expiredTimeStamp = context.ExpiredTo.ToUnixTimeSeconds();

        // 临界区
        // 如果过期时间戳不存在，则取消当前任务，并创建一个新的任务，并将当前任务添加到过期时间戳对应的集合中
        var set = _expiredContexts.GetElementOrDefault(expiredTimeStamp);
        if (set is null)
        {
            _expiredContexts.Enqueue(new HashSet<string> { context.ClientId }, expiredTimeStamp);
        }
        else
        {
            set.Add(context.ClientId);
            return; // 没对heap的priority队列进行修改, 所以不必通知
        }

        _sync.Release(1); // 通知Loop线程重新获取堆顶值
    }

    protected override void Loaded(IPluginManager pluginManager)
    {
        _loopTask = Task.Factory.StartNew(CheckExpireLoop, TaskCreationOptions.LongRunning);
        base.Loaded(pluginManager);
    }

    protected override void Unloaded(IPluginManager pluginManager)
    {
        _loopCts.Cancel();
        _loopTask?.Wait();
        base.Unloaded(pluginManager);
    }
}