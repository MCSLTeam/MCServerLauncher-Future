using System.Diagnostics;

namespace MCServerLauncher.Daemon.Minecraft.Server;

public class ServerInstance
{
    public ServerConfig Config { get; private set; }
    static Mutex _mutex = new();

    public ServerInstance(ServerConfig config)
    {
        Config = config;
    }

    /// <summary>
    ///  获取mc服务器进程
    /// 在创建服务器进程时,使用互斥锁修改Daemon进程的环境变量是为了同时兼容jar启动和bat/sh脚本启动的情况
    /// </summary>
    /// <param name="config">带创建进程的配置文件</param>
    /// <returns></returns>
    public static Process GetProcess(ServerConfig config)
    {
        var args = config.GetLaunchArguments();
        var startInfo = new ProcessStartInfo(config.JavaPath, args)
        {
            UseShellExecute = false,
            WorkingDirectory = "", // TODO
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
        };

        var originPath = Environment.GetEnvironmentVariable("PATH");
        var newPath = BasicUtils.IsWindows()
            ? $"{Path.GetDirectoryName(config.JavaPath)};{originPath}"
            : $"{Path.GetDirectoryName(config.JavaPath)}:{originPath}";

        _mutex.WaitOne();
        try
        {
            Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.Process);
            var process = Process.Start(startInfo);
            Environment.SetEnvironmentVariable("PATH", originPath, EnvironmentVariableTarget.Process);
            return process;
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
    }
}