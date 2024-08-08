using System.Reflection;
using Serilog;

namespace MCServerLauncher.Daemon;

public class BasicUtils
{
    public Settings AppSettings { get; set; }
    public static Version AppVersion => Assembly.GetExecutingAssembly().GetName().Version;

    private static void InitDataDirectory()
    {
        var dataFolders = new List<string>
        {
            "Data",
            Path.Combine("Data", "Logs"),
            Path.Combine("Data", "Logs", "Daemon"),
            Path.Combine("Data", "InstanceData"),
            Path.Combine("Data", "Configuration"),
            Path.Combine("Data", "Configuration", "Instance")
        };

        foreach (var dataFolder in dataFolders.Where(dataFolder => !Directory.Exists(dataFolder)))
            Directory.CreateDirectory(dataFolder!);
    }

    private static void InitLogger()
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

// TODO 可将链表优化成树
/// <summary>
///  -2^63 ~ 2^63 - 1内的整数区间(只支持减去某一子区间)，用于上传文件数据的记录
/// </summary>
public class LongRemain
{
    private LongRemainNode _head;

    /// <summary>
    ///     整数区间: [Begin, End)
    /// </summary>
    /// <param name="begin"></param>
    /// <param name="end"></param>
    public LongRemain(long begin, long end)
    {
        Begin = begin;
        End = end;
        _head = new LongRemainNode(begin, end);
    }

    public long Begin { get; private set; }
    public long End { get; private set; }
    
    /// <summary>
    ///  减去[from, to)
    /// </summary>
    /// <param name="from">闭区间</param>
    /// <param name="to">开区间</param>
    /// <returns>自身,用于链式操作</returns>
    public LongRemain Reduce(long from, long to)
    {
        LongRemainNode lastNode = null;
        var node = _head;
        while (node != null)
        {
            if (from <= node.Begin && to >= node.End)
            {
                if (lastNode == null)
                    _head = _head.Next;
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
    
    /// <summary>
    ///  获取剩余区间
    /// </summary>
    /// <returns></returns>
    public IEnumerable<(long Begin, long End)> GetRemains()
    {
        var node = _head;
        while (node != null)
        {
            yield return (node.Begin, node.End);
            node = node.Next;
        }
    }
    
    
    /// <summary>
    ///  获取剩余区间的总长度
    /// </summary>
    /// <returns></returns>
    public long GetRemain()
    {
        long remain = 0;
        foreach (var (begin, end) in GetRemains()) remain += end - begin;
        return remain;
    }
    
    /// <summary>
    ///  判断是否完成
    /// </summary>
    /// <returns></returns>
    public bool Done()
    {
        return _head == null;
    }
    
    
    /// <summary>
    ///  节点
    /// </summary>
    private class LongRemainNode
    {
        public LongRemainNode(long begin, long end)
        {
            Begin = begin;
            End = end;
        }

        public long Begin { get; set; }
        public long End { get; set; }
        public LongRemainNode Next { get; set; }
    }
}