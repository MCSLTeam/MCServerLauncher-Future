using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MCServerLauncher.WPF.Modules
{
    public class SettingsManager
    {
        private static Task _writeTask = Task.CompletedTask;
        private static readonly ConcurrentQueue<KeyValuePair<string, string>> Queue = new();
        public static Settings? Get { get; set; }


        /// <summary>
        ///    Initialize program settings.
        /// </summary>
        public static void InitSettings()
        {
            if (File.Exists("Data/Configuration/MCSL/Settings.json"))
            {
                Log.Information("[Set] Found profile, reading");
                Get =
                    JsonConvert.DeserializeObject<Settings>(File.ReadAllText("Data/Configuration/MCSL/Settings.json",
                        Encoding.UTF8));
            }
            else
            {
                Log.Information("[Set] Profile not found, creating");
                Get = new Settings
                {
                    InstanceCreation = new InstanceCreationSettingsModel
                    {
                        MinecraftJavaAutoAcceptEula = false,
                        MinecraftJavaAutoSwitchOnlineMode = false,
                        MinecraftBedrockAutoSwitchOnlineMode = false,
                        UseMirrorForMinecraftForgeInstall = true,
                        UseMirrorForMinecraftNeoForgeInstall = true,
                        UseMirrorForMinecraftFabricInstall = true,
                        UseMirrorForMinecraftQuiltInstall = true
                    },
                    Download = new ResDownloadSettingsModel
                    {
                        DownloadSource = "FastMirror",
                        ThreadCnt = 16,
                        ActionWhenDownloadError = "stop"
                    },
                    Instance = new InstanceSettingsModel
                    {
                        ActionWhenDeleteConfirm = "name",
                        FollowStart = new List<string?>()
                    },
                    App = new AppSettingsModel
                    {
                        Theme = "auto",
                        Language = "zh-CN",
                        FollowStartup = false,
                        AutoCheckUpdate = true,
                        IsCertImported = false,
                        IsFontInstalled = false,
                        IsAppEulaAccepted = false,
                        IsFirstSetupFinished = false
                    }
                };
                File.WriteAllText(
                    "Data/Configuration/MCSL/Settings.json",
                    JsonConvert.SerializeObject(Get, Formatting.Indented),
                    Encoding.UTF8
                );
            }
        }

        /// <summary>
        ///    Save MCServerLauncher.WPF settings.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="settingPath">The setting to be set, in the format of App.Theme.</param>
        /// <param name="value">The value of the setting.</param>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public static void SaveSetting<T>(string settingPath, T value)
        {
            var settingParts = settingPath.Split('.');
            if (settingParts.Length != 2)
                throw new ArgumentException("Invalid setting path format. Expected format: 'Class.Property'");

            var settingClass = settingParts[0];
            var settingTarget = settingParts[1];

            object? settings = settingClass switch
            {
                "InstanceCreation" => Get?.InstanceCreation,
                "ResDownload" => Get?.Download,
                "Instance" => Get?.Instance,
                "App" => Get?.App,
                _ => throw new ArgumentOutOfRangeException(nameof(settingClass), settingClass, "Invalid setting class.")
            };

            var property = settings?.GetType().GetProperty(settingTarget);
            if (property == null || property.PropertyType != typeof(T))
                throw new InvalidOperationException($"Property {settingTarget} not found or type mismatch.");

            if (property.GetValue(settings)?.Equals(value) == true) return;
            property.SetValue(settings, value);

            lock (Queue)
            {
                Queue.Enqueue(new KeyValuePair<string, string>(settingClass, settingTarget));
                if (_writeTask.IsCompleted)
                {
                    _writeTask = Task.Run(ProcessQueue);
                    Log.Information($"[Set] Saved Setting: {settingPath} = {value?.ToString()}");
                }
            }
        }

        /// <summary>
        ///    Queue implementation for saving settings.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static void ProcessQueue()
        {
            while (Queue.TryDequeue(out var setting))
            {
                object? settingClass = setting.Key switch
                {
                    "InstanceCreation" => Get?.InstanceCreation,
                    "ResDownload" => Get?.Download,
                    "Instance" => Get?.Instance,
                    "App" => Get?.App,
                    _ => throw new ArgumentOutOfRangeException(nameof(setting.Key), setting.Key,
                        "Invalid setting class.")
                };

                var property = settingClass?.GetType().GetProperty(setting.Value);
                if (property == null) continue;
                var value = property.GetValue(settingClass);
                File.WriteAllText(
                    "Data/Configuration/MCSL/Settings.json",
                    JsonConvert.SerializeObject(Get, Formatting.Indented),
                    Encoding.UTF8
                );
            }
        }
        public class InstanceCreationSettingsModel
        {
            public bool MinecraftJavaAutoAcceptEula { get; set; }
            public bool MinecraftJavaAutoSwitchOnlineMode { get; set; }
            public bool MinecraftBedrockAutoSwitchOnlineMode { get; set; }
            public bool UseMirrorForMinecraftForgeInstall { get; set; }
            public bool UseMirrorForMinecraftNeoForgeInstall { get; set; }
            public bool UseMirrorForMinecraftFabricInstall { get; set; }
            public bool UseMirrorForMinecraftQuiltInstall { get; set; }
        }

        public class ResDownloadSettingsModel
        {
            public string? DownloadSource { get; set; }
            public int ThreadCnt { get; set; }
            public string? ActionWhenDownloadError { get; set; }
        }

        public class InstanceSettingsModel
        {
            public string? ActionWhenDeleteConfirm { get; set; }
            public List<string?>? FollowStart { get; set; }
        }

        public class AppSettingsModel
        {
            public string? Theme { get; set; }
            public string? Language { get; set; }
            public bool FollowStartup { get; set; }
            public bool AutoCheckUpdate { get; set; }
            public bool IsCertImported { get; set; }
            public bool IsFontInstalled { get; set; }
            public bool IsAppEulaAccepted { get; set; }
            public bool IsFirstSetupFinished { get; set; }
        }

        public class Settings
        {
            public InstanceCreationSettingsModel? InstanceCreation { get; set; }
            public ResDownloadSettingsModel? Download { get; set; }
            public InstanceSettingsModel? Instance { get; set; }
            public AppSettingsModel? App { get; set; }
        }
    }
}
