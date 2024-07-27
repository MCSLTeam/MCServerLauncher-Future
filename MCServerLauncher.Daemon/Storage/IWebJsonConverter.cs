using Newtonsoft.Json;

namespace MCServerLauncher.Daemon.Storage;

public interface IWebJsonConverter
{
    string Serialize(object obj);
    T Deserialize<T>(string json);
}