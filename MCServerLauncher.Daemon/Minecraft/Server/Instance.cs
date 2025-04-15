using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.Network;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Storage;
using Serilog;

namespace MCServerLauncher.Daemon.Minecraft.Server;

public class Instance
{
    private readonly string _configPath;
    private readonly List<string> _properties = new();

    public Instance(InstanceConfig config)
    {
        Config = config;
        _configPath = Path.Combine(config.GetWorkingDirectory(), InstanceConfig.FileName);

        OnStatusChanged += OnStatusChangedHandler;
    }

    public InstanceConfig Config { get; private set; }
    public ushort? Port { get; private set; }
    private Process? ServerProcess { get; set; }
    public ServerStatus Status { get; private set; } = ServerStatus.Stopped;

    public List<string> ServerProperties
    {
        // 如果缓存为空，则尝试刷新一次
        get
        {
            if (_properties.Count == 0) SetServerProperties();
            return _properties;
        }
    }

    public event Action<Guid, ServerStatus>? OnStatusChanged;

    public event Action<Guid, string>? OnLog;

    /// <summary>
    ///     获取mc服务器进程
    ///     在创建服务器进程时,使用互斥锁修改Daemon进程的环境变量是为了同时兼容jar启动和bat/sh脚本启动的情况
    /// </summary>
    /// <param name="config">带创建进程的配置文件</param>
    /// <param name="beforeStart">在启动前执行的操作(连接stdout等)</param>
    /// <returns></returns>
    private Process GetProcess(InstanceConfig config, Action<Process> beforeStart)
    {
        var (target, args) = config.GetLaunchScript();

        var startInfo = new ProcessStartInfo(target, args)
        {
            UseShellExecute = false,
            WorkingDirectory = config.GetWorkingDirectory(),
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true
        };

        var originPath = Environment.GetEnvironmentVariable("PATH");
        startInfo.EnvironmentVariables["PATH"] = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $"{Path.GetDirectoryName(config.JavaPath)};{originPath}"
            : $"{Path.GetDirectoryName(config.JavaPath)}:{originPath}";

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        beforeStart?.Invoke(process);

        ChangeStatus(ServerStatus.Starting);
        process.Start();

        return process;
    }

    private IEnumerable<string> GetServerProperties()
    {
        var path = Path.Combine(Config.GetWorkingDirectory(), "server.properties");
        return File.Exists(path) ? File.ReadAllLines(path).ToList() : Enumerable.Empty<string>();
    }

    private void SetServerProperties()
    {
        _properties.Clear();
        _properties.AddRange(GetServerProperties());
    }

    private bool TryRefreshConfig()
    {
        try
        {
            var newConfig = FileManager.ReadJson<InstanceConfig>(_configPath)!;
            if (Config.Uuid != newConfig.Uuid) return false;
            Config = newConfig;
            return true;
        }
        catch (Exception e)
        {
            Log.Debug("[Instance] Failed to refresh config at '{0}', ignored: {1}", _configPath, e.Message);
            return false;
        }
    }

    // TODO ,stderr的接收；Player List改为使用MC SLP协议
    public async Task<bool> StartAsync(int delayToCheck = 500)
    {
        if (ServerProcess is not null)
        {
            if (!ServerProcess.HasExited) return false;
            ServerProcess.Close();
            ServerProcess.Dispose();
        }

        // 尝试从磁盘刷新配置
        TryRefreshConfig();

        ServerProcess = GetProcess(Config, process =>
        {
            process.OutputDataReceived += (sender, args) =>
            {
                var msg = args.Data;

                if (msg == null) return;

                if (msg.Contains("Done"))
                    ChangeStatus(ServerStatus.Running);
                else if (msg.Contains("Stopping the server"))
                    ChangeStatus(ServerStatus.Stopping);
                else if (msg.Contains("Minecraft has crashed")) ChangeStatus(ServerStatus.Crashed);
            };
            process.OutputDataReceived += (_, arg) =>
            {
                if (arg.Data is not null) OnLog?.Invoke(Config.Uuid, arg.Data);
            };
            process.ErrorDataReceived += (_, arg) =>
            {
                Log.Error($"[Server({Config.Name})] [STDERR] {arg.Data}");
                if (arg.Data is not null) OnLog?.Invoke(Config.Uuid, "[STDERR] " + arg.Data);
            };

            process.Exited += (_, _) =>
            {
                if (Status != ServerStatus.Crashed) ChangeStatus(ServerStatus.Stopped);
            };
        });
        ServerProcess.BeginOutputReadLine();
        ServerProcess.BeginErrorReadLine();

        await Task.Delay(delayToCheck);
        return !ServerProcess.HasExited;
    }

    public async Task KillProcess()
    {
        ServerProcess?.Kill();
        await WaitForExitAsync();
        ChangeStatus(ServerStatus.Stopped);
    }

    public Task WaitForExitAsync(CancellationToken ct = default)
    {
        return ServerProcess?.WaitForExitAsync(ct) ?? Task.CompletedTask;
    }

    public void WaitForExit()
    {
        ServerProcess?.WaitForExit();
    }

    public void WriteLine(string? message)
    {
        ServerProcess?.StandardInput.WriteLine(message);
    }

    private void ChangeStatus(ServerStatus newStatus)
    {
        Status = newStatus;
        OnStatusChanged?.Invoke(Config.Uuid, newStatus);
    }

    private void OnStatusChangedHandler(Guid _, ServerStatus newStatus)
    {
        if (newStatus == ServerStatus.Running)
        {
            // refresh server.properties
            SetServerProperties();

            // do something with server.properties
            if (ushort.TryParse(_properties.FirstOrDefault(line => line.StartsWith("server-port="))?.Split('=')[1],
                    out var parsed))
            {
                Port = parsed;
                Log.Debug("[Instance({0})] Server Port: {1}", Config.Name, Port);
            }
            else
            {
                Port = null;
                Log.Warning("[Instance({0})] Can't find or parse server port in server.properties", Config.Name);
            }
        }
    }

    public async Task<Player[]> GetServerPlayersAsync()
    {
        if (McVersion.Of(Config.McVersion) >= McVersion.Of("1.7") && Port.HasValue && Status == ServerStatus.Running)
        {
            try
            {
                var status = await SlpClient.GetStatusModern("127.0.0.1", Port.Value);
                if (status != null)
                {
                    return status.Payload.Players.Sample.Select(player => new Player(player.Name, player.Id)).ToArray();
                }
            }
            catch (SocketException)
            {
                return Array.Empty<Player>();
            }
        }

        return Array.Empty<Player>();
    }

    // TODO 使用SlpClient获取服务器信息(例如玩家列表)
    public async Task<InstanceStatus> GetStatusAsync()
    {
        return new InstanceStatus(
            Status,
            Config,
            ServerProperties,
            await GetServerPlayersAsync()
        );
    }
}