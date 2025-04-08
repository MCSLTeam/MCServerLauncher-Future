using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.DaemonClient.Connection;
using Newtonsoft.Json;

namespace MCServerLauncher.DaemonClient;

public class Daemon : IDaemon
{
    private bool _disposed;

    private Daemon(ClientConnection connection)
    {
        Connection = connection;

        Connection.OnEventReceived += OnEvent;
    }

    internal ClientConnection Connection { get; }

    public bool Online => Connection.Client.Online;
    public SubscribedEvents SubscribedEvents { get; } = new();
    public event Action<Guid, string>? InstanceLogEvent;
    public event Action<DaemonReport, long>? DaemonReportEvent;


    /// <summary>
    ///     心跳包是否超时
    /// </summary>
    public bool PingLost => Connection.PingLost;

    /// <summary>
    ///     上次ping时间
    /// </summary>
    public DateTime LastPing => Connection.LastPong;


    public async Task CloseAsync()
    {
        await Connection.CloseAsync();
    }

    /// <summary>
    ///     RPC中枢
    /// </summary>
    /// <param name="actionType"></param>
    /// <param name="parameter"></param>
    /// <param name="timeout"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<TResult> RequestAsync<TResult>(
        ActionType actionType,
        IActionParameter? parameter,
        int timeout = -1,
        CancellationToken cancellationToken = default
    )
        where TResult : class, IActionResult
    {
        return await Connection.RequestAsync<TResult>(actionType, parameter, timeout, cancellationToken);
    }

    /// <summary>
    ///     RPC中枢
    /// </summary>
    /// <param name="actionType"></param>
    /// <param name="parameter"></param>
    /// <param name="timeout"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task RequestAsync(
        ActionType actionType,
        IActionParameter? parameter,
        int timeout = -1,
        CancellationToken cancellationToken = default
    )
    {
        await Connection.RequestAsync(actionType, parameter, timeout, cancellationToken);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }


    /// <summary>
    ///     连接远程服务器
    /// </summary>
    /// <param name="address">ip地址</param>
    /// <param name="port">端口</param>
    /// <param name="token">jwt token</param>
    /// <param name="isSecure">是否使用wss</param>
    /// <param name="config">Daemon连接配置</param>
    /// <param name="timeout">连接超时时间</param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="WebSocketException">Daemon连接失败</exception>
    /// <exception cref="TimeoutException">连接超时</exception>
    /// <returns></returns>
    public static async Task<IDaemon> OpenAsync(string address, int port, string token,
        bool isSecure, ClientConnectionConfig config, int timeout = -1, CancellationToken cancellationToken = default)
    {
        var connection = await ClientConnection.OpenAsync(address, port, token, isSecure, config, cancellationToken)
            .TimeoutAfter(timeout);


        return new Daemon(connection);
    }

    private void OnEvent(EventType type, long timestamp, IEventMeta? meta, IEventData? data)
    {
        // ms timestamp转datetime
        switch (type)
        {
            case EventType.InstanceLog:
                // TODO 改为异步? 
                InstanceLogEvent?.Invoke(
                    (meta as InstanceLogEventMeta)!.InstanceId,
                    (data as InstanceLogEventData)!.Log
                );
                break;
            case EventType.DaemonReport:
                DaemonReportEvent?.Invoke(
                    (data as DaemonReportEventData)!.Report,
                    DateTime.UtcNow.ToUnixTimeMilliSeconds() - timestamp
                );
                break;
            default:
                throw new NotImplementedException(type.ToString());
        }
    }


    private static async Task Main()
    {
        try
        {
            var client = new HttpClient();
            for (var i = 0; i < 3; i++) Console.WriteLine(await client.GetStringAsync("http://127.0.0.1:11451/info"));

            // var daemon = await OpenAsync("172.23.190.95", 11451, "gUkBnAjxdNUx5wGZ1fgTXkvkppsIdj5D", false,
            var daemon = await OpenAsync(
                "127.0.0.1",
                11451,
                "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJwZXJtaXNzaW9ucyI6IioiLCJleHAiOjE3NDQxNzMyNDgsImlzcyI6Ik1DU2VydmVyTGF1bmNoZXIuRGFlbW9uIiwiYXVkIjoiTUNTZXJ2ZXJMYXVuY2hlci5EYWVtb24ifQ.t9bGF8xvEnN-bieKIEn6Gbe7-dlYL3aEcVeDbmhmgdc"
                , false,
                new ClientConnectionConfig
                {
                    HeartBeat = true,
                    MaxFailCount = 3,
                    PendingRequestCapacity = 100,
                    HeartBeatTick = TimeSpan.FromSeconds(3),
                    PingTimeout = 5000
                });
            daemon.DaemonReportEvent += (report, l) => { Console.WriteLine($"Daemon report: {report}\n ({l}ms)"); };

            // await daemon.SubscribeEvent(EventType.DaemonReport, null);
            // await Task.Delay(6010);
            // await daemon.CloseAsync();
            // return;
            Console.WriteLine("Connection OK");
            Console.WriteLine($"Daemon Latency: {await daemon.PingAsync()}ms");

            Console.WriteLine("\nDaemon system info:");
            var systemInfo = await daemon.GetSystemInfoAsync();
            Console.WriteLine(JsonConvert.SerializeObject(systemInfo, Formatting.Indented));

            Console.WriteLine("Wait 3000ms");
            await Task.Delay(3000);

            Console.WriteLine("\nDaemon side java list:");
            foreach (var info in await daemon.GetJavaListAsync()) Console.WriteLine($"    - {info.ToString()}");

            Console.WriteLine("Wait 3000ms");
            await Task.Delay(3000);
            Console.WriteLine("\nDaemon instances:");
            var status = await daemon.GetAllStatusAsync();
            foreach (var kv in status) Console.WriteLine($"    - {kv.Key}: {kv.Value.Config.Name}");

            var guid = status.FirstOrDefault().Key;

            if (guid == Guid.Empty)
            {
                // 从网络上下载mc核心并安装
                Console.WriteLine("No Instance found in daemon, download core from internet and install it!");
                var setting = new InstanceFactorySetting
                {
                    Uuid = Guid.Parse("fdbf680c-fe52-4f1d-89ba-a0d9d8b857b2"),
                    Name = "1-21-1",
                    InstanceType = InstanceType.Forge,
                    Target = "installer.jar",
                    TargetType = TargetType.Jar,
                    JavaPath = "java",
                    JavaArgs = Array.Empty<string>(),
                    SourceType = SourceType.Core,
                    // Source = "https://download.fastmirror.net/download/Vanilla/release/1.21.1-59353f",
                    Source =
                        "https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.4-49.2.0/forge-1.20.4-49.2.0-installer.jar",
                    // Source = "daemon/downloads/forge-1.20.4-49.2.0-installer.jar",
                    McVersion = "1.21.1",
                    UsePostProcess = false
                };
                guid = Guid.Parse("fdbf680c-fe52-4f1d-89ba-a0d9d8b857b2");
                Console.WriteLine($"Creating Instance: {setting.Name} ({setting.Uuid})");

                var config = await daemon.TryAddInstanceAsync(setting);
                Console.WriteLine("[InstanceManager] Created Server: " + config.Name);
            }

            Console.WriteLine("Wait 3000ms");
            await Task.Delay(3000);
            Console.WriteLine($"\nStarting Instance: {guid}");
            var instStatus = await daemon.GetInstanceStatusAsync(guid);
            var instName = instStatus.Config.Name;

            // 订阅实例日志
            daemon.InstanceLogEvent += (id, log) =>
            {
                if (id == guid) Console.WriteLine($"[{instName}] {log}");
            };
            await daemon.SubscribeEvent(EventType.InstanceLog, new InstanceLogEventMeta
            {
                InstanceId = guid
            });

            // 启动实例
            await daemon.StartInstanceAsync(guid);
            await Task.Delay(100);
            await PrintServerStatus(daemon, guid);

            // 等待一段时间
            await Task.Delay(15000);
            await PrintServerStatus(daemon, guid);
            Console.WriteLine("SEND MESSAGE TO DAEMON: 'help'");
            await daemon.SentToInstanceAsync(guid, "help");
            await Task.Delay(3000);
            Console.WriteLine("SEND MESSAGE TO DAEMON: 'list'");
            await daemon.SentToInstanceAsync(guid, "list");

            // 关闭实例
            await Task.Delay(3000);
            Console.WriteLine("STOP INSTANCE");
            await daemon.StopInstanceAsync(guid);
            await PrintServerStatus(daemon, guid);

            await Task.Delay(1000);
            await PrintServerStatus(daemon, guid);

            // await daemon.UnSubscribeEvent(EventType.InstanceLog, new InstanceLogEventMeta
            // {
            //     InstanceId = guid
            // });
            await daemon.CloseAsync();
        }
        catch (WebSocketException e)
        {
            Console.WriteLine($"[Daemon] Error occurred when connecting to daemon: {e.Message}");
            Console.WriteLine(e.InnerException);
        }
        catch (WebException)
        {
            Console.WriteLine("[Daemon] Can't connect to daemon, please check your network and connection settings");
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private static async Task PrintServerStatus(IDaemon daemon, Guid id)
    {
        var instStatus = await daemon.GetInstanceStatusAsync(id);
        Console.WriteLine($"[ServerStatus] '{instStatus.Config.Name}' status is {instStatus.Status}");
    }

    private void Dispose(bool disposed)
    {
        if (_disposed) return;
        if (disposed) Connection.Dispose();

        _disposed = true;
    }

    ~Daemon()
    {
        Dispose(false);
    }
}