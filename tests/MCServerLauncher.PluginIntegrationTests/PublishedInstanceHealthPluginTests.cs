using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using MCServerLauncher.Daemon.API.Events;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.PluginFixtures.InstanceHealth;
using MCServerLauncher.PluginFixtures.ReturnedError;
using MCServerLauncher.PluginFixtures.StartReturnedError;
using MCServerLauncher.PluginFixtures.StartHanging;
using MCServerLauncher.PluginFixtures.StartThrowing;
using MCServerLauncher.PluginFixtures.Throwing;
using RustyOptions;

namespace MCServerLauncher.PluginIntegrationTests;

public sealed class PublishedInstanceHealthPluginTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SupervisedStartupTimeout = TimeSpan.FromSeconds(50);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(75);

    [Fact]
    [Trait("Category", "PublishedPlugin")]
    public async Task PublishedDaemon_LoadsHealthPluginAndServesDiscoverRpcAndEvent()
    {
        var publishedDaemon = Environment.GetEnvironmentVariable("MCSL_PUBLISHED_DAEMON");
        Assert.False(
            string.IsNullOrWhiteSpace(publishedDaemon),
            "MCSL_PUBLISHED_DAEMON must point to a published daemon executable or directory for published-plugin acceptance.");

        await using var fixture = await PublishedDaemonFixture.CreateAsync(publishedDaemon!);
        await fixture.StartAsync();

        await using var client = new global::MCServerLauncher.DaemonClient.DaemonClient(new DaemonClientOptions(
            fixture.EndpointUri,
            fixture.Token,
            RequestTimeout,
            TimeSpan.FromMilliseconds(100)));
        var connected = await client.ConnectAsync().WaitAsync(RequestTimeout);
        Assert.True(connected.IsOk(out _), connected.IsErr(out var connectionError) ? connectionError!.Message : null);

        var discover = await client.DiscoverAsync().WaitAsync(RequestTimeout);
        Assert.True(discover.IsOk(out var document), discover.IsErr(out var discoverError) ? discoverError!.Message : null);
        Assert.Contains(document!.Methods, method =>
            method.Name == "plugin.community.instance-health.rpc.get");

        var health = await client.InvokeAsync(
            InstanceHealthProtocol.Rpc,
            new InstanceHealthRequest { Scope = "all" }).WaitAsync(RequestTimeout);
        Assert.True(health.IsOk(out var healthResult), health.IsErr(out var healthError) ? healthError!.Message : null);
        Assert.Equal(0, healthResult!.TotalInstances);
        Assert.Equal(0, healthResult.RunningInstances);

        var invalidHealth = await client.InvokeAsync(
            InstanceHealthProtocol.Rpc,
            new InstanceHealthRequest { Scope = "unsupported" }).WaitAsync(RequestTimeout);
        Assert.True(invalidHealth.IsErr(out var invalidHealthError));
        Assert.Equal("plugin_scope_unsupported", invalidHealthError!.Code);

        var changed = new TaskCompletionSource<DaemonEvent<InstanceHealthChanged, Unit>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var subscriptionResult = await client.SubscribeAsync(
            InstanceHealthProtocol.Changed,
            DaemonEventFilter<Unit>.Wildcard,
            value =>
            {
                changed.TrySetResult(value);
                return Task.CompletedTask;
            }).WaitAsync(RequestTimeout);
        Assert.True(
            subscriptionResult.IsOk(out var subscription),
            subscriptionResult.IsErr(out var subscriptionError) ? subscriptionError!.Message : null);
        await using (subscription!)
        {
            var eventValue = await changed.Task.WaitAsync(EventTimeout);
            Assert.Equal(DaemonEventFieldKind.Missing, eventValue.Meta.Kind);
            Assert.Equal(0, eventValue.Data.Value.TotalInstances);
        }

        var logs = await fixture.StopAndReadLogsAsync();
        Assert.True(fixture.GracefulStopObserved, "Published daemon did not complete its console-driven graceful shutdown.");
        Assert.Contains("fixture.returned-error", logs, StringComparison.Ordinal);
        Assert.Contains("fixture_returned_error", logs, StringComparison.Ordinal);
        Assert.Contains("fixture.throwing", logs, StringComparison.Ordinal);
        Assert.Contains("configure_threw", logs, StringComparison.Ordinal);
        Assert.Contains("fixture.start-returned-error", logs, StringComparison.Ordinal);
        Assert.Contains("fixture_start_returned_error", logs, StringComparison.Ordinal);
        Assert.Contains("fixture.start-throwing", logs, StringComparison.Ordinal);
        Assert.Contains("start_threw", logs, StringComparison.Ordinal);
        Assert.Contains("fixture.package_reference_consumer.configure", logs, StringComparison.Ordinal);
        Assert.Contains("fixture.instance_health.stop", logs, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "PublishedPlugin")]
    public async Task PublishedDaemon_TimesOutNonCooperativePluginAndStillServesTheListener()
    {
        var publishedDaemon = Environment.GetEnvironmentVariable("MCSL_PUBLISHED_DAEMON");
        Assert.False(
            string.IsNullOrWhiteSpace(publishedDaemon),
            "MCSL_PUBLISHED_DAEMON must point to a published daemon executable or directory for published-plugin acceptance.");

        await using var fixture = await PublishedDaemonFixture.CreateAsync(
            publishedDaemon!,
            includeNeverCompletingStartPlugin: true);
        var startedAt = Stopwatch.GetTimestamp();
        await fixture.StartAsync(SupervisedStartupTimeout);
        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        Assert.True(
            elapsed < SupervisedStartupTimeout,
            $"Published daemon did not become ready within supervised startup timeout: {elapsed}.");

        await using var client = new global::MCServerLauncher.DaemonClient.DaemonClient(new DaemonClientOptions(
            fixture.EndpointUri,
            fixture.Token,
            RequestTimeout,
            TimeSpan.FromMilliseconds(100)));
        var connected = await client.ConnectAsync().WaitAsync(RequestTimeout);
        Assert.True(connected.IsOk(out _), connected.IsErr(out var connectionError) ? connectionError!.Message : null);

        var discover = await client.DiscoverAsync().WaitAsync(RequestTimeout);
        Assert.True(discover.IsOk(out var document), discover.IsErr(out var discoverError) ? discoverError!.Message : null);
        Assert.Contains(document!.Methods, method => method.Name == "plugin.community.instance-health.rpc.get");
        Assert.DoesNotContain(document.Methods, method => method.Name == NeverCompletingStartProtocol.Rpc.Method.Value);
        Assert.DoesNotContain(document.Events, @event => @event.Name == NeverCompletingStartProtocol.Changed.Name.Value);

        var logs = await fixture.StopAndReadLogsAsync();
        Assert.True(fixture.GracefulStopObserved, "Published daemon did not complete its console-driven graceful shutdown.");
        Assert.Contains("fixture.start-never-completes", logs, StringComparison.Ordinal);
        Assert.Contains("start_timed_out", logs, StringComparison.Ordinal);
    }

    private sealed class PublishedDaemonFixture : IAsyncDisposable
    {
        private readonly string _root;
        private readonly string _daemonPath;
        private Process? _process;
        private Task<string>? _standardError;

        public bool GracefulStopObserved { get; private set; }

        private PublishedDaemonFixture(string root, string daemonPath, int port, string token)
        {
            _root = root;
            _daemonPath = daemonPath;
            WebSocketUri = new Uri($"ws://127.0.0.1:{port}/api/v2?token={Uri.EscapeDataString(token)}");
            EndpointUri = new Uri($"ws://127.0.0.1:{port}/api/v2");
            Token = token;
        }

        public Uri WebSocketUri { get; }

        public Uri EndpointUri { get; }

        public string Token { get; }

        public static async Task<PublishedDaemonFixture> CreateAsync(
            string configuredPath,
            bool includeNeverCompletingStartPlugin = false)
        {
            var source = ResolveDaemonPath(configuredPath);
            var root = Directory.CreateTempSubdirectory("mcsl-published-plugin-").FullName;
            CopyDirectory(Path.GetDirectoryName(source)!, root);
            var daemonPath = Path.Combine(root, Path.GetFileName(source));
            var port = GetUnusedLoopbackPort();
            var token = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();
            await File.WriteAllTextAsync(
                Path.Combine(root, "config.json"),
                $$"""{"port":{{port}},"secret":"{{token}}","main_token":"{{token}}","file_download_sessions":1,"verbose":false}""");

            var pluginDirectory = Path.Combine(root, "plugins", "community.instance-health");
            Directory.CreateDirectory(pluginDirectory);
            await WritePluginAsync(
                pluginDirectory,
                "community.instance-health",
                typeof(InstanceHealthPlugin).Assembly,
                typeof(InstanceHealthPlugin).FullName!,
                "[\"rpc.register\",\"event.publish\",\"instance.query\"]");
            await WritePluginAsync(
                Path.Combine(root, "plugins", "fixture.returned-error"),
                "fixture.returned-error",
                typeof(ReturnedErrorPlugin).Assembly,
                typeof(ReturnedErrorPlugin).FullName!,
                "[\"rpc.register\"]");
            await WritePluginAsync(
                Path.Combine(root, "plugins", "fixture.throwing"),
                "fixture.throwing",
                typeof(ThrowingPlugin).Assembly,
                typeof(ThrowingPlugin).FullName!,
                "[\"rpc.register\"]");
            await WritePluginAsync(
                Path.Combine(root, "plugins", "fixture.start-returned-error"),
                "fixture.start-returned-error",
                typeof(StartReturnedErrorPlugin).Assembly,
                typeof(StartReturnedErrorPlugin).FullName!,
                "[\"rpc.register\"]");
            await WritePluginAsync(
                Path.Combine(root, "plugins", "fixture.start-throwing"),
                "fixture.start-throwing",
                typeof(StartThrowingPlugin).Assembly,
                typeof(StartThrowingPlugin).FullName!,
                "[\"rpc.register\"]");
            if (includeNeverCompletingStartPlugin)
            {
                await WritePluginAsync(
                    Path.Combine(root, "plugins", "fixture.start-never-completes"),
                    "fixture.start-never-completes",
                    typeof(NeverCompletingStartPlugin).Assembly,
                    typeof(NeverCompletingStartPlugin).FullName!,
                    "[\"rpc.register\",\"event.publish\"]");
            }
            await WriteDocumentedPackagePluginAsync(root);

            return new PublishedDaemonFixture(root, daemonPath, port, token);
        }

        public async Task StartAsync(TimeSpan? startupTimeout = null)
        {
            _process = Process.Start(new ProcessStartInfo(_daemonPath)
            {
                WorkingDirectory = _root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true
            }) ?? throw new InvalidOperationException("The published daemon process could not be started.");
            _standardError = _process.StandardError.ReadToEndAsync();

            using var timeout = new CancellationTokenSource(startupTimeout ?? StartupTimeout);
            while (!timeout.IsCancellationRequested)
            {
                if (_process.HasExited)
                {
                    var error = await _process.StandardError.ReadToEndAsync(timeout.Token);
                    throw new InvalidOperationException($"The published daemon exited during startup: {error}");
                }

                try
                {
                    using var probe = new ClientWebSocket();
                    await probe.ConnectAsync(WebSocketUri, timeout.Token);
                    await probe.CloseAsync(WebSocketCloseStatus.NormalClosure, "ready", CancellationToken.None);
                    return;
                }
                catch (WebSocketException)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200), timeout.Token);
                }
            }

            throw new TimeoutException("The published daemon did not accept an authenticated WebSocket connection in time.");
        }

        public async Task<string> StopAndReadLogsAsync()
        {
            if (_process is not null)
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        await _process.StandardInput.WriteLineAsync("exit");
                        await _process.StandardInput.FlushAsync();
                    }
                }
                catch (InvalidOperationException)
                {
                }

                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await _process.WaitForExitAsync(timeout.Token);
                    GracefulStopObserved = !_process.HasExited || _process.ExitCode == 0;
                }
                catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException)
                {
                    try
                    {
                        if (!_process.HasExited)
                            _process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }

            var standardError = _standardError is null
                ? string.Empty
                : await _standardError.WaitAsync(TimeSpan.FromSeconds(10));
            var logDirectory = Path.Combine(_root, "daemon", "logs");
            var fileLogs = Directory.Exists(logDirectory)
                ? string.Join(
                    Environment.NewLine,
                    Directory.EnumerateFiles(logDirectory, "daemon-*.txt", SearchOption.TopDirectoryOnly)
                        .OrderBy(static path => path, StringComparer.Ordinal)
                        .Select(File.ReadAllText))
                : string.Empty;
            return fileLogs + Environment.NewLine + standardError;
        }

        public async ValueTask DisposeAsync()
        {
            if (_process is not null)
            {
                try
                {
                    if (!_process.HasExited)
                        _process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await _process.WaitForExitAsync(timeout.Token);
                }
                catch (InvalidOperationException)
                {
                }
            }

            if (_standardError is not null)
            {
                try
                {
                    await _standardError.WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (TimeoutException)
                {
                }
            }

            _process?.Dispose();
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                        }
                        catch (FileNotFoundException)
                        {
                        }
                    }

                    Directory.Delete(_root, recursive: true);
                    break;
                }
                catch (IOException) when (attempt < 19)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250));
                }
                catch (UnauthorizedAccessException) when (attempt < 19)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250));
                }
            }
        }

        private static string ResolveDaemonPath(string configuredPath)
        {
            var fullPath = Path.GetFullPath(configuredPath);
            if (File.Exists(fullPath))
                return fullPath;

            if (Directory.Exists(fullPath))
            {
                var candidate = Path.Combine(fullPath, "MCServerLauncher.Daemon.exe");
                if (File.Exists(candidate))
                    return candidate;
            }

            throw new FileNotFoundException("MCSL_PUBLISHED_DAEMON must identify a published daemon executable or its directory.", configuredPath);
        }

        private static int GetUnusedLoopbackPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        private static void CopyDirectory(string source, string destination)
        {
            foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
        }

        private static async Task WritePluginAsync(
            string pluginDirectory,
            string id,
            Assembly assembly,
            string entryType,
            string capabilities)
        {
            Directory.CreateDirectory(pluginDirectory);
            File.Copy(assembly.Location, Path.Combine(pluginDirectory, "PluginEntry.dll"));
            await File.WriteAllTextAsync(
                Path.Combine(pluginDirectory, "plugin.json"),
                $$"""
                {
                  "id": "{{id}}",
                  "version": "1.0.0",
                  "entry_assembly": "PluginEntry.dll",
                  "entry_type": "{{entryType}}",
                  "api_version": "[1.0.0,2.0.0)",
                  "capabilities": {{capabilities}}
                }
                """);
        }

        private static async Task WriteDocumentedPackagePluginAsync(string daemonRoot)
        {
            var repositoryRoot = FindRepositoryRoot();
            var buildRoot = Path.Combine(daemonRoot, ".documented-plugin-build");
            var packageSource = Path.Combine(buildRoot, "packages");
            var packageCache = Path.Combine(buildRoot, "package-cache");
            var publishDirectory = Path.Combine(buildRoot, "publish");
            Directory.CreateDirectory(packageSource);

            await RunDotNetAsync(
                repositoryRoot,
                "pack",
                Path.Combine(repositoryRoot, "src", "MCServerLauncher.Common", "MCServerLauncher.Common.csproj"),
                "--configuration", "Release", "--output", packageSource, "/m:1");
            await RunDotNetAsync(
                repositoryRoot,
                "pack",
                Path.Combine(repositoryRoot, "src", "MCServerLauncher.Daemon.API", "MCServerLauncher.Daemon.API.csproj"),
                "--configuration", "Release", "--output", packageSource, "/m:1");
            await RunDotNetAsync(
                repositoryRoot,
                "publish",
                Path.Combine(repositoryRoot, "tests", "Fixtures", "Plugins", "PackageReferenceConsumer", "PackageReferenceConsumer.csproj"),
                "--configuration", "Release",
                "-p:MCSLPluginBundle=true",
                "--output", publishDirectory,
                "--packages", packageCache,
                $"/p:RestoreAdditionalProjectSources={packageSource}",
                "/m:1");

            var pluginDirectory = Path.Combine(daemonRoot, "plugins", "fixture.package-reference-consumer");
            Directory.CreateDirectory(pluginDirectory);
            CopyDirectory(publishDirectory, pluginDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(pluginDirectory, "plugin.json"),
                """
                {
                  "id": "fixture.package-reference-consumer",
                  "version": "1.0.0",
                  "entry_assembly": "PackageReferenceConsumer.dll",
                  "entry_type": "MCServerLauncher.PackageReferenceConsumer.PackageReferenceConsumerPlugin",
                  "api_version": "[1.0.0,2.0.0)",
                  "capabilities": []
                }
                """);

            Directory.Delete(buildRoot, recursive: true);
        }

        private static async Task RunDotNetAsync(string workingDirectory, params string[] arguments)
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"dotnet {string.Join(' ', arguments)} failed:{Environment.NewLine}{await output}{Environment.NewLine}{await error}");
            }
        }

        private static string FindRepositoryRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "MCServerLauncher.sln")))
                    return current.FullName;

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not find the MCServerLauncher repository root.");
        }
    }
}
