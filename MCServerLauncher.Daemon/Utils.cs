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
                .WriteTo.Async(a => a.File("Data/Logs/Daemon/DaemonLog-.txt", rollingInterval: RollingInterval.Day,
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"))
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

    // TODO 可将链表优化成树
    public class LongRemain
    {
        public long Begin { get; private set; }
        public long End { get; private set; }

        private LongRemainNode _head;

        /// <summary>
        /// [Begin, End)
        /// </summary>
        /// <param name="begin"></param>
        /// <param name="end"></param>
        public LongRemain(long begin, long end)
        {
            Begin = begin;
            End = end;
            _head = new LongRemainNode(begin, end);
        }

        public LongRemain Reduce(long from, long to)
        {
            LongRemainNode lastNode = null;
            var node = _head;
            while (node != null)
            {
                if (from <= node.Begin && to >= node.End)
                {
                    if (lastNode == null)
                    {
                        _head = _head.Next;
                    }
                    else lastNode.Next = node.Next;

                    break;
                }

                if (from > node.Begin && to < node.End)
                {
                    var next = new LongRemainNode(to, node.End);
                    node.End = from;
                    next.Next = node.Next;
                    node.Next = next;
                    break;
                }

                if (node.Begin < from && from < node.End)
                {
                    // to >= node.Begin
                    node.End = from;
                    break;
                }


                if (node.Begin < to && to < node.End)
                {
                    // from <= node.End
                    node.Begin = to;
                    break;
                }

                if (to < node.Begin) break; // break

                lastNode = node;
                node = node.Next;
            }

            return this;
        }

        public IEnumerable<(long Begin, long End)> GetRemains()
        {
            var node = _head;
            while (node != null)
            {
                yield return (node.Begin, node.End);
                node = node.Next;
            }
        }

        public long GetRemain()
        {
            long remain = 0;
            foreach (var (begin, end) in GetRemains())
            {
                remain += end - begin;
            }
            return remain;
        }

        public bool Done()
        {
            return _head == null;
        }

        private class LongRemainNode
        {
            public long Begin { get; set; }
            public long End { get; set; }
            public LongRemainNode Next { get; set; }

            public LongRemainNode(long begin, long end)
            {
                Begin = begin;
                End = end;
            }
        }

        public static void Test()
        {
            var remain = new LongRemain(0, 100);
            remain = remain.Reduce(0, 20).Reduce(50, 60).Reduce(80, 100).Reduce(70, 75);
            Console.WriteLine(remain.Done());
            foreach (var (begin, end) in remain.GetRemains())
            {
                Console.WriteLine($"{begin},{end}");
            }

            Console.WriteLine(remain.GetRemain());
        }
    }
}