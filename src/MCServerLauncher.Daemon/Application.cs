using System.Reflection;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MCServerLauncher.Daemon.ApplicationCore;
using MCServerLauncher.Daemon.Bootstrap;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using TouchSocket.Core;
using TouchSocket.Core.AspNetCore;
using TouchSocket.Http;
using TouchSocket.Sockets;

namespace MCServerLauncher.Daemon;

public static class Application
{
    private static readonly SemaphoreSlim HostOwnershipGate = new(1, 1);
    private static DaemonHostContext? _currentHost;
    private static HttpService? _detachedHttpService;

    public static readonly DateTime StartTime = DateTime.Now;
    // Timer lifecycle managed by DaemonServiceComposition.AttachDaemonLifecycle
    public static HttpService HttpService
    {
        get => Volatile.Read(ref _currentHost)?.HttpService ?? Volatile.Read(ref _detachedHttpService)!;
        private set => Volatile.Write(ref _detachedHttpService, value);
    }
    public static Version AppVersion => Assembly.GetExecutingAssembly().GetName().Version!;
    public static event Func<Task>? OnStarted;
    public static event Func<Task>? OnStopping;
    internal static DaemonHostContext? CurrentHostContext => Volatile.Read(ref _currentHost);

    public static Task SetupAsync() => SetupAsync(null, null, null);

    internal static Task SetupAsync(Action<IServiceCollection>? configureServices) =>
        SetupAsync(configureServices, null, null);

    internal static async Task SetupAsync(
        Action<IServiceCollection>? configureServices,
        IPHost? listenHost,
        DaemonHostTestHooks? testHooks)
    {
        await HostOwnershipGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var previousHost = Volatile.Read(ref _currentHost);
            if (previousHost?.IsServing == true)
                throw new InvalidOperationException("The active daemon host cannot be replaced while ServeAsync is running.");

            if (previousHost is not null)
            {
                Volatile.Write(ref _currentHost, null);
                await previousHost.DisposeAsync().ConfigureAwait(false);
            }

            var host = await CreateHostAsync(configureServices, listenHost, testHooks).ConfigureAwait(false);
            Volatile.Write(ref _currentHost, host);
        }
        finally
        {
            HostOwnershipGate.Release();
        }
    }


    /// <summary>
    ///     读取配置，添加 /api/v2 WebSocket 和 HTTP 路由，并启动 HttpServer。
    ///     路由 /api/v2: WebSocket 长连接，实现 JSON-RPC V2 和版本化二进制帧。
    /// </summary>
    public static Task ServeAsync() => ServeAsync(null);

    internal static async Task ServeAsync(Func<DaemonHostContext, Task>? afterListenerStarted)
    {
        DaemonHostContext host;
        await HostOwnershipGate.WaitAsync().ConfigureAwait(false);
        try
        {
            host = Volatile.Read(ref _currentHost) ??
                   throw new InvalidOperationException("The daemon host has not been set up.");
            if (!host.TryBeginServe())
                throw new InvalidOperationException("ServeAsync is already running for the current daemon host.");
        }
        finally
        {
            HostOwnershipGate.Release();
        }

        CancellationTokenRegistration shutdownRegistration = default;
        try
        {
            var gs = host.HttpService.Resolver.GetRequiredService<GracefulShutdown>();
            if (!gs.CancellationToken.IsCancellationRequested)
            {
                await host.HttpService.StartAsync().ConfigureAwait(false);
                shutdownRegistration = gs.CancellationToken.Register(
                    static state => ((DaemonHostContext)state!).RequestStop(),
                    host);
                if (gs.CancellationToken.IsCancellationRequested)
                    host.RequestStop();

                var endpoints = GetRemoteEndpoints(host.HttpService);
                Log.Information("[Remote] Bind endpoint: {BindEndpoint}", endpoints.BindEndpoint);
                Log.Information("[Remote] Ws connect URLs: {ConnectUrls}", string.Join(", ", endpoints.WebSocketConnectUrls));
                Log.Information("[Remote] Http Server connect URLs: {ConnectUrls}", string.Join(", ", endpoints.HttpConnectUrls));
                Log.Information("[Remote] Apifox docs connect URLs: {ConnectUrls}", string.Join(", ", endpoints.ApifoxConnectUrls));

                if (afterListenerStarted is not null)
                    await afterListenerStarted(host).ConfigureAwait(false);

                await host.StartAsync(gs.CancellationToken).ConfigureAwait(false);
            }
            else
            {
                host.RequestStop();
            }

            await gs.WaitForShutdownAsync().ConfigureAwait(false);
            await host.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            shutdownRegistration.Dispose();
            try
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await ClearCurrentHostAsync(host).ConfigureAwait(false);
            }
        }
    }

    private static async Task InvokeAsync(Func<Task>? handlers)
    {
        if (handlers is null)
            return;

        foreach (var handler in handlers.GetInvocationList().Cast<Func<Task>>())
            await handler().ConfigureAwait(false);
    }

    private static async Task<DaemonHostContext> CreateHostAsync(
        Action<IServiceCollection>? configureServices,
        IPHost? listenHost,
        DaemonHostTestHooks? testHooks)
    {
        IServiceCollection collection = new ServiceCollection();
        collection.AddLogging(loggingBuilder => loggingBuilder.AddSerilog(dispose: true));
        configureServices?.Invoke(collection);

        var httpService = new HttpService();
        IServiceProvider? rootProvider = null;
        DaemonLifecycleAttachment? attachment = null;
        DaemonTouchSocketTransportConfiguration? transport = null;
        var stopCore = testHooks?.StopCore;
        var disposeHttpService = testHooks?.DisposeHttpService ??
                                 (static (HttpService service) => service.SafeDispose());
        try
        {
            transport = listenHost is null
                ? DaemonTouchSocketTransportProfile.CreateConfig(collection, httpService)
                : DaemonTouchSocketTransportProfile.CreateConfig(collection, httpService, listenHost);
            await httpService.SetupAsync(transport.Config).ConfigureAwait(false);
            rootProvider = transport.Container.ServiceProvider;
            attachment = DaemonServiceComposition.AttachDaemonLifecycle(httpService);
            return new DaemonHostContext(
                httpService,
                attachment,
                transport.Container,
                rootProvider,
                stopCore is null ? null : () => stopCore(httpService),
                () => disposeHttpService(httpService));
        }
        catch (Exception setupException)
        {
            if (rootProvider is null && transport is not null)
            {
                try
                {
                    rootProvider = transport.Container.ServiceProvider;
                }
                catch
                {
                }
            }
            try
            {
                await CleanupPartialHostAsync(
                    httpService,
                    attachment,
                    rootProvider,
                    disposeHttpService).ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException("Daemon host setup and cleanup both failed.",
                    setupException, cleanupException);
            }

            throw;
        }
    }

    private static async Task CleanupPartialHostAsync(
        HttpService httpService,
        DaemonLifecycleAttachment? attachment,
        IServiceProvider? rootProvider,
        Func<HttpService, Result> disposeHttpService)
    {
        List<Exception> failures = [];
        if (attachment is not null)
            await RunStepAsync(() => attachment.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        await RunStepAsync(() => httpService.StopAsync(), failures).ConfigureAwait(false);
        CaptureHttpServiceDisposeResult(httpService, disposeHttpService, failures);

        if (rootProvider is not null)
            await RunStepAsync(() => DisposeRootProviderAsync(rootProvider), failures).ConfigureAwait(false);
        ThrowIfCleanupFailed(failures, "Daemon host setup cleanup failed.");
    }

    private static async Task ClearCurrentHostAsync(DaemonHostContext host)
    {
        await HostOwnershipGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (ReferenceEquals(Volatile.Read(ref _currentHost), host))
                Volatile.Write(ref _currentHost, null);
        }
        finally
        {
            HostOwnershipGate.Release();
        }
    }

    internal static async Task DisposeCurrentHostAsync()
    {
        await HostOwnershipGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var host = Volatile.Read(ref _currentHost);
            if (host?.IsServing == true)
                throw new InvalidOperationException("The active daemon host must be stopped by ServeAsync.");
            if (host is null)
                return;

            Volatile.Write(ref _currentHost, null);
            await host.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            HostOwnershipGate.Release();
        }
    }

    private static async Task RunStepAsync(Func<Task> step, List<Exception> failures)
    {
        try
        {
            await step().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static void CaptureHttpServiceDisposeResult(
        HttpService httpService,
        Func<HttpService, Result> disposeHttpService,
        List<Exception> failures)
    {
        try
        {
            var result = disposeHttpService(httpService);
            if (!result.IsSuccess)
                failures.Add(new HttpServiceDisposeException(result.ResultCode, result.Message));
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static async Task DisposeRootProviderAsync(IServiceProvider rootProvider)
    {
        if (rootProvider is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (rootProvider is IDisposable disposable)
            disposable.Dispose();
    }

    private static void ThrowIfCleanupFailed(List<Exception> failures, string message)
    {
        if (failures.Count != 0)
            throw new AggregateException(message, failures);
    }

    private static RemoteEndpoints GetRemoteEndpoints(HttpService httpService)
    {
        var endpoint = httpService.Monitors
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
            connectAuthorities.Select(authority => $"ws://{authority}/api/v2").ToArray(),
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

    internal sealed record DaemonHostTestHooks(
        Func<HttpService, Task>? StopCore = null,
        Func<HttpService, Result>? DisposeHttpService = null);

    internal sealed class HttpServiceDisposeException : InvalidOperationException
    {
        internal HttpServiceDisposeException(ResultCode resultCode, string? resultMessage)
            : base("TouchSocket HTTP service disposal failed.")
        {
            ResultCode = resultCode;
            ResultMessage = resultMessage;
        }

        internal ResultCode ResultCode { get; }
        internal string? ResultMessage { get; }
    }

    internal sealed class DaemonHostContext : IAsyncDisposable
    {
        private readonly SemaphoreSlim _operationGate = new(1, 1);
        private readonly object _stopOwnershipGate = new();
        private readonly object _disposeOwnershipGate = new();
        private readonly Func<Task>? _stopCoreOverride;
        private readonly Func<Result> _disposeHttpService;
        private Task? _stopTask;
        private Task? _disposeTask;
        private int _serveClaimed;
        private int _disposeStarted;

        internal DaemonHostContext(
            HttpService httpService,
            DaemonLifecycleAttachment lifecycle,
            AspNetCoreContainer rootContainer,
            IServiceProvider rootProvider,
            Func<Task>? stopCoreOverride,
            Func<Result> disposeHttpService)
        {
            HttpService = httpService ?? throw new ArgumentNullException(nameof(httpService));
            Lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            RootContainer = rootContainer ?? throw new ArgumentNullException(nameof(rootContainer));
            RootProvider = rootProvider ?? throw new ArgumentNullException(nameof(rootProvider));
            _stopCoreOverride = stopCoreOverride;
            _disposeHttpService = disposeHttpService ?? throw new ArgumentNullException(nameof(disposeHttpService));
        }

        internal HttpService HttpService { get; }
        internal DaemonLifecycleAttachment Lifecycle { get; }
        internal AspNetCoreContainer RootContainer { get; }
        internal IServiceProvider RootProvider { get; }
        internal bool IsServing => Volatile.Read(ref _serveClaimed) != 0;
        internal int StopExecutionCount { get; private set; }
        internal int DisposeExecutionCount { get; private set; }

        internal bool TryBeginServe()
        {
            if (Volatile.Read(ref _disposeStarted) != 0)
                return false;
            return Interlocked.CompareExchange(ref _serveClaimed, 1, 0) == 0;
        }

        internal async Task<bool> StartAsync(CancellationToken shutdownToken)
        {
            await _operationGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (shutdownToken.IsCancellationRequested || IsStopRequested())
                    return false;
                if (!await Lifecycle.StartAsync().ConfigureAwait(false))
                    return false;

                await InvokeAsync(OnStarted).ConfigureAwait(false);
                return true;
            }
            finally
            {
                _operationGate.Release();
            }
        }

        internal void RequestStop() => _ = StopAsync();

        internal Task StopAsync()
        {
            TaskCompletionSource? owner = null;
            Task task;
            lock (_stopOwnershipGate)
            {
                if (_stopTask is not null)
                    return _stopTask;

                owner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                task = owner.Task;
                _stopTask = task;
            }

            _ = CompleteStopAsync(owner);
            return task;
        }

        public ValueTask DisposeAsync() => new(DisposeOwnedAsync());

        private Task DisposeOwnedAsync()
        {
            TaskCompletionSource? owner = null;
            Task task;
            lock (_disposeOwnershipGate)
            {
                if (_disposeTask is not null)
                    return _disposeTask;

                owner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                task = owner.Task;
                _disposeTask = task;
                Volatile.Write(ref _disposeStarted, 1);
            }

            _ = CompleteDisposeAsync(owner);
            return task;
        }

        private async Task CompleteStopAsync(TaskCompletionSource completion)
        {
            try
            {
                await StopCoreAsync().ConfigureAwait(false);
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        private async Task StopCoreAsync()
        {
            await _operationGate.WaitAsync().ConfigureAwait(false);
            try
            {
                StopExecutionCount++;
                List<Exception> failures = [];

                await RunStepAsync(Lifecycle.StopAsync, failures).ConfigureAwait(false);
                if (OnStopping is not null)
                {
                    foreach (var handler in OnStopping.GetInvocationList().Cast<Func<Task>>())
                        await RunStepAsync(handler, failures).ConfigureAwait(false);
                }

                if (_stopCoreOverride is not null)
                {
                    await RunStepAsync(_stopCoreOverride, failures).ConfigureAwait(false);
                }
                else
                {
                    Log.Debug("[ApplicationCore] stopping local application services ...");
                    await RunStepAsync(
                        () => HttpService.Resolver.GetRequiredService<IDaemonRuntimeLifecycle>()
                            .StopAsync(CancellationToken.None),
                        failures).ConfigureAwait(false);

                }

                ThrowIfCleanupFailed(failures, "One or more daemon host stop steps failed.");
            }
            finally
            {
                _operationGate.Release();
            }
        }

        private async Task CompleteDisposeAsync(TaskCompletionSource completion)
        {
            try
            {
                await DisposeCoreAsync().ConfigureAwait(false);
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        private async Task DisposeCoreAsync()
        {
            DisposeExecutionCount++;
            List<Exception> failures = [];
            if (IsServing)
                await RunStepAsync(StopAsync, failures).ConfigureAwait(false);
            await RunStepAsync(() => Lifecycle.DisposeAsync().AsTask(), failures).ConfigureAwait(false);

            Log.Debug("[Application] shutting down Http service ...");
            await RunStepAsync(() => HttpService.StopAsync(), failures).ConfigureAwait(false);
            CaptureHttpServiceDisposeResult(HttpService, _ => _disposeHttpService(), failures);

            await RunStepAsync(() => DisposeRootProviderAsync(RootProvider), failures).ConfigureAwait(false);
            ThrowIfCleanupFailed(failures, "One or more daemon host disposal steps failed.");
        }

        private bool IsStopRequested()
        {
            lock (_stopOwnershipGate)
                return _stopTask is not null;
        }
    }

    public static async Task<bool> InitAsync()
    {
        return await DaemonStartupInitialization.InitializeAsync();
    }
}
