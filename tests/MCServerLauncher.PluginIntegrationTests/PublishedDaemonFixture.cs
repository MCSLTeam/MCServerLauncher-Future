using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using MCServerLauncher.PluginFixtures.InstanceHealth;
using MCServerLauncher.PluginFixtures.ReturnedError;
using MCServerLauncher.PluginFixtures.StartReturnedError;
using MCServerLauncher.PluginFixtures.StartHanging;
using MCServerLauncher.PluginFixtures.StartThrowing;
using MCServerLauncher.PluginFixtures.Throwing;

namespace MCServerLauncher.PluginIntegrationTests;

/// <summary>
/// Boots the published daemon executable named by MCSL_PUBLISHED_DAEMON in an isolated copy of
/// its publish directory, with a hand-written config.json on a free loopback port.
/// </summary>
internal sealed class PublishedDaemonFixture : IAsyncDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);

    private readonly string _root;
    private readonly string _daemonPath;
    private readonly bool _includeNeverCompletingDisposePlugin;
    private Process? _process;
    private Task<string>? _standardError;

    public bool GracefulStopObserved { get; private set; }

    private PublishedDaemonFixture(
        string root,
        string daemonPath,
        int port,
        string token,
        bool includeNeverCompletingDisposePlugin)
    {
        _root = root;
        _daemonPath = daemonPath;
        _includeNeverCompletingDisposePlugin = includeNeverCompletingDisposePlugin;
        WebSocketUri = new Uri($"ws://127.0.0.1:{port}/api/v2?token={Uri.EscapeDataString(token)}");
        EndpointUri = new Uri($"ws://127.0.0.1:{port}/api/v2");
        Token = token;
    }

    public Uri WebSocketUri { get; }

    public Uri EndpointUri { get; }

    public string Token { get; }

    /// <summary>
    /// The daemon's working directory; its data root is <c>daemon/</c> underneath.
    /// </summary>
    public string Root => _root;

    public static async Task<PublishedDaemonFixture> CreateAsync(
        string configuredPath,
        bool includeNeverCompletingStartPlugin = false,
        bool includeNeverCompletingDisposePlugin = false,
        bool includePlugins = true)
    {
        var source = ResolveDaemonPath(configuredPath);
        var root = Directory.CreateTempSubdirectory("mcsl-published-plugin-").FullName;
        CopyDirectory(Path.GetDirectoryName(source)!, root);
        var daemonPath = Path.Combine(root, Path.GetFileName(source));
        var port = GetUnusedLoopbackPort();
        var token = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();
        // Spec section 4: plugins load only with an explicit entries opt-in record.
        var entryIds = includePlugins
            ? new List<string>
            {
                "community.instance-health",
                "fixture.returned-error",
                "fixture.throwing",
                "fixture.start-returned-error",
                "fixture.start-throwing",
                "fixture.package-reference-consumer",
            }
            : [];
        if (includeNeverCompletingStartPlugin)
            entryIds.Add("fixture.start-never-completes");
        if (includeNeverCompletingDisposePlugin)
            entryIds.Add("fixture.late-http-cleanup");
        var entriesJson = string.Join(",", entryIds.Select(id => $"\"{id}\":{{\"enabled\":true}}"));
        var pluginGrantsJson = includeNeverCompletingDisposePlugin
            ? "\"grant_level\":\"High\",\"plugin_grants\":{\"fixture.late-http-cleanup\":[\"network.http.listen\"]},"
            : string.Empty;
        var pluginsConfig = $",\"plugins\":{{{pluginGrantsJson}\"entries\":{{{entriesJson}}}}}";
        await File.WriteAllTextAsync(
            Path.Combine(root, "config.json"),
            $$"""{"port":{{port}},"secret":"{{token}}","main_token":"{{token}}","file_download_sessions":1,"verbose":false{{pluginsConfig}}}""");

        // Skipping the plugin payloads also skips the SDK pack/publish they need, which is most of
        // this fixture's setup cost for a test that only exercises built-in RPCs.
        if (!includePlugins)
        {
            return new PublishedDaemonFixture(
                root,
                daemonPath,
                port,
                token,
                includeNeverCompletingDisposePlugin);
        }

        var pluginDirectory = Path.Combine(root, "plugins", "community.instance-health");
        Directory.CreateDirectory(pluginDirectory);
        await WritePluginAsync(
            pluginDirectory,
            "community.instance-health",
            typeof(InstanceHealthPlugin).Assembly,
            typeof(InstanceHealthPlugin).FullName!,
            "[\"event.publish\",\"instance.query\",\"rpc.register\"]");
        await WritePluginAsync(
            Path.Combine(root, "plugins", "fixture.returned-error"),
            "fixture.returned-error",
            typeof(ReturnedErrorPlugin).Assembly,
            typeof(ReturnedErrorPlugin).FullName!,
            "[\"event.publish\",\"instance.query\",\"rpc.register\"]");
        await WritePluginAsync(
            Path.Combine(root, "plugins", "fixture.throwing"),
            "fixture.throwing",
            typeof(ThrowingPlugin).Assembly,
            typeof(ThrowingPlugin).FullName!,
            "[\"event.publish\",\"instance.query\",\"rpc.register\"]");
        await WritePluginAsync(
            Path.Combine(root, "plugins", "fixture.start-returned-error"),
            "fixture.start-returned-error",
            typeof(StartReturnedErrorPlugin).Assembly,
            typeof(StartReturnedErrorPlugin).FullName!,
            "[\"event.publish\",\"instance.query\",\"rpc.register\"]");
        await WritePluginAsync(
            Path.Combine(root, "plugins", "fixture.start-throwing"),
            "fixture.start-throwing",
            typeof(StartThrowingPlugin).Assembly,
            typeof(StartThrowingPlugin).FullName!,
            "[\"event.publish\",\"instance.query\",\"rpc.register\"]");
        if (includeNeverCompletingStartPlugin)
        {
            await WritePluginAsync(
                Path.Combine(root, "plugins", "fixture.start-never-completes"),
                "fixture.start-never-completes",
                typeof(NeverCompletingStartPlugin).Assembly,
                typeof(NeverCompletingStartPlugin).FullName!,
                "[\"event.publish\",\"instance.query\",\"rpc.register\"]");
        }
        if (includeNeverCompletingDisposePlugin)
        {
            await WritePluginAsync(
                Path.Combine(root, "plugins", "fixture.late-http-cleanup"),
                "fixture.late-http-cleanup",
                typeof(LateHttpRegistrationPlugin).Assembly,
                typeof(LateHttpRegistrationPlugin).FullName!,
                "[\"network.http.listen\"]");
        }
        await WriteDocumentedPackagePluginAsync(root);

        return new PublishedDaemonFixture(
            root,
            daemonPath,
            port,
            token,
            includeNeverCompletingDisposePlugin);
    }

    public async Task StartAsync(TimeSpan? startupTimeout = null)
    {
        var startInfo = new ProcessStartInfo(_daemonPath)
        {
            WorkingDirectory = _root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        if (_includeNeverCompletingDisposePlugin)
        {
            var ownedPort = GetUnusedLoopbackPort();
            var latePort = GetUnusedLoopbackPort();
            while (latePort == ownedPort)
                latePort = GetUnusedLoopbackPort();

            startInfo.Environment["MCSL_LATE_HTTP_MODE"] = "shutdown";
            startInfo.Environment["MCSL_LATE_HTTP_OWNED_PORT"] = ownedPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
            startInfo.Environment["MCSL_LATE_HTTP_PORT"] = latePort.ToString(System.Globalization.CultureInfo.InvariantCulture);
            startInfo.Environment["MCSL_LATE_HTTP_SIGNAL_PATH"] = Path.Combine(_root, "late-http.signal");
            startInfo.Environment["MCSL_LATE_HTTP_RESULT_PATH"] = Path.Combine(_root, "late-http.result");
            startInfo.Environment["MCSL_LATE_HTTP_DISPOSE_ENTERED_PATH"] = Path.Combine(_root, "dispose.entered");
            startInfo.Environment["MCSL_LATE_HTTP_DISPOSE_RELEASE_PATH"] = Path.Combine(_root, "dispose.release");
        }

        _process = Process.Start(startInfo) ??
                   throw new InvalidOperationException("The published daemon process could not be started.");
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

    public async Task<string> StopAndReadLogsAsync(TimeSpan? shutdownTimeout = null)
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
                using var timeout = new CancellationTokenSource(shutdownTimeout ?? TimeSpan.FromSeconds(10));
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
        string features)
    {
        Directory.CreateDirectory(pluginDirectory);
        File.Copy(assembly.Location, Path.Combine(pluginDirectory, "PluginEntry.dll"));
        await File.WriteAllTextAsync(
            Path.Combine(pluginDirectory, "mcsl-plugin.json"),
            $$"""
            {
              "package": {
                "id": "{{id}}",
                "version": "1.0.0"
              },
              "entry": {
                "assembly": "PluginEntry.dll",
                "type": "{{entryType}}"
              },
              "requires": {
                "api": "[2.0.0,3.0.0)",
                "features": {{features}}
              }
            }
            """);
    }

    private static async Task WriteDocumentedPackagePluginAsync(string daemonRoot)
    {
        var repositoryRoot = FindRepositoryRoot();
        var buildRoot = Path.Combine(daemonRoot, ".documented-plugin-build");
        var configuredPackageSource = Environment.GetEnvironmentVariable("MCSL_PLUGIN_PACKAGE_SOURCE");
        var packageSource = string.IsNullOrWhiteSpace(configuredPackageSource)
            ? Path.Combine(buildRoot, "packages")
            : Path.GetFullPath(configuredPackageSource);
        var packageCache = Path.Combine(buildRoot, "package-cache");
        var publishDirectory = Path.Combine(buildRoot, "publish");

        if (string.IsNullOrWhiteSpace(configuredPackageSource))
        {
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
                "pack",
                Path.Combine(repositoryRoot, "src", "MCServerLauncher.Daemon.Plugin.Sdk", "MCServerLauncher.Daemon.Plugin.Sdk.csproj"),
                "--configuration", "Release", "--output", packageSource, "/m:1");
        }
        else
        {
            var requiredPackages = new[]
            {
                "MCServerLauncher.Common.2.0.0-preview.6.nupkg",
                "MCServerLauncher.Daemon.API.2.0.0-preview.6.nupkg",
                "MCServerLauncher.Daemon.Plugin.Sdk.2.0.0-preview.6.nupkg"
            };
            foreach (var packageName in requiredPackages)
            {
                if (!File.Exists(Path.Combine(packageSource, packageName)))
                {
                    throw new FileNotFoundException(
                        $"MCSL_PLUGIN_PACKAGE_SOURCE is missing required package '{packageName}'.",
                        Path.Combine(packageSource, packageName));
                }
            }
        }

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
        if (!File.Exists(Path.Combine(pluginDirectory, "mcsl-plugin.json")))
        {
            throw new InvalidDataException(
                "The Plugin.Sdk package consumer publish did not include mcsl-plugin.json.");
        }

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
            if (File.Exists(Path.Combine(current.FullName, "MCServerLauncher.slnx")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the MCServerLauncher repository root.");
    }
}
