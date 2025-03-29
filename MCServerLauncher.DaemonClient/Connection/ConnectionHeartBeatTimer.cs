using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.ProtoType.Action;
using Serilog;

namespace MCServerLauncher.DaemonClient.Connection;

internal class HeartBeatTimerState
{
    public HeartBeatTimerState(ClientConnection conn, SynchronizationContext ctx)
    {
        Connection = conn;
        ConnectionContext = ctx;
    }

    public ClientConnection Connection { get; set; }
    public int PingPacketLost { get; set; }
    public SynchronizationContext ConnectionContext { get; set; }
}


internal class ConnectionHeartBeatTimer
{
    private readonly CancellationTokenSource _cancelTokenSource = new();
    private readonly TimeSpan _interval;
    private readonly HeartBeatTimerState _state;
    private Task? _timerLoopTask;

    public ConnectionHeartBeatTimer(ClientConnection conn, TimeSpan interval)
    {
        _state = new HeartBeatTimerState(conn, SynchronizationContext.Current!);
        _interval = interval;
    }

    public void Start()
    {
        _timerLoopTask = Task.Factory.StartNew(
            () => TimerLoop(_state, _cancelTokenSource.Token)
            , _cancelTokenSource.Token
        );
    }

    public async Task Stop()
    {
        _cancelTokenSource.Cancel();
        if (_timerLoopTask != null) await _timerLoopTask;
    }

    private async Task TimerLoop(HeartBeatTimerState state, CancellationToken ct)
    {
        var innerTasks = new HashSet<Task>();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, ct);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            var innerTask = OnTimer(state, ct);
            innerTasks.Add(innerTask);
            innerTasks.RemoveWhere(t => t.IsCompleted);
        }

        await Task.WhenAll(innerTasks);
    }

    /// <summary>
    ///     心跳定时器超时逻辑: 根据连接情况设定ClientConnection的PingLost，根据config判断是否关闭连接
    /// </summary>
    /// <param name="state">Timer状态</param>
    /// <param name="ct"></param>
    private static async Task OnTimer(HeartBeatTimerState state, CancellationToken ct)
    {
        Log.Debug("[ClientConnection] Heartbeat timer triggered.");

        if (ct.IsCancellationRequested) return;
        try
        {
            var result = await state.Connection.RequestAsync<PingResult>(
                ActionType.Ping,
                null,
                state.Connection.Config.PingTimeout,
                ct
            );

            state.PingPacketLost = 0;
            var timestamp = result.Time;
            Log.Debug($"[ClientConnection] Ping packet received, timestamp: {timestamp}");
        }
        catch (TimeoutException)
        {
            if (ct.IsCancellationRequested) return;

            state.PingPacketLost++;
            Log.Warning($"[ClientConnection] Ping packet lost, lost {state.PingPacketLost} times.");
            // 切换到ClientConnection所在线程,防止数据竞争
            state.ConnectionContext.Post(_ => state.Connection.MarkAsPingLost(), null);
        }

        if (state.PingPacketLost < state.Connection.Config.MaxPingPacketLost) return;
        Log.Error("Ping packet lost too many times, close connection.");
        // 关闭连接
        await state.Connection.CloseAsync();
    }
}