namespace MCServerLauncher.Daemon.Utils;

public interface ITickingCallback
{
    private static readonly List<ITickingCallback> TickingCallbacks = new List<ITickingCallback>();

    private static readonly Thread TickingThread = new Thread(() =>
    {
        while (true)
        {
            var startTime = DateTime.Now;
            foreach (var tickingCallback in TickingCallbacks)
            {
                tickingCallback.Tick();
            }

            // 0.05 s/tick (20 tick/s)
            var timeSpan = TimeSpan.FromMilliseconds(50).Subtract(DateTime.Now - startTime);
            if (timeSpan > TimeSpan.Zero)
                Thread.Sleep(timeSpan);
        }
    });

    public static void AddTickingCallback(ITickingCallback tickingCallback)
    {
        if (!TickingThread.IsAlive)
            TickingThread.Start();
        TickingCallbacks.Add(tickingCallback);
    }

    public static void RemoveTickingCallback(ITickingCallback tickingCallback)
    {
        TickingCallbacks.Remove(tickingCallback);
    }

    void Tick();
}