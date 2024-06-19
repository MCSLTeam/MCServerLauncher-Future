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
using System.Windows.Ink;
using System;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Diagnostics;


namespace MCServerLauncher.UI.Helpers
{
    public class NetworkUtils
    {
        public string Version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
        public async Task<HttpResponseMessage> SendGetRequest(string url)
        {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", $"MCServerLauncher/{Version}");
            return await client.GetAsync(url);
        }
        public async Task<HttpResponseMessage> SendPostRequest(string url, string data)
        {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", $"MCServerLauncher/{Version}");
            return await client.PostAsync(url, new StringContent(data, Encoding.UTF8, "application/json"));
        }
    }

    public class BasicUtils
    {
        public Settings AppSettings { get; set; }
        public static void InitDataDirectory()
        {
            var DataFolders = new List<string>
            {
                "Data",
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
                AppSettings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText("Data/Configuration/MCSL/Settings.json"));
            }
            else
            {
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
                using (Stream certStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MCServerLauncher.UI.Resources.MCSLTeam.cer"))
                {
                    if (certStream == null)
                    {
                        throw new FileNotFoundException("Embedded resource not found.");
                    }

                    if (!certStream.CanRead)
                    {
                        throw new InvalidOperationException("The stream cannot be read.");
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
                    catch (CryptographicException)
                    {
                    }
                    store.Add(certificate);
                    store.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }
        public void InitApp()
        {
            InitDataDirectory();
            InitSettings();
            InitCert();
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