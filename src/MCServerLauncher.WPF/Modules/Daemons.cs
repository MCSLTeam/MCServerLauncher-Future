using MCServerLauncher.DaemonClient;
using MCServerLauncher.Daemon.API.Errors;
using System.Text.Json;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RustyOptions;
using TypedDaemonClient = MCServerLauncher.DaemonClient.DaemonClient;

namespace MCServerLauncher.WPF.Modules
{
    public class DaemonsListManager
    {
        private static readonly ConcurrentQueue<KeyValuePair<string, string>> Queue = new();
        private static readonly object QueueLock = new();
        public static List<Constants.DaemonConfigModel>? Get { get; set; }

        private static string DaemonsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Configuration", "MCSL", "Daemons.json");

        /// <summary>
        ///    Initialize daemon list.
        /// </summary>
        public static void InitDaemonListConfig()
        {
            lock (QueueLock)
            {
                if (File.Exists(DaemonsPath))
                {
                    Log.Information("[Set] Found daemon list, reading");
                    Get =
                        JsonSerializer.Deserialize<List<Constants.DaemonConfigModel>>(File.ReadAllText(DaemonsPath,
                            Encoding.UTF8));
                    if (Get is null) {
                        Get = new List<Constants.DaemonConfigModel>();
                    }
                }
                else
                {
                    Log.Information("[Set] Daemon list not found, creating");
                    List<string> newList = new();
                    File.WriteAllText(
                        DaemonsPath,
                        JsonSerializer.Serialize(newList, new JsonSerializerOptions { WriteIndented = true }),
                        Encoding.UTF8
                    );
                }
            }
        }
        public static void AddDaemon(Constants.DaemonConfigModel config)
        {
            lock (QueueLock)
            {
                Get?.Add(config);
                SaveDaemonList();
            }
        }
        public static void RemoveDaemon(Constants.DaemonConfigModel config)
        {
            lock (QueueLock)
            {
                Get?.Remove(config);
                SaveDaemonList();
            }
        }
        private static void SaveDaemonList()
        {
            lock (QueueLock)
            {
                File.WriteAllText(
                    DaemonsPath,
                    JsonSerializer.Serialize(Get, new JsonSerializerOptions { WriteIndented = true }),
                    Encoding.UTF8
                );
            }
        }

        public static Constants.DaemonConfigModel MatchDaemonBySelection(string selection)
        {
            return Get?.FirstOrDefault(daemon =>
            {
                string displayName = $"{daemon.FriendlyName} [{(daemon.IsSecure ? "wss" : "ws")}://{daemon.EndPoint}:{daemon.Port}]";
                return displayName == selection;
            })!;
        }
    }

    public class DaemonsWsManager
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> ConnectionGates = new();
        private static readonly ConcurrentDictionary<string, TypedDaemonClient> Connections = new();

        public static IReadOnlyDictionary<string, TypedDaemonClient> DaemonConnections => Connections;

        public static async Task<Result<Unit, DaemonError>> Remove(
            Constants.DaemonConfigModel config,
            CancellationToken cancellationToken = default)
        {
            if (config is null)
            {
                return Result.Err<Unit, DaemonError>(new ValidationDaemonError(
                    "connection.config_required",
                    "A daemon connection configuration is required."));
            }

            var validation = CreateOptions(config);
            if (validation.IsErr(out var validationError))
                return Result.Err<Unit, DaemonError>(validationError!);

            var key = GetKey(validation.Unwrap());
            var gate = ConnectionGates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!Connections.TryRemove(key, out var existing))
                    return Result.Ok<Unit, DaemonError>(Unit.Default);

                try
                {
                    await existing.DisposeAsync().ConfigureAwait(false);
                    Log.Information("[DaemonWs] Disconnected: {0}:{1}", config.EndPoint, config.Port);
                    return Result.Ok<Unit, DaemonError>(Unit.Default);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[DaemonWs] Error while disconnecting from daemon {0}:{1}", config.EndPoint, config.Port);
                    return Result.Err<Unit, DaemonError>(new TransportDaemonError(
                        "transport.dispose_failed",
                        "The daemon client could not be disposed."));
                }
            }
            finally
            {
                gate.Release();
            }
        }

        public static async Task<Result<TypedDaemonClient, DaemonError>> Get(
            Constants.DaemonConfigModel config,
            CancellationToken cancellationToken = default)
        {
            var validation = CreateOptions(config);
            if (validation.IsErr(out var validationError))
                return Result.Err<TypedDaemonClient, DaemonError>(validationError!);

            var key = GetKey(validation.Unwrap());
            var gate = ConnectionGates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Connections.TryGetValue(key, out var existing))
                {
                    if (existing.ConnectionState is not DaemonConnectionState.Closing and not DaemonConnectionState.Closed)
                        return Result.Ok<TypedDaemonClient, DaemonError>(existing);

                    Connections.TryRemove(new KeyValuePair<string, TypedDaemonClient>(key, existing));
                    try
                    {
                        await existing.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[DaemonWs] Failed to dispose a closed daemon client {0}:{1}", config.EndPoint, config.Port);
                        return Result.Err<TypedDaemonClient, DaemonError>(new TransportDaemonError(
                            "transport.dispose_failed",
                            "The previous daemon client could not be disposed."));
                    }
                }

                var client = new TypedDaemonClient(validation.Unwrap());
                try
                {
                    var connectResult = await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    if (connectResult.IsErr(out var connectError))
                    {
                        await DisposeIgnoringFailureAsync(client, config).ConfigureAwait(false);
                        LogConnectionFailure(config, connectError!);
                        return Result.Err<TypedDaemonClient, DaemonError>(connectError!);
                    }

                    if (!Connections.TryAdd(key, client))
                    {
                        await DisposeIgnoringFailureAsync(client, config).ConfigureAwait(false);
                        return Connections.TryGetValue(key, out var winner)
                            ? Result.Ok<TypedDaemonClient, DaemonError>(winner)
                            : Result.Err<TypedDaemonClient, DaemonError>(new TransportDaemonError(
                                "transport.connection_race",
                                "The daemon connection changed while it was being created."));
                    }

                    Log.Information("[DaemonWs] Connected: {0}:{1}", config.EndPoint, config.Port);
                    return Result.Ok<TypedDaemonClient, DaemonError>(client);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await DisposeIgnoringFailureAsync(client, config).ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    await DisposeIgnoringFailureAsync(client, config).ConfigureAwait(false);
                    Log.Error(ex, "[DaemonWs] Unexpected connection failure for daemon {0}:{1}", config.EndPoint, config.Port);
                    return Result.Err<TypedDaemonClient, DaemonError>(new TransportDaemonError(
                        "transport.connect_failed",
                        "The daemon client could not establish a connection."));
                }
            }
            finally
            {
                gate.Release();
            }
        }

        public static async Task CreateAllDaemonWsAsync(CancellationToken cancellationToken = default)
        {
            var listSnapshot = DaemonsListManager.Get?.ToArray();
            if (listSnapshot is not { Length: > 0 })
                return;

            var maxConcurrency = Math.Min(Math.Max(1, Environment.ProcessorCount), 4);
            using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var tasks = listSnapshot.Select(ConnectOneAsync).ToArray();
            await Task.WhenAll(tasks).ConfigureAwait(false);

            async Task ConnectOneAsync(Constants.DaemonConfigModel config)
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var result = await Get(config, cancellationToken).ConfigureAwait(false);
                    if (result.IsErr(out var error))
                        LogConnectionFailure(config, error!);
                }
                finally
                {
                    semaphore.Release();
                }
            }
        }

        public static bool TryGetExisting(
            Constants.DaemonConfigModel config,
            out TypedDaemonClient? daemon)
        {
            var validation = CreateOptions(config);
            if (validation.IsErr(out _))
            {
                daemon = null;
                return false;
            }

            return Connections.TryGetValue(GetKey(validation.Unwrap()), out daemon);
        }

        private static Result<DaemonClientOptions, DaemonError> CreateOptions(Constants.DaemonConfigModel config)
        {
            if (config is null)
            {
                return Result.Err<DaemonClientOptions, DaemonError>(new ValidationDaemonError(
                    "connection.config_required",
                    "A daemon connection configuration is required."));
            }

            try
            {
                var scheme = config.IsSecure ? Uri.UriSchemeWss : Uri.UriSchemeWs;
                var endpoint = new UriBuilder(scheme, config.EndPoint, config.Port, "/api/v2").Uri;
                return Result.Ok<DaemonClientOptions, DaemonError>(new DaemonClientOptions(endpoint, config.Token!));
            }
            catch (Exception ex) when (ex is ArgumentException or UriFormatException)
            {
                return Result.Err<DaemonClientOptions, DaemonError>(new ValidationDaemonError(
                    "connection.config_invalid",
                    "The daemon connection configuration is invalid."));
            }
        }

        private static string GetKey(DaemonClientOptions options)
        {
            var tokenDigest = SHA256.HashData(Encoding.UTF8.GetBytes(options.Token));
            return $"{options.Endpoint.AbsoluteUri}|sha256:{Convert.ToHexString(tokenDigest)}";
        }

        private static void LogConnectionFailure(Constants.DaemonConfigModel config, DaemonError error)
        {
            Log.Error(
                "[DaemonWs] Failed to connect to daemon {0}:{1} - {2}: {3}",
                config.EndPoint,
                config.Port,
                error.Code,
                error.Message);
        }

        private static async Task DisposeIgnoringFailureAsync(
            TypedDaemonClient client,
            Constants.DaemonConfigModel config)
        {
            try
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[DaemonWs] Failed to dispose daemon client {0}:{1}", config.EndPoint, config.Port);
            }
        }
    }
}
