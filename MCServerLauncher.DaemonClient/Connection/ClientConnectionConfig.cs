using System;

namespace MCServerLauncher.DaemonClient.Connection;

public class ClientConnectionConfig
{
    public bool HeartBeat { get; set; } = true;
    public TimeSpan HeartBeatTick { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     PingTimeout宜小于PingInterval
    /// </summary>
    public int PingTimeout { get; set; } = -1;

    public int MaxFailCount { get; set; } = 3;
    public int PendingRequestCapacity { get; set; } = 100;
}