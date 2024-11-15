using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Serilog;

namespace MCServerLauncher.WPF.Modules
{
    public class DaemonsListManager
    {
        private static readonly ConcurrentQueue<KeyValuePair<string, string>> Queue = new();
        private static readonly object QueueLock = new();
        public static List<DaemonConfigModel>? DaemonList { get; set; }

        /// <summary>
        ///    Initialize daemon list.
        /// </summary>
        public void InitDaemonListConfig()
        {
            lock (QueueLock)
            {
                if (File.Exists("Data/Configuration/MCSL/Daemons.json"))
                {
                    Log.Information("[Set] Found daemon list, reading");
                    DaemonList =
                        JsonConvert.DeserializeObject<List<DaemonConfigModel>>(File.ReadAllText("Data/Configuration/MCSL/Daemons.json",
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
        public void AddDaemon(DaemonConfigModel config)
        {
            lock (QueueLock)
            {
                DaemonList?.Add(config);
                SaveDaemonList();
            }
        }
        public void RemoveDaemon(DaemonConfigModel config)
        {
            lock (QueueLock)
            {
                DaemonList?.Remove(config);
                SaveDaemonList();
            }
        }
        private void SaveDaemonList()
        {
            lock (QueueLock)
            {
                File.WriteAllText(
                    "Data/Configuration/MCSL/Daemons.json",
                    JsonConvert.SerializeObject(DaemonList, Formatting.Indented),
                    Encoding.UTF8
                );
            }
        }

        public class DaemonConfigModel
        {
            public bool? IsSecure { get; set; }
            public string? WebSocketAddress { get; set; }
            public string? JWT { get; set; }
            public string? FriendlyName { get; set; }
        }

    }
}