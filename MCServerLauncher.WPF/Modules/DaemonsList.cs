using Newtonsoft.Json;
using Serilog;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MCServerLauncher.WPF.Modules;

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
}