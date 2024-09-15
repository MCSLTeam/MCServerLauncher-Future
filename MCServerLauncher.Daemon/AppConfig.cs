using MCServerLauncher.Daemon.Storage;
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
    [JsonIgnore] private static AppConfig _appConfig;

    /// <summary>
    ///     端口
    /// </summary>
    public readonly ushort Port;


    /// <summary>
    ///     Jwt-secret
    /// </summary>
    public readonly string Secret;

    public AppConfig(ushort Port, string Secret)
    {
        this.Port = Port;
        this.Secret = Secret;
    }

    private static AppConfig GetDefault()
    {
        return new AppConfig(11451, GetRandomSecret());
    }

    private static string GetRandomSecret()
    {
        return Guid.NewGuid().ToString();
    }


    public bool TrySave(string path = "config.json")
    {
        try
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
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