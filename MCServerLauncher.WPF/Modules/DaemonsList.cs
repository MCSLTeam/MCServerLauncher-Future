﻿using Newtonsoft.Json;
using Serilog;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MCServerLauncher.WPF.Modules
{
    public class DaemonsListManager
    {
        private static readonly ConcurrentQueue<KeyValuePair<string, string>> Queue = new();
        private static readonly object QueueLock = new();
        public static List<DaemonConfigModel>? Get { get; set; }

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
        public static void AddDaemon(DaemonConfigModel config)
        {
            lock (QueueLock)
            {
                Get?.Add(config);
                SaveDaemonList();
            }
        }
        public static void RemoveDaemon(DaemonConfigModel config)
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

        public class DaemonConfigModel
        {
            public bool IsSecure { get; set; }
            public string? EndPoint { get; set; }
            public int Port { get; set; }
            public string? Token { get; set; }
            public string? FriendlyName { get; set; }
        }

    }
}