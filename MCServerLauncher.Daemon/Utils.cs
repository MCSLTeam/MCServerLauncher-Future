namespace MCServerLauncher.Daemon.Helpers
{
    public class Utils
    {
        public Settings AppSettings { get; set; }

        public static void InitDataDirectory()
        {
            var dataFolders = new List<string>
            {
                "Data",
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
        public void InitApp()
        {
            InitDataDirectory();
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
