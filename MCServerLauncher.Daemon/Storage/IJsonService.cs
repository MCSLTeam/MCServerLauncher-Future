using Newtonsoft.Json;

namespace MCServerLauncher.Daemon.Storage;

public interface IJsonService
{
    string Serialize(object obj);
    T Deserialize<T>(string json);
}