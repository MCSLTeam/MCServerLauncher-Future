using Serilog;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Minecraft.Server;

public class PropertiesHandler
{
    public const string FileName = "server.properties";

    private readonly ReaderWriterLockSlim _lock = new();

    private readonly Dictionary<string, string> _properties = new();

    public string[] ServerPropertiesList
    {
        get
        {
            _lock.EnterReadLock();
            var result = _properties.Select(x => $"{x.Key}={x.Value}").ToArray();
            _lock.ExitReadLock();
            return result;
        }
    }

    public void Load(string path)
    {
        if (!File.Exists(path)) return;

        var content = string.Empty;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (IOException e)
        {
            Log.Warning(e, "[PropertiesHandler] Failed to load properties from {0}", path);
        }

        if (content.IsNullOrWhiteSpace()) return;

        _lock.EnterWriteLock();
        _properties.Clear();
        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
            var kv = line.SplitFirst('=');
            _properties.Add(kv[0], kv.Length == 2 ? kv[1] : string.Empty);
        }

        _lock.ExitWriteLock();
    }

    public string GetProperty(string key)
    {
        _lock.EnterReadLock();
        var result = _properties.GetValueOrDefault(key, string.Empty);
        _lock.ExitReadLock();
        return result;
    }
}