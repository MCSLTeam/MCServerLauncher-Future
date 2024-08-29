using System.Diagnostics;
using Serilog;

namespace MCServerLauncher.Daemon.Minecraft.Server;

public class ServerInstance
{
    private static readonly Mutex _mutex = new();


    public ServerInstance(ServerConfig config)
    {
        Config = config;
    }

    public ServerConfig Config { get; private set; }
    public Process ServerProcess { get; private set; }

    /// <summary>
    ///    获取mc服务器进程
    ///    在创建服务器进程时,使用互斥锁修改Daemon进程的环境变量是为了同时兼容jar启动和bat/sh脚本启动的情况
    /// </summary>
    /// <param name="config">带创建进程的配置文件</param>
    /// <param name="beforeStart">在启动前执行的操作</param>
    /// <returns></returns>
    public static Task<Process> GetProcess(ServerConfig config, Action<Process> beforeStart)
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
        var newPath = BasicUtils.IsWindows()
            ? $"{Path.GetDirectoryName(config.JavaPath)};{originPath}"
            : $"{Path.GetDirectoryName(config.JavaPath)}:{originPath}";


        return Task.Run(() =>
        {
            _mutex.WaitOne();
            try
            {
                Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.Process);
                var process = new Process
                {
                    StartInfo = startInfo
                };
                beforeStart?.Invoke(process);
                process.Start();
                Environment.SetEnvironmentVariable("PATH", originPath, EnvironmentVariableTarget.Process);
                return process;
            }
            finally
            {
                _mutex.ReleaseMutex();
            }
        });
    }

    public async Task Start()
    {
        ServerProcess = await GetProcess(Config, process =>
        {
            process.OutputDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                {
                    Log.Information("[MinecraftServer] " + args.Data);
                }
            };
        });
        ServerProcess.BeginOutputReadLine();
    }
}