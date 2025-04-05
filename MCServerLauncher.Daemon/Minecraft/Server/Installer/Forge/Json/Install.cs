using Newtonsoft.Json;

namespace MCServerLauncher.Daemon.Minecraft.Server.Installer.Forge.Json;

public class Install : Specification
{
    [JsonIgnore] private Mirror? _mirror;

    [JsonIgnore] private bool _triedMirrors;
    public string Profile { get; set; }

    public string Version { get; set; }

    public string Icon { get; set; }

    public string Minecraft { get; set; }

    public string Json { get; set; }

    public string Logo { get; set; }

    public Artifact? Path { get; set; }

    public string UrlIcon { get; set; }

    public string Welcome { get; set; }

    public string? MirrorList { get; set; }

    public bool HideClient { get; set; }

    public bool HideServer { get; set; }

    public bool HideExtract { get; set; }

    public bool HideOffline { get; set; }

    public Version.Library[] Libraries { get; set; } = Array.Empty<Version.Library>();

    public List<Processor> Processors { get; set; } = new();

    public Dictionary<string, DataFile> Data { get; set; } = new();

    public string GetUrlIcon()
    {
        return UrlIcon ?? "/url.png";
    }

    public string GetWelcome()
    {
        return Welcome ?? string.Empty;
    }

    public async Task<Mirror?> GetMirror()
    {
        if (_mirror is not null) return _mirror;

        // TODO 返回从命令行参数获取的镜像，我们通常设定mirror为固定值
        // if (SimpleInstaller.Mirror != null)
        // {
        //     _mirror = new Mirror("Mirror", "", "", SimpleInstaller.Mirror.ToString());
        //     return _mirror;
        // }

        if (MirrorList is null || _triedMirrors) return _mirror;

        _triedMirrors = true;
        using var client = new HttpClient();
        var mirrors = JsonConvert.DeserializeObject<List<Mirror>>(
            await client.GetStringAsync(MirrorList),
            InstallProfileJsonSettings.Settings
        );
        _mirror = mirrors?.Count > 0 ? mirrors[new Random().Next(mirrors.Count)] : null;

        return _mirror;
    }

    public List<Processor> GetProcessors(string side)
    {
        return Processors.Where(p => p.IsSide(side)).ToList();
    }

    public Dictionary<string, string> GetData(bool client)
    {
        return Data.ToDictionary(e => e.Key, e => client ? e.Value.Client : e.Value.Server);
    }

    public class Processor
    {
        public List<string>? Sides { get; set; }

        public Artifact Jar { get; set; }

        public Artifact[] Classpath { get; set; } = Array.Empty<Artifact>();

        public string[] Args { get; set; } = Array.Empty<string>();

        public Dictionary<string, string> Outputs { get; set; } = new();

        public bool IsSide(string side)
        {
            return Sides is null || Sides.Contains(side);
        }
    }

    public class DataFile
    {
        public string Client { get; set; }

        public string Server { get; set; }
    }
}