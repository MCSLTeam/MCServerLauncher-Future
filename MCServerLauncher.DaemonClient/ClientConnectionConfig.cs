﻿using System;

namespace MCServerLauncher.DaemonClient;

public class ClientConnectionConfig
{
    public TimeSpan PingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     PingTimeout宜小于PingInterval
    /// </summary>
    public int PingTimeout { get; set; } = 3000;

    public int MaxPingPacketLost { get; set; } = 3;
    public int PendingRequestCapacity { get; set; } = 100;
}