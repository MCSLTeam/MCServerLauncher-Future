using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.ProtoType.Action;

namespace MCServerLauncher.DaemonClient;

public class Daemon : IDaemon
{
    private Daemon(ClientConnection connection)
    {
        Connection = connection;
    }

    public WebSocketState WebsocketState => Connection.WebSocket.State;
    public ClientConnection Connection { get; }
    public bool IsClosed => Connection.Closed;

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


    private static async Task Main()
    {
        Console.WriteLine("|||||||||||||||||||||||start connecting...");
        try
        {
            var daemon = await OpenAsync("127.0.0.1", 11451, "LEFELMeM1qIXxZTUUSzDC6t2GbCYMFhJ", false,
                new ClientConnectionConfig
                {
                    MaxPingPacketLost = 3,
                    PendingRequestCapacity = 100,
                    PingInterval = TimeSpan.FromSeconds(1),
                    PingTimeout = -1
                });

            Console.WriteLine(await daemon.PingAsync());
            await Task.Delay(10000);
            await daemon.CloseAsync();
            // Console.WriteLine($"|||||||||||||||||||||||connected: {await daemon.PingAsync()}");
            // Console.WriteLine(await daemon.GetSystemInfoAsync());
        }
        catch (WebSocketException e)
        {
            Console.WriteLine($"[Daemon] Error occurred when connecting to daemon: {e.Message}");
            Console.WriteLine(e.InnerException);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    //public static async Task RunTest()
    //{
    //    var usr = "admin";
    //    var pwd = "3a9eff1b-4723-407e-a072-30da28b00f4a";
    //    // var pwd1 = "Hqwd7H5WHLIgeyNu00jMlA==";
    //    var isSecure = false;
    //    var ip = "127.0.0.1";
    //    var port = 11451;

    //    // login
    //    var token = await LoginAsync(ip, port, usr, pwd, isSecure, 86400) ?? "token not found";
    //    Log.Debug($"Token got: {token}");

    //    try
    //    {
    //        var daemon = await OpenAsync(ip, port, token, isSecure, new ClientConnectionConfig
    //        {
    //            MaxPingPacketLost = 3,
    //            PendingRequestCapacity = 100,
    //            PingInterval = TimeSpan.FromSeconds(1),
    //            PingTimeout = 3000
    //        });
    //        Log.Information("[daemon] connected: {0}", await daemon.PingAsync());
    //        await Task.Delay(3000);
    //        var rv = await daemon.GetJavaListAsync();
    //        rv.ForEach(x => Log.Debug($"Java: {x.ToString()}"));
    //        await Task.Delay(3001);
    //        await daemon.CloseAsync();
    //    }
    //    catch (WebSocketException e)
    //    {
    //        Log.Error($"[Daemon] Websocket Error occurred when connecting to daemon(ws://{ip}:{port}): {e}");
    //    }
    //    catch (TimeoutException e)
    //    {
    //        Log.Error($"[Daemon] Timeout occurred when connecting to daemon(ws://{ip}:{port}): {e}");
    //    }
    //    catch (Exception e)
    //    {
    //        Log.Error($"[Daemon] Error occurred when connecting to daemon(ws://{ip}:{port}): {e}");
    //    }
    //}
}