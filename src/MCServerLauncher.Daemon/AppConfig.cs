using MCServerLauncher.Daemon.Plugins.Configuration;
using MCServerLauncher.Daemon.ApplicationCore.Audit;
using MCServerLauncher.Daemon.ApplicationCore.Backups;
using MCServerLauncher.Daemon.ApplicationCore.Monitoring;
using MCServerLauncher.Daemon.ApplicationCore.Operations;
using MCServerLauncher.Daemon.Serialization;
using MCServerLauncher.Daemon.Storage;
using Serilog;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace MCServerLauncher.Daemon;

/// <summary>
///     app配置文件
/// </summary>
internal class AppConfig
{
    /// <summary>
    ///     不可变单例。必须原子发布：<see cref="GetDefault" /> 会生成随机 Secret/MainToken，
    ///     两个线程各自加载出的实例互不通用（签发与校验用不同 secret，且 Reset 可能写在被丢弃的实例上）。
    /// </summary>
    private static readonly Lazy<AppConfig> LazyAppConfig = new(() => LoadOrDefault());

    public readonly bool Verbose;

    [JsonConstructor]
    internal AppConfig(
        ushort port,
        string secret,
        string mainToken,
        int fileDownloadSessions = 3,
        bool verbose = false,
        DaemonSecurityConfig? security = null,
        DaemonPluginsConfig? plugins = null,
        DaemonOperationsConfig? operations = null,
        DaemonBackupConfig? backups = null,
        DaemonAuditConfig? audit = null,
        DaemonMonitoringConfig? monitoring = null)
    {
        Port = port;
        Secret = secret;
        MainToken = mainToken;
        FileDownloadSessions = fileDownloadSessions;
        Verbose = verbose;
        // Cold-merge defaults for missing security/plugins/operations sections so existing config.json
        // files keep working without forcing a first-run Q&A.
        Security = security ?? new DaemonSecurityConfig();
        Plugins = plugins ?? DaemonPluginsConfig.Default;
        Operations = operations ?? new DaemonOperationsConfig();
        Backups = backups ?? new DaemonBackupConfig();
        Audit = audit ?? new DaemonAuditConfig();
        Monitoring = monitoring ?? new DaemonMonitoringConfig();
        _ = PluginAdmissionPolicy.ParseGrantLevel(Plugins.GrantLevel);
        Operations.Validate();
        Backups.Validate();
        Audit.Validate();
        Monitoring.Validate();
        Log.Information("[AppConfig] Loaded configuration for port {Port}.", port);
    }

    /// <summary>
    ///     主令牌
    /// </summary>
    public string MainToken { get; private set; }

    /// <summary>
    ///     端口
    /// </summary>
    public ushort Port { get; }


    /// <summary>
    ///     Jwt-secret
    /// </summary>
    public string Secret { get; private set; }

    public int FileDownloadSessions { get; private set; }

    public DaemonSecurityConfig Security { get; private set; }

    public DaemonPluginsConfig Plugins { get; private set; }

    public DaemonOperationsConfig Operations { get; private set; }

    public DaemonBackupConfig Backups { get; private set; }

    public DaemonAuditConfig Audit { get; private set; }

    public DaemonMonitoringConfig Monitoring { get; private set; }

    internal static JsonTypeInfo<AppConfig> PersistenceWriteIndentedTypeInfo { get; } = ResolvePersistenceWriteIndentedTypeInfo();

    private static AppConfig GetDefault()
    {
        return new AppConfig(11452, GenerateRandomString(), GenerateRandomString());
    }

    private static string GenerateRandomString(int length = 32)
    {
        // 32 bytes -> 64 hex chars when length is 32; keep length as character count.
        var byteCount = Math.Max(1, (length + 1) / 2);
        Span<byte> bytes = stackalloc byte[byteCount];
        RandomNumberGenerator.Fill(bytes);
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return hex.Length <= length ? hex : hex[..length];
    }

    public bool ResetSecret()
    {
        Secret = GenerateRandomString();
        return TrySave();
    }

    public bool ResetMainToken()
    {
        MainToken = GenerateRandomString();
        return TrySave();
    }

    private static string ConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

    private static JsonTypeInfo<AppConfig> ResolvePersistenceWriteIndentedTypeInfo()
    {
        return DaemonPersistenceJsonBoundary.StjWriteIndentedOptions.GetTypeInfo(typeof(AppConfig)) as JsonTypeInfo<AppConfig>
               ?? throw new NotSupportedException(
                   $"Daemon persistence boundary does not provide source-generated JsonTypeInfo for {typeof(AppConfig).FullName}.");
    }

    public bool TrySave(string? path = null)
    {
        path ??= ConfigPath;
        try
        {
            FileManager.WriteJsonAndBackup(path, this);
            return true;
        }
        catch (Exception e)
        {
            Log.Error("Failed to save config file: {Message}", e.Message);
            return false;
        }
    }

    private static AppConfig LoadOrDefault(string? path = null)
    {
        path ??= ConfigPath;
        return FileManager.ReadJsonOr(path, GetDefault);
    }

    public static AppConfig Get()
    {
        return LazyAppConfig.Value;
    }
}
