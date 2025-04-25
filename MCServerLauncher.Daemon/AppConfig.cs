using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using Newtonsoft.Json;
using Serilog;

namespace MCServerLauncher.Daemon;

/// <summary>
///     app配置文件
/// </summary>
internal class AppConfig
{
    /// <summary>
    ///     不可变单例
    /// </summary>
    [JsonIgnore] private static AppConfig? _appConfig;

    public readonly bool Verbose;

    [JsonConstructor]
    private AppConfig(ushort port, string secret, string mainToken, byte fileDownloadSessions = 3, bool verbose = false)
    {
        Port = port;
        Secret = secret;
        MainToken = mainToken;
        FileDownloadSessions = fileDownloadSessions;
        Verbose = verbose;
        Log.Information("[AppConfig] Main token: {0}", mainToken);
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

    private static AppConfig GetDefault()
    {
        return new AppConfig(11451, GenerateRandomString(), GenerateRandomString());
    }

    private static string GenerateRandomString(int length = 32)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }

    public bool ResetSecret()
    {
        Secret = GenerateRandomString();
        return TrySave();
    }

    public bool ResetMainToken()
    {
        Secret = GenerateRandomString();
        return TrySave();
    }

    public bool TrySave(string path = "config.json")
    {
        try
        {
            File.WriteAllText(path,
                JsonConvert.SerializeObject(this, Formatting.Indented, DaemonJsonSettings.Settings));
            return true;
        }
        catch (Exception e)
        {
            Log.Fatal($"Failed to save config file: {e.Message}");
            return false;
        }
    }

    private static AppConfig LoadOrDefault(string path = "config.json")
    {
        return FileManager.ReadJsonOr(path, GetDefault);
    }

    public static AppConfig Get()
    {
        return _appConfig ??= LoadOrDefault();
    }
}