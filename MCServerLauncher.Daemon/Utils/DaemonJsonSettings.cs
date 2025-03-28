using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Daemon.Remote.Authentication.PermissionSystem;
using Newtonsoft.Json;

namespace MCServerLauncher.Daemon.Utils;

public static class DaemonJsonSettings
{
    public static readonly JsonSerializerSettings Settings = CreateSettings();

    private static JsonSerializerSettings CreateSettings()
    {
        var settings = new JsonSerializerSettings(JsonSettings.Settings);
        settings.Converters.Add(new Permission.PermissionJsonConverter());
        return settings;
    }
}