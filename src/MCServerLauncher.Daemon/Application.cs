using System.Reflection;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MCServerLauncher.Daemon.ApplicationCore;
using MCServerLauncher.Daemon.Bootstrap;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Remote.Event;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using TouchSocket.Http;

namespace MCServerLauncher.Daemon;

public static class Application
{
    public static readonly DateTime StartTime = DateTime.Now;
    // Timer lifecycle managed by DaemonServiceComposition.AttachDaemonLifecycle
    public static HttpService HttpService { get; private set; } = default!;
    public static Version AppVersion => Assembly.GetExecutingAssembly().GetName().Version!;
    public static event Func<Task>? OnStarted;
    public static event Func<Task>? OnStopping;

    public static async Task SetupAsync()
    {
        var selectedRegistry = ActionHandlerRegistryRuntime.Selected;
        IServiceCollection collection = new ServiceCollection();
        collection.AddLogging(loggingBuilder => loggingBuilder.AddSerilog(dispose: true));

        HttpService = new HttpService();
        await HttpService.SetupAsync(DaemonTouchSocketTransportProfile.CreateConfig(collection, HttpService, selectedRegistry));

        DaemonServiceComposition.AttachDaemonLifecycle(HttpService);
    }


    /// <summary>
    ///     读取配置,添加/api/v1和/login路由的handler,并启动HttpServer。
    ///     路由/api/v1: ws长连接，实现rpc。
    ///     路由/login: http请求，实现登录，返回一个jwt。
    /// </summary>
    public static async Task ServeAsync()
    {
        var gs = HttpService.Resolver.GetRequiredService<GracefulShutdown>();

        OnStopping += () => StopAsync();
        gs.OnShutdown += () => OnStopping.Invoke();

        await HttpService.StartAsync();
        var endpoints = GetRemoteEndpoints();
        Log.Information("[Remote] Bind endpoint: {BindEndpoint}", endpoints.BindEndpoint);
        Log.Information("[Remote] Ws Server connect URLs: {ConnectUrls}", string.Join(", ", endpoints.WebSocketConnectUrls));
        Log.Information("[Remote] Http Server connect URLs: {ConnectUrls}", string.Join(", ", endpoints.HttpConnectUrls));
        Log.Information("[Remote] Apifox docs connect URLs: {ConnectUrls}", string.Join(", ", endpoints.ApifoxConnectUrls));

        await (OnStarted?.Invoke() ?? Task.CompletedTask);

        await gs.WaitForShutdownAsync();

        // 最后释放HttpService
        Log.Debug("[Application] shutting down Http service ...");
        await HttpService.StopAsync();
    }

    private static async Task StopAsync(int timeout = -1)
    {
        using var cts = new CancellationTokenSource(timeout);

        HttpService.Resolver.GetRequiredService<EventTriggerService>().Stop();
        HttpService.Resolver.GetRequiredService<LegacyDomainEventAdapter>().Dispose();
        HttpService.Resolver.GetRequiredService<InstanceDomainEventBridge>().Dispose();

        Log.Debug("[ApplicationCore] stopping local application services ...");
        await HttpService.Resolver.GetRequiredService<IDaemonRuntimeLifecycle>().StopAsync(cts.Token);

        Log.Debug("[WsContextContainer] closing websocket connections ...");
        await Task.WhenAll(HttpService.Resolver.GetRequiredService<WsContextContainer>()
            .Select(kv => kv.Value.GetWebsocket().CloseAsync("Daemon exit", cts.Token)));
    }

    private static RemoteEndpoints GetRemoteEndpoints()
    {
        var endpoint = HttpService.Monitors
            .Select(monitor => monitor.Socket.LocalEndPoint)
            .OfType<IPEndPoint>()
            .FirstOrDefault();

        if (endpoint is null)
        {
            endpoint = new IPEndPoint(IPAddress.Any, AppConfig.Get().Port);
        }

        var connectAuthorities = GetConnectableAuthorities(endpoint).ToArray();
        return new RemoteEndpoints(
            FormatAuthority(endpoint.Address, endpoint.Port),
            connectAuthorities.Select(authority => $"ws://{authority}/api/v1").ToArray(),
            connectAuthorities.Select(authority => $"http://{authority}/").ToArray(),
            connectAuthorities.Select(authority => $"http://{authority}/apifox.json").ToArray());
    }

    private static IEnumerable<string> GetConnectableAuthorities(IPEndPoint endpoint)
    {
        if (!IPAddress.IsLoopback(endpoint.Address) && !IsWildcardAddress(endpoint.Address))
        {
            yield return FormatAuthority(endpoint.Address, endpoint.Port);
            yield break;
        }

        yield return FormatAuthority(IPAddress.Loopback, endpoint.Port);

        foreach (var address in EnumerateLanIPv4Addresses())
        {
            yield return FormatAuthority(address, endpoint.Port);
        }
    }

    private static IEnumerable<IPAddress> EnumerateLanIPv4Addresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
            .Where(address => !IPAddress.IsLoopback(address))
            .Distinct();
    }

    private static bool IsWildcardAddress(IPAddress address)
    {
        return address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);
    }

    private static string FormatAuthority(IPAddress address, int port)
    {
        return address.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{address}]:{port}"
            : $"{address}:{port}";
    }

    private sealed record RemoteEndpoints(
        string BindEndpoint,
        string[] WebSocketConnectUrls,
        string[] HttpConnectUrls,
        string[] ApifoxConnectUrls);

    public static async Task<bool> InitAsync()
    {
        return await DaemonStartupInitialization.InitializeAsync();
    }
}
