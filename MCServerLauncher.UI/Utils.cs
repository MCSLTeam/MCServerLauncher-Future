using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Net.Http;
using System.Windows.Media;
using System.Windows;
using System.Reflection;
using Newtonsoft.Json;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using System;
using System.Security.Cryptography;
using System.Diagnostics;
using Serilog;
using Microsoft.Win32;

namespace MCServerLauncher.UI.Helpers
{
    public class NetworkUtils
    {
        public static string Version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
        private HttpClient client = new();
        public async Task<HttpResponseMessage> SendGetRequest(string Url)
        {
            Log.Information($"[Net] Try to get url \"{Url}\"");
            client.DefaultRequestHeaders.Add("User-Agent", $"MCServerLauncher/{Version}");
            return await client.GetAsync(Url);
        }
        public async Task<HttpResponseMessage> SendPostRequest(string Url, string Data)
        {
            Log.Information($"[Net] Try to post url \"{Url}\" with data {Data}");
            client.DefaultRequestHeaders.Add("User-Agent", $"MCServerLauncher/{Version}");
            return await client.PostAsync(Url, new StringContent(Data, Encoding.UTF8, "application/json"));
        }
        public static void OpenUrl(string Url)
        {
            try
            { 
                Process.Start(Url);
                Log.Information("[Net] Try to open url \"{Url}\"");
            }
            catch (Exception ex)
            {
                Log.Error($"[Net] Failed to open url \"{Url}\". Reason: {ex.Message}");
            }
        }
    }
    public class BasicUtils
    {
        public static string Version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
        public Settings AppSettings { get; set; }
        public static void InitDataDirectory()
        {
            var DataFolders = new List<string>
            {
                "Data",
                Path.Combine("Data", "Logs"),
                Path.Combine("Data", "Logs", "UI"),
                Path.Combine("Data", "Configuration"),
                Path.Combine("Data", "Configuration", "MCSL")
            };

            foreach (string DataFolder in DataFolders)
            {
                if (!Directory.Exists(DataFolder))
                {
                    Directory.CreateDirectory(DataFolder);
                }
            }
        }
        public void InitSettings()
        {
            if (File.Exists("Data/Configuration/MCSL/Settings.json"))
            {
                Log.Information("[Set] Found profile, reading");
                AppSettings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText("Data/Configuration/MCSL/Settings.json"));
            }
            else
            {
                Log.Information("[Set] Profile not found, creating");
                AppSettings = new Settings
                {
                    MinecraftJava = new MinecraftJavaSettings
                    {
                        AutoAcceptEULA = false,
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
                        FollowStart = new List<string> { }
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
                    JsonConvert.SerializeObject(AppSettings, Formatting.Indented)
                );
            }
        }
        public void InitCert()
        {
            try
            {
                Log.Information("[Cer] Importing certificate");
                using (Stream certStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MCServerLauncher.UI.Resources.MCSLTeam.cer"))
                {
                    if (certStream == null)
                    {
                        throw new FileNotFoundException("Embedded resource not found");
                    }

                    if (!certStream.CanRead)
                    {
                        throw new InvalidOperationException("The stream cannot be read");
                    }
                    byte[] buffer = new byte[certStream.Length];
                    certStream.Read(buffer, 0, buffer.Length);
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
                .WriteTo.Async(a => a.File("Data/Logs/UI/UILog-.txt", rollingInterval: RollingInterval.Day, outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"))
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }
        public void InitApp()
        {
            InitLogger();
            Log.Information($"[Exe] MCServerLauncher Future v{Version}");
            Log.Information($"[Env] WorkingDir: {Environment.CurrentDirectory}");
            //Log.Information("Test Infomation");
            //Log.Warning("Test Warning");
            //Log.Error("Test Error");
            //Log.Fatal("Test Fatal");
            //Log.Debug("Test Debug");
            InitDataDirectory();
            InitSettings();
            InitCert();
        }
        public string SelectFile(string filter, string initDirectory="C:\\")
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.InitialDirectory = initDirectory;
            dialog.Filter = filter;
            dialog.FilterIndex = 2;
            dialog.RestoreDirectory = true;
            dialog.ShowDialog();
            return dialog.FileName;
        }
    }
}

public class MinecraftJavaSettings
{
    public bool AutoAcceptEULA { get; set; }
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
namespace MCServerLauncher.UI.Helpers
{
    internal static class VisualTreeExtensions
    {
        public static T TryFindParent<T>(this DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
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
