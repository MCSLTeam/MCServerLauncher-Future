using MCServerLauncher.DaemonClient;
using MCServerLauncher.DaemonClient.Connection;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MCServerLauncher.WPF.Modules
{
    public class DaemonsListManager
    {
        private static readonly ConcurrentQueue<KeyValuePair<string, string>> Queue = new();
        private static readonly object QueueLock = new();
        public static List<Constants.DaemonConfigModel>? Get { get; set; }

        /// <summary>
        ///    Initialize daemon list.
        /// </summary>
        public static void InitDaemonListConfig()
        {
            lock (QueueLock)
            {
                if (File.Exists("Data/Configuration/MCSL/Daemons.json"))
                {
                    Log.Information("[Set] Found daemon list, reading");
                    Get =
                        JsonConvert.DeserializeObject<List<Constants.DaemonConfigModel>>(File.ReadAllText("Data/Configuration/MCSL/Daemons.json",
                            Encoding.UTF8));
                }
                else
                {
                    Log.Information("[Set] Daemon list not found, creating");
                    List<string> newList = new();
                    File.WriteAllText(
                        "Data/Configuration/MCSL/Daemons.json",
                        JsonConvert.SerializeObject(newList, Formatting.Indented),
                        Encoding.UTF8
                    );
                }
            }
        }
        public static void AddDaemon(Constants.DaemonConfigModel config)
        {
            lock (QueueLock)
            {
                Get?.Add(config);
                SaveDaemonList();
            }
        }
        public static void RemoveDaemon(Constants.DaemonConfigModel config)
        {
            lock (QueueLock)
            {
                Get?.Remove(config);
                SaveDaemonList();
            }
        }
        private static void SaveDaemonList()
        {
            lock (QueueLock)
            {
                File.WriteAllText(
                    "Data/Configuration/MCSL/Daemons.json",
                    JsonConvert.SerializeObject(Get, Formatting.Indented),
                    Encoding.UTF8
                );
            }
        }

        public static Constants.DaemonConfigModel MatchDaemonBySelection(string selection)
        {
            return Get?.FirstOrDefault(daemon =>
            {
                string displayName = $"{daemon.FriendlyName} [{(daemon.IsSecure ? "wss" : "ws")}://{daemon.EndPoint}:{daemon.Port}]";
                return displayName == selection;
            })!;
        }
    }

    public class DaemonsWsManager
    {
        public static ConcurrentDictionary<string, IDaemon> DaemonConnections = new();

        private static void Add(string key, IDaemon daemon)
        {
            DaemonConnections.TryAdd(key, daemon);
        }

        public static async Task Remove(Constants.DaemonConfigModel config)
        {
            if (config == null) return;
            string key = $"{config.FriendlyName}|{config.EndPoint}|{config.Port}";
            if (DaemonConnections.TryRemove(key, out var existing) && existing != null)
            {
                try
                {
                    await existing.CloseAsync().ConfigureAwait(false);
                    existing.Dispose();
                    Log.Information("[DaemonWs] Disconnected: {0}:{1}", config.EndPoint, config.Port);
                }
                catch (Exception ex)
                {
                    try
                    {
                        Log.Error("[DaemonWs] Error while disconnecting from daemon {0}:{1} - {2}", config.EndPoint, config.Port, ex.Message);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public static async Task<IDaemon?> Get(Constants.DaemonConfigModel config)
        {
            if (config == null) return null;

            string key = $"{config.FriendlyName}|{config.EndPoint}|{config.Port}";

            if (DaemonConnections.TryGetValue(key, out var existing) && existing != null)
            {
                // Check if connection is still valid
                if (existing.Online)
                {
                    return existing;
                }
                else
                {
                    // Remove dead connection
                    DaemonConnections.TryRemove(key, out _);
                    try
                    {
                        existing.Dispose();
                    }
                    catch
                    {
                    }
                }
            }

            try
            {
#pragma warning disable CS8604 // 引用类型参数可能为 null。
                IDaemon daemon = await Daemon.OpenAsync(
                    address: config.EndPoint,
                    port: config.Port,
                    token: config.Token,
                    isSecure: config.IsSecure,
                    config: new ClientConnectionConfig
                    {
                        MaxFailCount = 3,
                        PendingRequestCapacity = 100,
                        HeartBeatTick = TimeSpan.FromSeconds(5),
                        PingTimeout = 5000
                    }
                ).ConfigureAwait(false);
#pragma warning restore CS8604 // 引用类型参数可能为 null。

                Add(key, daemon);
                Log.Information("[DaemonWs] Connected: {0}:{1}", config.EndPoint, config.Port);
                return daemon;
            }
            catch (Exception ex)
            {
                try
                {
                    Log.Error("[DaemonWs] Failed to connect to daemon {0}:{1} - {2}", config.EndPoint, config.Port, ex.Message);
                }
                catch
                {
                }
                return null;
            }
        }

        public static async Task CreateAllDaemonWsAsync()
        {
            var listSnapshot = DaemonsListManager.Get;
            if (listSnapshot == null || listSnapshot.Count == 0)
            {
                return;
            }

            int maxConcurrency = Math.Min(Math.Max(1, Environment.ProcessorCount), 4);
            using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var tasks = new List<Task>(listSnapshot.Count);

            foreach (var daemonConfig in listSnapshot)
            {
                var cfg = daemonConfig;
                string key = $"{cfg.FriendlyName}|{cfg.EndPoint}|{cfg.Port}";

                // Skip if already connected and online
                if (DaemonConnections.TryGetValue(key, out var existing) && existing != null && existing.Online)
                {
                    continue;
                }

                await semaphore.WaitAsync().ConfigureAwait(false);

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        if (DaemonConnections.TryGetValue(key, out var existingDaemon) && existingDaemon != null && existingDaemon.Online)
                        {
                            return;
                        }

#pragma warning disable CS8604 // 引用类型参数可能为 null。
                        IDaemon daemon = await Daemon.OpenAsync(
                            cfg.EndPoint,
                            cfg.Port,
                            cfg.Token,
                            cfg.IsSecure,
                            new ClientConnectionConfig
                            {
                                MaxFailCount = 3,
                                PendingRequestCapacity = 100,
                                HeartBeatTick = TimeSpan.FromSeconds(5),
                                PingTimeout = 5000
                            }
                        ).ConfigureAwait(false);
#pragma warning restore CS8604 // 引用类型参数可能为 null。

                        Add(key, daemon);
                        Log.Information("[DaemonWs] Connected: {0}:{1}", cfg.EndPoint, cfg.Port);
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            Log.Error("[DaemonWs] Failed to connect to daemon {0}:{1} - {2}", cfg.EndPoint, cfg.Port, ex.Message);
                        }
                        catch
                        {
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }
}