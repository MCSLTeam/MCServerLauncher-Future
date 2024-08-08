using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using Newtonsoft.Json;
using Serilog;
using static MCServerLauncher.WPF.App;

namespace MCServerLauncher.WPF.Helpers
{
    public class CreateInstanceUtils
    {
        public static string SelectFile(string title, string filter)
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = filter
            };
            return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
        }

        public static string SelectFolder(string title)
        {
            FolderBrowserDialog folderBrowserDialog = new();
            folderBrowserDialog.RootFolder = Environment.SpecialFolder.MyComputer;
            folderBrowserDialog.ShowDialog();
            return folderBrowserDialog.ShowDialog() == DialogResult.OK ? folderBrowserDialog.SelectedPath : null;
        }
    }

    public static class ResDownloadUtils
    {
        private static readonly Func<string, (int, int, int, int)> VersionToTuple = version =>
        {
            if (!version.Contains(".") && !version.Contains("-"))
                return version switch
                {
                    "horn" => (1, 19, 2, 0),
                    "GreatHorn" => (1, 19, 3, 0),
                    "Executions" => (1, 19, 4, 0),
                    "Trials" => (1, 20, 1, 0),
                    "Net" => (1, 20, 2, 0),
                    "Whisper" => (1, 20, 4, 0),
                    "general" => (0, 0, 0, 0),
                    "snapshot" => (0, 0, 0, 0),
                    "release" => (0, 0, 0, 0),
                    _ => (0, 0, 0, 0)
                };

            if (version.Contains("-")) version = version.ToLower().Replace("-", ".").Replace("rc", "").Replace("pre", "").Replace("snapshot", "0");
            var parts = version.Split('.');
            if (parts.Length == 2)
                return (int.Parse(parts[0]), int.Parse(parts[1]), 0, 0);
            if (parts.Length == 3)
                return (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), 0);
            return (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
            ;
        };

        private static readonly Func<string, string> VersionComparator = version =>
        {
            var versionTuple = VersionToTuple(version);
            return $"{versionTuple.Item1:D3}.{versionTuple.Item2:D3}.{versionTuple.Item3:D3}";
        };

        public static List<string> SequenceMinecraftVersion(List<string> originalList)
        {
            return originalList.OrderByDescending(VersionComparator).ToList();
        }
    }

    public static class NetworkUtils
    {
        private static readonly HttpClient Client = new();

        public static async Task<HttpResponseMessage> SendGetRequest(string url)
        {
            Log.Information($"[Net] Try to get url \"{url}\"");
            Client.DefaultRequestHeaders.Add("User-Agent", $"MCServerLauncher/{AppVersion}");
            return await Client.GetAsync(url);
        }

        public static async Task<HttpResponseMessage> SendPostRequest(string url, string data)
        {
            Log.Information($"[Net] Try to post url \"{url}\" with data {data}");
            Client.DefaultRequestHeaders.Add("User-Agent", $"MCServerLauncher/{AppVersion}");
            return await Client.PostAsync(url, new StringContent(data, Encoding.UTF8, "application/json"));
        }

        public static void OpenUrl(string url)
        {
            try
            {
                Process.Start(url);
                Log.Information($"[Net] Try to open url \"{url}\"");
            }
            catch (Exception ex)
            {
                Log.Error($"[Net] Failed to open url \"{url}\". Reason: {ex.Message}");
            }
        }
    }

    public class BasicUtils
    {
        public static Settings AppSettings { get; set; }
        private static Task _writeTask = Task.CompletedTask;
        private static readonly ConcurrentQueue<KeyValuePair<string, string>> Queue = new();
        private static void InitDataDirectory()
        {
            var dataFolders = new List<string>
            {
                "Data",
                Path.Combine("Data", "Logs"),
                Path.Combine("Data", "Logs", "WPF"),
                Path.Combine("Data", "Configuration"),
                Path.Combine("Data", "Configuration", "MCSL")
            };

            foreach (var dataFolder in dataFolders.Where(dataFolder => !Directory.Exists(dataFolder)))
                Directory.CreateDirectory(dataFolder);
        }

        private void InitSettings()
        {
            if (File.Exists("Data/Configuration/MCSL/Settings.json"))
            {
                Log.Information("[Set] Found profile, reading");
                AppSettings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText("Data/Configuration/MCSL/Settings.json", Encoding.UTF8));
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
                        MinecraftForgeInstallerSource = "BMCLAPI"
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
                        FollowStartup = false,
                        AutoCheckUpdate = true
                    }
                };
                File.WriteAllText(
                    "Data/Configuration/MCSL/Settings.json",
                    JsonConvert.SerializeObject(AppSettings, Formatting.Indented),
                    Encoding.UTF8
                );
            }
        }

        public static void SaveSetting<T>(string settingPath, T value)
        {
            // example:
            // SaveSettings("Download.ThreadCnt", 32);
            var settingParts = settingPath.Split('.');
            if (settingParts.Length != 2)
            {
                throw new ArgumentException("Invalid setting object format. Expected format: 'Class.Property'");
            }

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
            {
                throw new InvalidOperationException($"Property {settingTarget} not found or type mismatch.");
            }

            property.SetValue(settings, value);

            Task.Run(() =>
            {
                lock (Queue)
                {
                    Queue.Enqueue(new KeyValuePair<string, string>(settingClass, settingTarget));
                    if (_writeTask.IsCompleted)
                    {
                        _writeTask = Task.Run(() =>
                        {
                            while (Queue.TryDequeue(out var setting))
                            {
                                object settingClass = setting.Key switch
                                {
                                    "InstanceCreation" => AppSettings.InstanceCreation,
                                    "ResDownload" => AppSettings.Download,
                                    "Instance" => AppSettings.Instance,
                                    "App" => AppSettings.App,
                                    _ => throw new ArgumentOutOfRangeException(nameof(setting.Key), setting.Key, @"Invalid setting class.")
                                };
                                PropertyInfo settingProperty = settingClass.GetType().GetProperty(setting.Value);
                                settingProperty.SetValue(settingClass, value);
                                File.WriteAllText(
                                    "Data/Configuration/MCSL/Settings.json",
                                    JsonConvert.SerializeObject(AppSettings, Formatting.Indented),
                                    Encoding.UTF8
                                );
                            }
                        });
                    }
                }
            });
        }

        private void InitCert()
        {
            try
            {
                Log.Information("[Cer] Importing certificate");
                using var certStream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("MCServerLauncher.WPF.Resources.MCSLTeam.cer");
                if (certStream == null) throw new FileNotFoundException("Embedded resource not found");
                if (!certStream.CanRead) throw new InvalidOperationException("The stream cannot be read");
                var buffer = new byte[certStream.Length];
                certStream.Read(buffer, 0, buffer.Length);
                var certificate = new X509Certificate2(buffer);
                var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadWrite);
                try
                {
                    store.Remove(certificate);
                }
                catch (CryptographicException)
                {
                }

                store.Add(certificate);
                store.Close();
                Log.Information("[Cer] Certificate successfully imported");
            }
            catch (Exception ex)
            {
                Log.Error($"[Cer] Failed to import certificate. Reason: {ex.Message}");
            }
        }

        private static void InitLogger()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Async(a => a.File("Data/Logs/WPF/WPFLog-.txt", rollingInterval: RollingInterval.Day,
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"))
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }

        public void InitApp()
        {
            InitLogger();
            Log.Information($"[Exe] MCServerLauncher Future v{AppVersion}");
            Log.Information($"[Env] WorkingDir: {Environment.CurrentDirectory}");
            //Log.Information("");
            //Log.Warning("");
            //Log.Error("");
            //Log.Fatal("");
            //Log.Debug("");
            InitDataDirectory();
            InitSettings();
            InitCert();
        }
    }
}

public class InstanceCreationSettings
{
    public bool MinecraftJavaAutoAcceptEula { get; set; }
    public bool MinecraftJavaAutoSwitchOnlineMode { get; set; }
    public bool MinecraftBedrockAutoSwitchOnlineMode { get; set; }
    public string MinecraftForgeInstallerSource { get; set; }
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
    public bool FollowStartup { get; set; }
    public bool AutoCheckUpdate { get; set; }
}

public class Settings
{
    public InstanceCreationSettings InstanceCreation { get; set; }
    public ResDownloadSettings Download { get; set; }
    public InstanceSettings Instance { get; set; }
    public AppSettings App { get; set; }
}

namespace MCServerLauncher.WPF.Helpers
{
    internal static class VisualTreeExtensions
    {
        public static T TryFindParent<T>(this DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T parentType) return parentType;
                parent = VisualTreeHelper.GetParent(parent);
            }

            return null;
        }
    }
}