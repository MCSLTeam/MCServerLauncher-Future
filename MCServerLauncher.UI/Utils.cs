using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows;
using Newtonsoft.Json;


namespace MCServerLauncher.UI.Helpers
{
    public class Utils
    {
        public Settings AppSettings { get; set; }

        public static void InitDataDirectory()
        {
            var dataFolders = new List<string>
            {
                "Data",
                Path.Combine("Data", "Configuration"),
                Path.Combine("Data", "Configuration", "MCSL")
            };

            foreach (string dataFolder in dataFolders)
            {
                if (!Directory.Exists(dataFolder))
                {
                    Directory.CreateDirectory(dataFolder);
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
        public void InitApp()
        {
            InitDataDirectory();
            InitSettings();
        }
        public HttpContent SendGetRequest(string url)
        {
            HttpClient client = new();
            client.DefaultRequestHeaders.Add("User-Agent", "MCServerLauncher");
            HttpResponseMessage response = client.GetAsync(url).Result;
            return response.Content;
        }
        public HttpContent SendPostRequest(string url, string data)
        {
            HttpClient client = new();
            client.DefaultRequestHeaders.Add("User-Agent", "MCServerLauncher");
            HttpResponseMessage response = client.PostAsync(url, new StringContent(data)).Result;
            return response.Content;
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