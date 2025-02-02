using MCServerLauncher.Daemon.Utils;
using Newtonsoft.Json;

namespace MCServerLauncher.Daemon.Storage;

/// <summary>
///     添加C#的BigCamelCase与Json的snake_case的互转，以及多种自定义Json转换器
/// </summary>
public class WebJsonConverter : IWebJsonConverter
{
    public string Serialize(object obj)
    {
        return JsonConvert.SerializeObject(obj, Formatting.Indented, JsonSettings.Settings);
    }

    public T? Deserialize<T>(string json)
    {
        return JsonConvert.DeserializeObject<T>(json, JsonSettings.Settings);
    }

    public JsonSerializer GetSerializer()
    {
        return JsonSerializer.Create(JsonSettings.Settings);
    }
}