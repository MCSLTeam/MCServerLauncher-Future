using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
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
using static MCServerLauncher.WPF.Main.App;

namespace MCServerLauncher.WPF.Main.Helpers
{
    public class CreateInstanceUtils
    {
        public static string SelectFile(string title, string filter)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = title,
                Filter = filter
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                return dialog.FileName;
            }
            else
            {
                return null;
            }
        }
        public static string SelectFolder(string title)
        {
            FolderBrowserDialog folderBrowserDialog = new();
            folderBrowserDialog.RootFolder = Environment.SpecialFolder.MyComputer;
            folderBrowserDialog.ShowDialog();
            return folderBrowserDialog.ShowDialog() == DialogResult.OK ? folderBrowserDialog.SelectedPath : null;
        }
    }
    public class ResDownloadUtils
    {
        public static List<string> SequenceMinecraftVersion(List<string> originalList)
        {
            return originalList.OrderByDescending(VersionComparator).ToList();

        }

        private static readonly Func<string, (int, int, int, int)> VersionToTuple = version =>
        {
            if (!version.Contains(".") && !version.Contains("-"))
            {
                switch (version)
                {
                    case "horn":
                        return (1, 19, 2, 0);
                    case "GreatHorn":
                        return (1, 19, 3, 0);
                    case "Executions":
                        return (1, 19, 4, 0);
                    case "Trials":
                        return (1, 20, 1, 0);
                    case "Net":
                        return (1, 20, 2, 0);
                    case "Whisper":
                        return (1, 20, 4, 0);
                    case "general":
                        return (0, 0, 0, 0);
                    case "snapshot":
                        return (0, 0, 0, 0);
                    case "release":
                        return (0, 0, 0, 0);
                    default:
                        return (0, 0, 0, 0);
                }
            }
            if (version.Contains("-"))
            {
                version = version.ToLower().Replace("-", ".").Replace("rc", "").Replace("pre", "").Replace("snapshot", "0");
            }
            Console.WriteLine(version);
            string[] parts = version.Split('.');
            Console.WriteLine(string.Join(", ", parts));
            if (parts.Length == 2)
            {
                return (int.Parse(parts[0]), int.Parse(parts[1]), 0, 0);
            }
            else if (parts.Length == 3)
            {
                return (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), 0);
            }
            else
            {
                return (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
            };
        };
        private static readonly Func<string, string> VersionComparator = version =>
        {
            var versionTuple = VersionToTuple(version);
            return $"{versionTuple.Item1:D3}.{versionTuple.Item2:D3}.{versionTuple.Item3:D3}";
        };
    }
    public class NetworkUtils
    {
        private static readonly HttpClient Client = new();
        public static async Task<HttpResponseMessage> SendGetRequest(string url)
        {
            Log.Information($"[Net] Try to get url \"{url}\"");
            Client.DefaultRequestHeaders.Add(name:"User-Agent", $"MCServerLauncher/{AppVersion}");
            return await Client.GetAsync(url);
        }
        public static async Task<HttpResponseMessage> SendPostRequest(string url, string data)
        {
            Log.Information($"[Net] Try to post url \"{url}\" with data {data}");
            Client.DefaultRequestHeaders.Add(name: "User-Agent", $"MCServerLauncher/{AppVersion}");
            return await Client.PostAsync(url, new StringContent(data, encoding: Encoding.UTF8, mediaType: "application/json"));
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
        public Settings AppSettings { get; set; }
        public static void InitDataDirectory()
        {
            var dataFolders = new List<string>
            {
                "Data",
                Path.Combine("Data", "Logs"),
                Path.Combine("Data", "Logs", "WPF"),
                Path.Combine("Data", "Configuration"),
                Path.Combine("Data", "Configuration", "MCSL")
            };

            foreach (string dataFolder in dataFolders.Where(dataFolder => !Directory.Exists(dataFolder)))
            {
                Directory.CreateDirectory(dataFolder);
            }
        }
        public void InitSettings()
        {
            if (File.Exists(path: "Data/Configuration/MCSL/Settings.json"))
            {
                Log.Information("[Set] Found profile, reading");
                AppSettings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(path: "Data/Configuration/MCSL/Settings.json"));
            }
            else
            {
                Log.Information("[Set] Profile not found, creating");
                AppSettings = new Settings
                {
                    MinecraftJava = new MinecraftJavaSettings
                    {
                        AutoAcceptEula = false,
                        AutoSwitchOnlineMode = false,
                        QuickMenu = true
                    },
                    Download = new DownloadSettings
                    {
                        Thread = 8,
                        SpeedLimit = 0,
                        DownloadSource = "FastMirror"
                    },
                    InstanceCreation = new InstanceCreationSettings
                    {
                        KeepDataWhenBack = false,
                        ShowCreationConfirm = true
                    },
                    Instance = new InstanceSettings
                    {
                        Input = "UTF-8",
                        Output = "UTF-8",
                        CleanConsoleWhenStopped = true,
                        FollowStart = new List<string> ()
                    },
                    App = new AppSettings
                    {
                        Theme = "auto",
                        FollowStartup = false,
                        AutoCheckUpdate = true
                    }
                };
                File.WriteAllText(
                    path: "Data/Configuration/MCSL/Settings.json",
                    contents: JsonConvert.SerializeObject(AppSettings, Formatting.Indented)
                );
            }
        }
        public void InitCert()
        {
            try
            {
                Log.Information("[Cer] Importing certificate");
                using Stream certStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MCServerLauncher.WPF.Resources.MCSLTeam.cer");
                if (certStream == null)
                {
                    throw new FileNotFoundException("Embedded resource not found");
                }

                if (!certStream.CanRead)
                {
                    throw new InvalidOperationException("The stream cannot be read");
                }
                byte[] buffer = new byte[certStream.Length];
                certStream.Read(buffer, offset: 0, count: buffer.Length);
                X509Certificate2 certificate = new X509Certificate2(buffer);
                X509Store store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadWrite);
                try
                {
                    store.Remove(certificate);
                }
                catch (CryptographicException) { }
                store.Add(certificate);
                store.Close();
                Log.Information("[Cer] Certificate successfully imported");
            }
            catch (Exception ex)
            {
                Log.Error($"[Cer] Failed to import certificate. Reason: {ex.Message}");
            }
        }
        public static void InitLogger()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Async(configure: a => a.File(path: "Data/Logs/WPF/WPFLog-.txt", rollingInterval: RollingInterval.Day, outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"))
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

public class MinecraftJavaSettings
{
    public bool AutoAcceptEula { get; set; }
    public bool AutoSwitchOnlineMode { get; set; }
    public bool QuickMenu { get; set; }
}

public class DownloadSettings
{
    public int Thread { get; set; }
    public int SpeedLimit { get; set; }
    public string DownloadSource { get; set; }
}

public class InstanceCreationSettings
{
    public bool KeepDataWhenBack { get; set; }
    public bool ShowCreationConfirm { get; set; }
}

public class InstanceSettings
{
    public string Input { get; set; }
    public string Output { get; set; }
    public bool CleanConsoleWhenStopped { get; set; }
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
    public MinecraftJavaSettings MinecraftJava { get; set; }
    public DownloadSettings Download { get; set; }
    public InstanceCreationSettings InstanceCreation { get; set; }
    public InstanceSettings Instance { get; set; }
    public AppSettings App { get; set; }
}
namespace MCServerLauncher.WPF.Main.Helpers
{
    internal static class VisualTreeExtensions
    {
        public static T TryFindParent<T>(this DependencyObject child) where T : DependencyObject
        {
            DependencyObject parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T parentType)
                {
                    return parentType;
                }
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }
}