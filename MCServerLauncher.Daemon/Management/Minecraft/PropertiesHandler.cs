using MCServerLauncher.Daemon.Utils;
using Serilog;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Management.Minecraft;

public class PropertiesHandler
{
    public const string FileName = "server.properties";
    private readonly FileChange _fileChange;

    private readonly ReaderWriterLockSlim _lock = new();

    private readonly Dictionary<string, string> _properties = new();

    public PropertiesHandler(string path)
    {
        _fileChange = new FileChange(path);
    }

    public event Action<IReadOnlyDictionary<string, string>>? OnPropertiesUpdated;

    public void Load()
    {
        if (!_fileChange.HasChanged() || !_fileChange.Exists) return;

        var content = string.Empty;
        try
        {
            content = File.ReadAllText(_fileChange.Path);
        }
        catch (IOException e)
        {
            Log.Warning(e, "[PropertiesHandler] Failed to load properties from {0}", _fileChange.Path);
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

        _lock.EnterReadLock();
        OnPropertiesUpdated?.Invoke(_properties);
        _lock.ExitReadLock();
    }

    public string GetProperty(string key, bool update = true)
    {
        if (update) Load();

        _lock.EnterReadLock();
        var result = _properties.GetValueOrDefault(key, string.Empty);
        _lock.ExitReadLock();
        return result;
    }

    public Dictionary<string, string> GetProperties(bool update = true)
    {
        if (update) Load();

        _lock.EnterReadLock();
        var result = new Dictionary<string, string>(_properties);
        _lock.ExitReadLock();
        return result;
    }
}