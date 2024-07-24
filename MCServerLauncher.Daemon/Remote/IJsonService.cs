namespace MCServerLauncher.Daemon.Remote;

public interface IJsonService
{
    string Serialize(object obj);
    T Deserialize<T>(string json);
}