using System.Diagnostics;
using Serilog;

namespace MCServerLauncher.Daemon.Minecraft.Server;

public class ServerInstance
{
    public ServerInstance(ServerConfig config)
    {
        Config = config;
    }

    public ServerConfig Config { get; }
    public Process? ServerProcess { get; private set; }
    public ServerStatus Status { get; private set; } = ServerStatus.Stopped;

    private HashSet<string> Players { get; } = new();
    private List<string> Properties { get; } = new();

    /// <summary>
    ///     获取mc服务器进程
    ///     在创建服务器进程时,使用互斥锁修改Daemon进程的环境变量是为了同时兼容jar启动和bat/sh脚本启动的情况
    /// </summary>
    /// <param name="config">带创建进程的配置文件</param>
    /// <param name="beforeStart">在启动前执行的操作(连接stdout等)</param>
    /// <returns></returns>
    private Process GetProcess(ServerConfig config, Action<Process> beforeStart)
    {
        var (target, args) = config.GetLaunchScript();

        var startInfo = new ProcessStartInfo(target, args)
        {
            UseShellExecute = false,
            WorkingDirectory = config.WorkingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true
        };

        var originPath = Environment.GetEnvironmentVariable("PATH");
        startInfo.EnvironmentVariables["PATH"] = BasicUtils.IsWindows()
            ? $"{Path.GetDirectoryName(config.JavaPath)};{originPath}"
            : $"{Path.GetDirectoryName(config.JavaPath)}:{originPath}";

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        beforeStart?.Invoke(process);

        // refresh server.properties
        Properties.Clear();
        Properties.AddRange(GetServerProperties());

        process.Start();

        Status = ServerStatus.Starting;

        return process;
    }

    private IEnumerable<string> GetServerProperties()
    {
        var path = Path.Combine(Config.WorkingDirectory, "server.properties");
        return File.Exists(path) ? File.ReadAllLines(path).ToList() : Enumerable.Empty<string>();
    }

    public void Start()
    {
        ServerProcess = GetProcess(Config, process =>
        {
            process.OutputDataReceived += (sender, args) =>
            {
                var msg = args.Data;

                if (msg == null) return;

                if (msg.Contains("Done"))
                {
                    Status = ServerStatus.Running;
                }
                else if (msg.Contains("Stopping the server"))
                {
                    Status = ServerStatus.Stopping;
                }
                else if (msg.Contains("Minecraft has crashed"))
                {
                    Status = ServerStatus.Crashed;
                }
                else if (msg.Contains("joined the game"))
                {
                    // [18:26:19] [Worker-Main-14/INFO] (MinecraftServer) Alex joined the game
                    var substring = msg[..^16];
                    var player = substring[(substring.LastIndexOf(' ') + 1) ..];
                    Players.Add(player);
                }
                else if (msg.Contains("left the game"))
                {
                    // [18:27:03] [Server thread/INFO] (MinecraftServer) Ares_Connor left the game
                    var substring = msg[..^14];
                    var player = substring[(substring.LastIndexOf(' ') + 1) ..];
                    Players.Remove(player);
                }

                Log.Information($"[Server({Config.Name})] {args.Data}");
            };

            process.Exited += (_, _) => Status = ServerStatus.Stopped;
        });
        ServerProcess.BeginOutputReadLine();
    }

    public InstanceStatus GetStatus()
    {
        return new InstanceStatus(
            Status,
            Config,
            Players.ToList(),
            Properties
        );
    }
}