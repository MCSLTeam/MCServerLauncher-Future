﻿using Newtonsoft.Json;
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
        public static Settings AppSettings { get; set; }


        /// <summary>
        ///    Initialize program settings.
        /// </summary>
        public void InitSettings()
        {
            if (File.Exists("Data/Configuration/MCSL/Settings.json"))
            {
                Log.Information("[Set] Found profile, reading");
                AppSettings =
                    JsonConvert.DeserializeObject<Settings>(File.ReadAllText("Data/Configuration/MCSL/Settings.json",
                        Encoding.UTF8));
            }
            else
            {
                Log.Information("[Set] Profile not found, creating");
                AppSettings = new Settings
                {
                    InstanceCreation = new InstanceCreationSettings
                    {
                        MinecraftJavaAutoAcceptEula = false,
                        MinecraftJavaAutoSwitchOnlineMode = false,
                        MinecraftBedrockAutoSwitchOnlineMode = false,
                        UseMirrorForMinecraftForgeInstall = true,
                        UseMirrorForMinecraftNeoForgeInstall = true,
                        UseMirrorForMinecraftFabricInstall = true,
                        UseMirrorForMinecraftQuiltInstall = true
                    },
                    Download = new ResDownloadSettings
                    {
                        DownloadSource = "FastMirror",
                        ThreadCnt = 16,
                        ActionWhenDownloadError = "stop"
                    },
                    Instance = new InstanceSettings
                    {
                        ActionWhenDeleteConfirm = "name",
                        FollowStart = new List<string>()
                    },
                    App = new AppSettings
                    {
                        Theme = "auto",
                        Language = "zh-Hans",
                        FollowStartup = false,
                        AutoCheckUpdate = true,
                        IsCertImported = false,
                        IsFontInstalled = false,
                        IsFirstSetupFinished = false
                    }
                };
                File.WriteAllText(
                    "Data/Configuration/MCSL/Settings.json",
                    JsonConvert.SerializeObject(AppSettings, Formatting.Indented),
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

            object settings = settingClass switch
            {
                "InstanceCreation" => AppSettings.InstanceCreation,
                "ResDownload" => AppSettings.Download,
                "Instance" => AppSettings.Instance,
                "App" => AppSettings.App,
                _ => throw new ArgumentOutOfRangeException(nameof(settingClass), settingClass, "Invalid setting class.")
            };

            var property = settings.GetType().GetProperty(settingTarget);
            if (property == null || property.PropertyType != typeof(T))
                throw new InvalidOperationException($"Property {settingTarget} not found or type mismatch.");

            property.SetValue(settings, value);

            lock (Queue)
            {
                Queue.Enqueue(new KeyValuePair<string, string>(settingClass, settingTarget));
                if (_writeTask.IsCompleted)
                {
                    _writeTask = Task.Run(ProcessQueue);
                    Log.Information($"[Set] Saved Setting: {settingPath} = {value.ToString()}");
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
                object settingClass = setting.Key switch
                {
                    "InstanceCreation" => AppSettings.InstanceCreation,
                    "ResDownload" => AppSettings.Download,
                    "Instance" => AppSettings.Instance,
                    "App" => AppSettings.App,
                    _ => throw new ArgumentOutOfRangeException(nameof(setting.Key), setting.Key,
                        "Invalid setting class.")
                };

                var property = settingClass.GetType().GetProperty(setting.Value);
                if (property == null) continue;
                var value = property.GetValue(settingClass);
                File.WriteAllText(
                    "Data/Configuration/MCSL/Settings.json",
                    JsonConvert.SerializeObject(AppSettings, Formatting.Indented),
                    Encoding.UTF8
                );
            }
        }
    }
}
public class InstanceCreationSettings
{
    public bool MinecraftJavaAutoAcceptEula { get; set; }
    public bool MinecraftJavaAutoSwitchOnlineMode { get; set; }
    public bool MinecraftBedrockAutoSwitchOnlineMode { get; set; }
    public bool UseMirrorForMinecraftForgeInstall { get; set; }
    public bool UseMirrorForMinecraftNeoForgeInstall { get; set; }
    public bool UseMirrorForMinecraftFabricInstall { get; set; }
    public bool UseMirrorForMinecraftQuiltInstall { get; set; }
}

public class ResDownloadSettings
{
    public string DownloadSource { get; set; }
    public int ThreadCnt { get; set; }
    public string ActionWhenDownloadError { get; set; }
}

public class InstanceSettings
{
    public string ActionWhenDeleteConfirm { get; set; }
    public List<string> FollowStart { get; set; }
}

public class AppSettings
{
    public string Theme { get; set; }
    public string Language { get; set; }
    public bool FollowStartup { get; set; }
    public bool AutoCheckUpdate { get; set; }
    public bool IsCertImported { get; set; }
    public bool IsFontInstalled { get; set; }
    public bool IsFirstSetupFinished { get; set; }
}

public class Settings
{
    public InstanceCreationSettings InstanceCreation { get; set; }
    public ResDownloadSettings Download { get; set; }
    public InstanceSettings Instance { get; set; }
    public AppSettings App { get; set; }
}