using Serilog;
namespace MCServerLauncher.Daemon.Helpers
{
    public class BasicUtils
    {
        public Settings AppSettings { get; set; }

        public static void InitDataDirectory()
        {
            var dataFolders = new List<string>
            {
                "Data",
                Path.Combine("Data", "Logs"),
                Path.Combine("Data", "Logs", "Daemon"),
                Path.Combine("Data", "InstanceData"),
                Path.Combine("Data", "Configuration"),
                Path.Combine("Data", "Configuration", "Instance"),
            };

            foreach (string dataFolder in dataFolders)
            {
                if (!Directory.Exists(dataFolder))
                {
                    Directory.CreateDirectory(dataFolder);
                }
            }
        }
        public static void InitLogger()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Async(a => a.File("Data/Logs/Daemon/DaemonLog-.txt", rollingInterval: RollingInterval.Day, outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"))
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }
        public static void InitApp()
        {
            InitLogger();
            InitDataDirectory();
        }

        public static bool IsWindows()
        {
            return Environment.OSVersion.Platform == PlatformID.Win32NT;
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

}
